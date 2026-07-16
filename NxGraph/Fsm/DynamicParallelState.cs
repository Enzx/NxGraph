using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Sync twin of <see cref="Async.AsyncDynamicParallelState"/> (runtime parity): at the start
/// of each visit a selector reads the machine-bound <see cref="BlackboardContext"/> and
/// returns a <see cref="RegionMask"/> deciding which region graphs run. Only selected regions
/// are stepped; unselected regions are never stepped and never reset. Join semantics match
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
public sealed class DynamicParallelState : ILogic, ISubGraphProvider, IBlackboardSettable
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
    {
        _selector = Guard.NotNull(selector, nameof(selector));
        Guard.NotNull(regions, nameof(regions));
        if (regions.Length == 0)
        {
            throw new ArgumentException("At least one region is required.", nameof(regions));
        }

        if (regions.Length > 64)
        {
            throw new ArgumentException(
                $"Dynamic parallel composites support at most 64 regions ({regions.Length} given) — " +
                "the selection mask is a single ulong.", nameof(regions));
        }

        _mode = mode;
        _regions = new StateMachine[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            _regions[i] = new StateMachine(regions[i]);
        }

        _done = new bool[regions.Length];
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
        return _anyFailed ? Result.Failure : Result.Success;
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
