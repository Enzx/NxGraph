using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncHistoryState"/>: wraps a child graph as a
/// composite node with <b>history</b>. When the child fails (or is cancelled) and the parent
/// later re-enters the composite — e.g. after a failure edge and a <c>Goto</c> back —
/// execution resumes at the child's last-active node instead of restarting from its start
/// node. A child that <i>completed</i> restarts from the top on re-entry.
/// <para>
/// This is <b>shallow</b> history for the wrapped graph: the resumed node itself restarts
/// fresh. <b>Deep</b> history falls out of composition — wrap the nested subgraphs inside the
/// child with their own history composites and each level resumes its own position.
/// </para>
/// <para>
/// <see cref="ParallelStepMode"/> decides how child nodes map onto ticks:
/// <see cref="ParallelStepMode.RunToJoin"/> completes the child run inside one
/// <see cref="Execute"/> call (usable from both runtimes via the sync-logic adapter);
/// <see cref="ParallelStepMode.RoundPerTick"/> advances one child node per call and returns
/// <see cref="Result.InProgress"/> in between (sync runtime only — the async loop rejects
/// node-level <see cref="Result.InProgress"/>). A parent <c>Suspend()</c> between
/// RoundPerTick ticks does not capture composite-internal position (flat snapshots are
/// primitives-only); resuming that on a fresh graph restarts the visit. Use the parent's
/// <c>SuspendDeep()</c>/<c>ResumeDeep(...)</c> to carry the mid-visit position and the
/// remembered history across a durable boundary (see <see cref="ISuspendableComposite"/>).
/// </para>
/// </summary>
public sealed class HistoryState : ILogic, ISubGraphProvider, IBlackboardSettable, ISuspendableComposite
{
    /// <summary>The wrapped child machine.</summary>
    public StateMachine Child { get; }

    /// <summary>How child nodes map onto <see cref="Execute"/> calls.</summary>
    public ParallelStepMode Mode { get; }

    // Visit bookkeeping for RoundPerTick, which spans many Execute() calls. The first call of
    // a visit applies the history semantics; reaching a terminal result (or an escaping child
    // exception) clears it so the next visit re-applies them.
    private bool _inFlight;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs => [Child.Graph];

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        // The recursive stamping walk stops at IBlackboardSettable nodes — forward to the
        // child machine so it validates against its own graph's declarations at stamp time
        // (uniform with the async history composite and the parallel composites).
        ((IBlackboardSettable)Child).SetBlackboards(in context);
    }

    public HistoryState(Graph child, ParallelStepMode mode = ParallelStepMode.RunToJoin)
    {
        Guard.NotNull(child, nameof(child));
        Child = new StateMachine(child);
        // Manual keeps the child's position (current node) intact after a failure instead of
        // auto-resetting to the start — that position is exactly the history to resume from.
        Child.SetRestartPolicy(RestartPolicy.Manual);
        Mode = mode;
    }

    // ── ISuspendableComposite ─────────────────────────────────────────────
    // Two things persist across Execute() calls: the RoundPerTick visit flag (_inFlight)
    // and the child machine's position — mid-run between ticks, or terminal-Failed history
    // under RestartPolicy.Manual. Capturing the child's deep snapshot preserves exactly the
    // shape the re-entry lift in Execute consumes, so no new lift logic is needed on resume.

    CompositeSnapshot ISuspendableComposite.SuspendComposite(int nodeIndex)
    {
        return new CompositeSnapshot(nodeIndex, _inFlight, Done: [], Children: [Child.SuspendDeep()]);
    }

    void ISuspendableComposite.ResumeComposite(CompositeSnapshot snapshot)
    {
        DeepSnapshots.ValidateShape(snapshot, expectedChildren: 1, expectedDoneBits: 0);
        _inFlight = snapshot.InFlight;
        DeepSnapshots.ResumeChild(Child, snapshot, 0);
    }

    public Result Execute()
    {
        if (!_inFlight)
        {
            ExecutionStatus status = Child.Status;
            switch (status)
            {
                case ExecutionStatus.Failed:
                case ExecutionStatus.Cancelled:
                {
                    // Re-enter at the recorded position: lift the terminal snapshot back into
                    // a running stepped session with a fresh retry budget for the resumed node.
                    StateMachineSnapshot history = Child.Suspend();
                    Child.Resume(history with
                    {
                        Status = ExecutionStatus.Running,
                        MidRun = true,
                        Attempts = 0,
                    });
                    break;
                }
                case ExecutionStatus.Completed:
                    Child.Reset();
                    break;
            }

            _inFlight = true;
        }

        Result result;
        try
        {
            result = Child.Execute();
            if (Mode == ParallelStepMode.RunToJoin)
            {
                while (result == Result.InProgress)
                {
                    result = Child.Execute();
                }
            }
        }
        catch
        {
            // The child threw out of its node logic. Drop the visit so the next entry
            // re-applies the history semantics; the exception itself propagates into the
            // parent machine's normal failure handling.
            _inFlight = false;
            throw;
        }

        if (!result.IsCompleted)
        {
            return Result.InProgress;
        }

        _inFlight = false;
        return result;
    }
}
