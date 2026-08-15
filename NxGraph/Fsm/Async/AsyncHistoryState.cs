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
public sealed class AsyncHistoryState : IAsyncLogic, ISubGraphProvider, IBlackboardSettable,
    ISuspendableComposite, IOwnedBoardProvider
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

    /// <param name="child">The child graph to wrap.</param>
    /// <param name="outcomeCodeKey">Optional Graph- or Global-scoped key the composite writes the
    /// child's terminal outcome code to, through the stamped parent-level context, the moment
    /// the child run reaches a genuine terminal — success and failure terminals alike, before
    /// the composite returns its own <see cref="Result"/>. A terminal that declared no outcome
    /// writes <c>0</c> — hosts route on non-zero codes. A lift-back re-entry publishes nothing
    /// until the resumed session actually ends. Node-scoped keys are rejected here.</param>
    /// <param name="outcomeNameKey">Optional key for the outcome's registered display name;
    /// same timing as <paramref name="outcomeCodeKey"/>, empty string when none is declared.
    /// Either key may be declared without the other.</param>
    /// <param name="ports">Optional ports declaration (see <see cref="SubGraphPorts"/>): the
    /// child runs on its own board created from its declared Graph schema. Inputs apply at
    /// every <b>fresh</b> child start; a lift-back re-entry applies nothing — the board, like
    /// the position, is the survived state. Outputs copy when the child session genuinely
    /// ends, success and failure alike.</param>
    public AsyncHistoryState(Graph child, BlackboardKey<int>? outcomeCodeKey = null,
        BlackboardKey<string>? outcomeNameKey = null, SubGraphPorts? ports = null)
        : this(CreateChild(child, outcomeCodeKey, outcomeNameKey, ports))
    {
    }

    private AsyncHistoryState(AsyncStateMachine child)
    {
        Child = child;
        // Manual keeps the child's position (current node) intact after a failure instead of
        // auto-resetting to the start — that position is exactly the history to resume from.
        Child.SetRestartPolicy(RestartPolicy.Manual);
    }

    private static AsyncStateMachine CreateChild(Graph child, BlackboardKey<int>? outcomeCodeKey,
        BlackboardKey<string>? outcomeNameKey, SubGraphPorts? ports)
    {
        Guard.NotNull(child, nameof(child));
        // The child machine owns the publish and the ports: it writes the keys and copies the
        // outputs at its own genuine terminals, and applies inputs only at its own fresh run
        // starts — which is exactly the history timing: a lift-back re-entry resumes a
        // session, applies nothing, and publishes only when that session ends.
        return new AsyncStateMachine(child, null, outcomeCodeKey, outcomeNameKey, ports);
    }

    /// <summary>
    /// Creates a history composite with name-bound outcome keys and ports — the
    /// deserialization rebind form (see <see cref="AsyncStateMachine.Unbound"/>). A
    /// <see langword="null"/> name means that key is not declared.
    /// </summary>
    public static AsyncHistoryState Unbound(Graph child, string? outcomeCodeKeyName,
        string? outcomeNameKeyName, SubGraphPorts? ports = null)
    {
        Guard.NotNull(child, nameof(child));
        return new AsyncHistoryState(
            AsyncStateMachine.Unbound(child, outcomeCodeKeyName, outcomeNameKeyName, ports));
    }

    IEnumerable<OwnedBoardEntry> IOwnedBoardProvider.EnumerateOwnedBoards(int nodeIndex) =>
        ((IOwnedBoardProvider)Child).EnumerateOwnedBoards(nodeIndex);

    /// <summary>The declared outcome-code key's name, or <see langword="null"/> — the serialization identity of the declaration.</summary>
    public string? OutcomeCodeKeyName => Child.OutcomeCodeKeyName;

    /// <summary>The declared outcome-name key's name, or <see langword="null"/> — the serialization identity of the declaration.</summary>
    public string? OutcomeNameKeyName => Child.OutcomeNameKeyName;

    // ── ISuspendableComposite ─────────────────────────────────────────────
    // History's memory *is* the child machine's terminal position, kept live under
    // RestartPolicy.Manual. Capturing the child's deep snapshot (Failed status included —
    // machine Suspend rejects only transient statuses) preserves exactly the shape the
    // re-entry lift in ExecuteAsync consumes, so a deep-resumed fresh machine re-enters
    // at the remembered node with no new lift logic. Async composites are never mid-visit
    // at a parent step boundary, so InFlight is always false here.

    CompositeSnapshot ISuspendableComposite.SuspendComposite(int nodeIndex)
    {
        return new CompositeSnapshot(nodeIndex, InFlight: false, Done: [], Children: [Child.SuspendDeep()]);
    }

    void ISuspendableComposite.ResumeComposite(CompositeSnapshot snapshot)
    {
        DeepSnapshots.ValidateShape(snapshot, expectedChildren: 1, expectedDoneBits: 0);
        DeepSnapshots.ResumeChild(Child, snapshot, 0);
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
