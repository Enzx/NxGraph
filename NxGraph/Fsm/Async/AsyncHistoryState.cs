using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Wraps a child graph as a composite node with <b>history</b>: when the child fails (or is
/// cancelled) and the parent later re-enters the composite — e.g. after a failure edge and a
/// <c>Goto</c> back — execution resumes at the child's last-active node instead of restarting
/// from its start node. A child that <i>completed</i> restarts from the top on re-entry.
/// <para>
/// This is <b>shallow</b> history for the wrapped graph: the resumed node itself restarts
/// fresh. <b>Deep</b> history falls out of composition — wrap the nested subgraphs inside the
/// child with their own history composites and each level resumes its own position.
/// </para>
/// <para>
/// The happy path steps the child at 0 B; the resume bookkeeping allocates a small snapshot
/// only on the failure-recovery path, which is off the hot loop by definition.
/// </para>
/// </summary>
public sealed class AsyncHistoryState : IAsyncLogic, ISubGraphProvider, IBlackboardSettable
{
    /// <summary>The wrapped child machine.</summary>
    public AsyncStateMachine Child { get; }

    IEnumerable<Graph> ISubGraphProvider.SubGraphs => [Child.Graph];

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        // The recursive stamping walk stops at IBlackboardSettable nodes — forward to the
        // child machine so it validates against its own graph's declarations at stamp time
        // (uniform with plain .SubGraph nesting and the dynamic parallel composite).
        ((IBlackboardSettable)Child).SetBlackboards(in context);
    }

    public AsyncHistoryState(Graph child)
    {
        Guard.NotNull(child, nameof(child));
        Child = new AsyncStateMachine(child);
        // Manual keeps the child's position (current node) intact after a failure instead of
        // auto-resetting to the start — that position is exactly the history to resume from.
        Child.SetRestartPolicy(RestartPolicy.Manual);
    }

    public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        ExecutionStatus status = Child.Status;
        switch (status)
        {
            case ExecutionStatus.Failed:
            case ExecutionStatus.Cancelled:
            {
                // Re-enter at the recorded position: lift the terminal snapshot back into a
                // running stepped session with a fresh retry budget for the resumed node.
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
                await Child.Reset(ct).ConfigureAwait(false);
                break;
        }

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await Child.StepAsync(ct).ConfigureAwait(false);
        }

        return result;
    }
}
