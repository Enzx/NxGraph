using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncParallelState"/>: orthogonal (AND-state)
/// regions via cooperative interleaving. Each region is a child graph run by its own sync
/// <see cref="StateMachine"/>; a <b>round</b> advances every still-running region by one node.
/// Rounds repeat until all regions reach a terminal result (the join). The composite returns
/// <see cref="Result.Success"/> only when every region succeeded; if any region failed it
/// returns <see cref="Result.Failure"/> after the join, which then flows through the parent's
/// unified fault model (failure edges, retries).
/// <para>
/// <see cref="ParallelStepMode"/> decides how rounds map onto ticks:
/// <see cref="ParallelStepMode.RunToJoin"/> completes the whole join inside one
/// <see cref="Execute"/> call; <see cref="ParallelStepMode.RoundPerTick"/> runs one round per
/// call and returns <see cref="Result.InProgress"/> in between, so region progress aligns 1:1
/// with game-loop frames. <c>RoundPerTick</c> is only valid under the sync
/// <see cref="StateMachine"/> (the async runtime rejects node-level
/// <see cref="Result.InProgress"/>); <c>RunToJoin</c> works under both runtimes via the
/// sync-logic adapter.
/// </para>
/// <para>
/// Like the async composite, this is deliberately not thread-concurrent (see the scope
/// decision on <see cref="Async.AsyncParallelState"/>); region graphs must contain sync
/// (<see cref="ILogic"/>) node logic, as with any sync-run graph.
/// </para>
/// </summary>
public sealed class ParallelState : ILogic, ISubGraphProvider, IBlackboardSettable, ISuspendableComposite
{
    private readonly StateMachine[] _regions;
    private readonly bool[] _done;
    private readonly ParallelStepMode _mode;

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        // The recursive stamping walk stops at IBlackboardSettable nodes — forward to the
        // region machines ourselves (each validates against its own graph's declarations),
        // matching DynamicParallelState.
        for (int i = 0; i < _regions.Length; i++)
        {
            ((IBlackboardSettable)_regions[i]).SetBlackboards(in context);
        }
    }

    // Join bookkeeping for RoundPerTick, which spans many Execute() calls. The first call of
    // a visit resets it; reaching the join (or an escaping region exception) clears it.
    private bool _inFlight;
    private int _remaining;
    private bool _anyFailed;

    /// <summary>The region machines.</summary>
    public IReadOnlyList<StateMachine> Regions => _regions;

    /// <summary>How region rounds map onto <see cref="Execute"/> calls.</summary>
    public ParallelStepMode Mode => _mode;

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

    public ParallelState(ParallelStepMode mode, params Graph[] regions)
    {
        Guard.NotNull(regions, nameof(regions));
        if (regions.Length == 0)
        {
            throw new ArgumentException("At least one region is required.", nameof(regions));
        }

        _mode = mode;
        _regions = new StateMachine[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            _regions[i] = new StateMachine(regions[i]);
            // Manual keeps a finished region's terminal status readable between the join and
            // the next visit (instead of auto-resetting to Ready the instant it finishes) —
            // deep suspend recomputes the join's failure aggregation from those statuses.
            // The fresh-visit reset happens in ResetTerminalRegions instead, so visits still
            // start clean exactly as they did under the regions' old auto-reset.
            _regions[i].SetRestartPolicy(RestartPolicy.Manual);
        }

        _done = new bool[regions.Length];
    }

    // ── ISuspendableComposite ─────────────────────────────────────────────
    // RoundPerTick join bookkeeping spans Execute() calls: the visit flag, the per-region
    // done bits, and each region machine's own position persist between ticks. _remaining
    // and _anyFailed are derivable (regions minus done popcount / any region Failed) and are
    // deliberately not captured — they are recomputed on resume so the wire shape cannot
    // self-contradict.

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
        // Fresh visit: regions left terminal by the previous visit start over. Mid-run
        // regions (possible after an escaping exception dropped the join) are left as-is,
        // matching the old behavior where they continued from their positions.
        for (int i = 0; i < _regions.Length; i++)
        {
            if (_regions[i].Status is ExecutionStatus.Completed or ExecutionStatus.Failed
                or ExecutionStatus.Cancelled)
            {
                _regions[i].Reset();
            }
        }
    }

    public Result Execute()
    {
        if (!_inFlight)
        {
            ResetTerminalRegions();
            for (int i = 0; i < _done.Length; i++)
            {
                _done[i] = false;
            }

            _remaining = _regions.Length;
            _anyFailed = false;
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
            // a fresh pass instead of resuming stale bookkeeping; the exception itself
            // propagates into the parent machine's normal failure handling.
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
}
