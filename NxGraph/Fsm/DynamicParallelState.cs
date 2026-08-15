using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Sync twin of <see cref="Async.AsyncDynamicParallelState"/> (runtime parity): at the start
/// of each visit a selector reads the machine-bound <see cref="BlackboardContext"/> and
/// returns a <see cref="RegionMask"/> deciding which region graphs run. Only selected regions
/// are stepped; unselected regions are never stepped (a region left terminal by an earlier
/// visit is reset on the next visit's first tick, selected or not, so stale results never
/// leak across visits). Join semantics match
/// <see cref="ParallelState"/>: <see cref="Result.Success"/> only when every selected region
/// succeeded, else <see cref="Result.Failure"/> through the parent's unified fault model.
/// <para>
/// <see cref="ParallelStepMode"/> maps rounds onto ticks exactly as in
/// <see cref="ParallelState"/> — <see cref="ParallelStepMode.RoundPerTick"/> evaluates the
/// selector on the visit's first tick only; the selected set is fixed until the join. An empty
/// mask is a vacuous join (immediate <see cref="Result.Success"/>); mask bits at or above the
/// region count fail loudly. Selectors run once per visit — keep them allocation-free
/// (compose with <see cref="RegionMask.Bit"/> and <c>|</c>, or precompute masks at setup).
/// </para>
/// </summary>
public sealed class DynamicParallelState : ILogic, ISubGraphProvider, IBlackboardSettable,
    ISuspendableComposite, IOwnedBoardProvider
{
    private readonly Func<BlackboardContext, RegionMask> _selector;
    private readonly StateMachine[] _regions;
    private readonly bool[] _done;
    private readonly ParallelStepMode _mode;
    private BlackboardContext _blackboards;

    // Join bookkeeping for RoundPerTick, which spans many Execute() calls. The first call of
    // a visit evaluates the selector and resets it; reaching the join (or an escaping region
    // exception) clears it.
    private bool _inFlight;
    private int _remaining;
    private bool _anyFailed;

    /// <summary>The region machines (selected or not).</summary>
    public IReadOnlyList<StateMachine> Regions => _regions;

    /// <summary>How region rounds map onto <see cref="Execute"/> calls.</summary>
    public ParallelStepMode Mode => _mode;

    /// <summary>
    /// The region selector this composite was constructed with. Exposed (like
    /// <see cref="Regions"/> and <see cref="Mode"/>) so serializers can map the delegate
    /// back to a registered key — delegates themselves cannot ride a wire payload.
    /// </summary>
    public Func<BlackboardContext, RegionMask> Selector => _selector;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs
    {
        get
        {
            foreach (StateMachine region in _regions)
            {
                yield return region.Graph;
            }
        }
    }

    public DynamicParallelState(ParallelStepMode mode, Func<BlackboardContext, RegionMask> selector,
        params Graph[] regions)
        : this(mode, selector, ParallelComposites.ToRegions(regions))
    {
    }

    /// <summary>
    /// Region-entry overload: each <see cref="ParallelRegion"/> pairs its graph with an
    /// optional per-region ports declaration (see <see cref="SubGraphPorts"/>). A dynamically
    /// deselected region applies no inputs and copies no outputs for that execution.
    /// </summary>
    public DynamicParallelState(ParallelStepMode mode, Func<BlackboardContext, RegionMask> selector,
        ParallelRegion[] regions)
        : this(mode, selector, ParallelComposites.BuildSyncRegions(regions, unbound: false))
    {
    }

    private DynamicParallelState(ParallelStepMode mode, Func<BlackboardContext, RegionMask> selector,
        StateMachine[] regions)
    {
        _selector = Guard.NotNull(selector, nameof(selector));
        ParallelComposites.ValidateDynamicRegionCount(regions.Length, nameof(regions));

        _mode = mode;
        _regions = regions;
        foreach (StateMachine region in regions)
        {
            // Manual keeps a finished region's terminal status readable between the join and
            // the next visit — deep suspend recomputes the join's failure aggregation from
            // those statuses. The fresh-visit reset happens in ResetTerminalRegions instead.
            region.SetRestartPolicy(RestartPolicy.Manual);
        }

        _done = new bool[regions.Length];
    }

    /// <summary>
    /// Creates a composite whose region machines carry name-bound ports — the deserialization
    /// rebind form (see <see cref="StateMachine.Unbound"/>). <paramref name="ports"/> aligns
    /// with <paramref name="regions"/> by index; <see langword="null"/> entries (or a
    /// <see langword="null"/> array) mean the shared-board default.
    /// </summary>
    public static DynamicParallelState Unbound(ParallelStepMode mode,
        Func<BlackboardContext, RegionMask> selector, Graph[] regions, SubGraphPorts?[]? ports) =>
        new(mode, selector,
            ParallelComposites.BuildSyncRegions(ParallelComposites.ToRegions(regions, ports), unbound: true));

    IEnumerable<OwnedBoardEntry> IOwnedBoardProvider.EnumerateOwnedBoards(int nodeIndex) =>
        ParallelComposites.EnumerateOwnedBoards(nodeIndex, _regions);

    // ── ISuspendableComposite ─────────────────────────────────────────────
    // Same durable state as ParallelState, with one addition that makes Done capture (not
    // derivation) mandatory: a deselected region is done-without-terminal-status (its machine
    // never ran), so child statuses alone cannot reconstruct the selection projected into
    // _done at the visit's first tick. The selector is NOT re-evaluated on resume — the
    // captured Done bits carry the visit's fixed selection.

    CompositeSnapshot ISuspendableComposite.SuspendComposite(int nodeIndex)
    {
        StateMachineDeepSnapshot[] children = new StateMachineDeepSnapshot[_regions.Length];
        for (int i = 0; i < _regions.Length; i++)
        {
            children[i] = _regions[i].SuspendDeep();
        }

        return new CompositeSnapshot(nodeIndex, _inFlight, (bool[])_done.Clone(), children);
    }

    void ISuspendableComposite.ResumeComposite(CompositeSnapshot snapshot)
    {
        DeepSnapshots.ValidateShape(snapshot, expectedChildren: _regions.Length, expectedDoneBits: _done.Length);
        _inFlight = snapshot.InFlight;
        for (int i = 0; i < _done.Length; i++)
        {
            _done[i] = snapshot.Done[i];
        }

        for (int i = 0; i < _regions.Length; i++)
        {
            DeepSnapshots.ResumeChild(_regions[i], snapshot, i);
        }

        RecomputeJoinBookkeeping();
    }

    private void RecomputeJoinBookkeeping()
    {
        // _remaining and _anyFailed are deliberately not captured — recompute them so the
        // wire shape cannot self-contradict. A deselected region is done with a non-terminal
        // machine status and therefore never counts as failed.
        _remaining = 0;
        _anyFailed = false;
        for (int i = 0; i < _regions.Length; i++)
        {
            if (!_done[i])
            {
                _remaining++;
            }
            else if (_regions[i].Status == ExecutionStatus.Failed)
            {
                _anyFailed = true;
            }
        }
    }

    private void ResetTerminalRegions()
    {
        // Fresh visit: regions left terminal by the previous visit start over (selected or
        // not this time). Mid-run regions are left as-is, matching ParallelState.
        for (int i = 0; i < _regions.Length; i++)
        {
            if (_regions[i].Status is ExecutionStatus.Completed or ExecutionStatus.Failed
                or ExecutionStatus.Cancelled)
            {
                _regions[i].Reset();
            }
        }
    }

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        // The recursive stamping walk stops at IBlackboardSettable nodes — forward to the
        // region machines ourselves (each validates against its own graph's declarations).
        _blackboards = context;
        for (int i = 0; i < _regions.Length; i++)
        {
            ((IBlackboardSettable)_regions[i]).SetBlackboards(in context);
        }
    }

    public Result Execute()
    {
        if (!_inFlight)
        {
            ResetTerminalRegions();
            RegionMask selected = _selector(_blackboards);
            ValidateMask(selected);

            _remaining = 0;
            _anyFailed = false;
            for (int i = 0; i < _regions.Length; i++)
            {
                bool isSelected = selected.Contains(i);
                _done[i] = !isSelected; // unselected regions are "done" before the first round
                if (isSelected)
                {
                    _remaining++;
                }
            }

            if (_remaining == 0)
            {
                return Result.Success; // vacuous join; no in-flight state to keep
            }

            _inFlight = true;
        }

        try
        {
            if (_mode == ParallelStepMode.RunToJoin)
            {
                while (_remaining > 0)
                {
                    RunRound();
                }
            }
            else
            {
                RunRound();
                if (_remaining > 0)
                {
                    return Result.InProgress;
                }
            }
        }
        catch
        {
            // A region threw out of its node logic. Drop the join so the next visit starts
            // a fresh pass (with a fresh selector evaluation) instead of resuming stale
            // bookkeeping; the exception propagates into the parent's failure handling.
            _inFlight = false;
            throw;
        }

        _inFlight = false;
        RecopyOutputsInRegionOrder();
        return _anyFailed ? Result.Failure : Result.Success;
    }

    private void RecopyOutputsInRegionOrder()
    {
        // Several regions may target the same parent output key: re-applying the terminal
        // copies in region order at the join makes that conflict resolve by region order (the
        // documented rule) instead of by completion order. Values are unchanged since each
        // region's terminal, so this is idempotent for every non-conflicting declaration.
        // Deselected regions never reached a terminal this visit and are skipped via their
        // status (Manual policy keeps it readable until the next visit's reset).
        for (int i = 0; i < _regions.Length; i++)
        {
            if (_regions[i].Status is ExecutionStatus.Completed or ExecutionStatus.Failed
                or ExecutionStatus.Cancelled)
            {
                _regions[i].RecopyOutputPortsAtJoin();
            }
        }
    }

    private void RunRound()
    {
        for (int i = 0; i < _regions.Length; i++)
        {
            if (_done[i])
            {
                continue;
            }

            Result step = _regions[i].Execute();
            if (!step.IsCompleted)
            {
                continue;
            }

            _done[i] = true;
            _remaining--;
            if (step.IsFailure)
            {
                _anyFailed = true;
            }
        }
    }

    private void ValidateMask(RegionMask mask)
    {
        if (_regions.Length < 64 && mask.Bits >> _regions.Length != 0)
        {
            throw new InvalidOperationException(
                $"Selector returned {mask} with bits at or above the region count ({_regions.Length}).");
        }
    }
}
