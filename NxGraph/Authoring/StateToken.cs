using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

/// <summary>
/// Returned by the DSL whenever you add a state.  It lets you fluently wire
/// transitions and continue building without exposing NodeId.
/// </summary>
public readonly struct StateToken
{
    internal StateToken(NodeId id, GraphBuilder builder)
    {
        Id = id;
        Builder = builder;
    }

    public GraphBuilder Builder { get; }

    public NodeId Id { get; }

    /// <summary> Adds a transition from the current state to <paramref name="target"/>. </summary>
    public StateToken To(StateToken target)
    {
        Builder.AddTransition(Id, target.Id);
        return target;
    }

    /// <summary> Adds a brand-new async state and immediately wires a transition to it. </summary>
    public StateToken ToAsync(IAsyncLogic nextStateAsyncLogic)
    {
        NodeId next = Builder.AddNode(nextStateAsyncLogic);
        Builder.AddTransition(Id, next);
        return new StateToken(next, Builder);
    }

    /// <summary> Adds a brand-new sync state and immediately wires a transition to it. </summary>
    public StateToken To(ILogic nextStateSyncLogic)
    {
        NodeId next = Builder.AddNode(nextStateSyncLogic);
        Builder.AddTransition(Id, next);
        return new StateToken(next, Builder);
    }

    /// <summary>
    /// Declares the terminal outcome reported when a run ends at this state
    /// (e.g. "Approved" vs "Rejected"), with an optional display name for the code.
    /// </summary>
    public StateToken WithOutcome(int code, string? name = null)
    {
        Builder.SetOutcome(Id, code, name);
        return this;
    }

    /// <summary>
    /// Attaches an action fired when the machine enters this state — once per visit,
    /// before its first execution (retries do not re-fire it).
    /// </summary>
    public StateToken OnEnter(Action action)
    {
        Builder.SetEnterAction(Id, action);
        return this;
    }

    /// <summary>
    /// Attaches an action fired when the machine leaves this state — once per visit,
    /// after its final execution, regardless of outcome.
    /// </summary>
    public StateToken OnExit(Action action)
    {
        Builder.SetExitAction(Id, action);
        return this;
    }

    /// <summary>
    /// Declares a retry policy for this state: on <c>Failure</c> it is re-run in place until
    /// it succeeds or <paramref name="maxAttempts"/> executions are consumed, then normal
    /// failure handling applies. Backoff is honored by the async runtime only.
    /// </summary>
    public StateToken Retry(byte maxAttempts, TimeSpan backoff = default,
        BackoffKind backoffKind = BackoffKind.Fixed)
    {
        Builder.SetRetryPolicy(Id, new RetryPolicy(maxAttempts, backoff, backoffKind));
        return this;
    }

    /// <summary>
    /// Routes this state's <c>Failure</c> outcome to <paramref name="handler"/> instead of
    /// terminating the machine. The success chain continues from this state.
    /// </summary>
    public StateToken OnError(StateToken handler)
    {
        Builder.AddFailureTransition(Id, handler.Id);
        return this;
    }

    /// <summary>
    /// Adds a brand-new sync state as the failure handler for this state.
    /// The handler is terminal unless wired further; the success chain continues from this state.
    /// </summary>
    public StateToken OnError(ILogic handlerLogic)
    {
        NodeId handler = Builder.AddNode(handlerLogic);
        Builder.AddFailureTransition(Id, handler);
        return this;
    }

    /// <summary>
    /// Adds a brand-new async state as the failure handler for this state.
    /// The handler is terminal unless wired further; the success chain continues from this state.
    /// </summary>
    public StateToken OnErrorAsync(IAsyncLogic handlerLogic)
    {
        NodeId handler = Builder.AddNode(handlerLogic);
        Builder.AddFailureTransition(Id, handler);
        return this;
    }

    /// <summary>
    /// Wires this state's outgoing transition back to the node named <paramref name="targetName"/>
    /// (assigned via <c>SetName</c>, before or after this call). The name resolves at
    /// <see cref="GraphBuilder.Build"/>; unknown or ambiguous names fail the build.
    /// The chain ends here — a state has a single outgoing transition.
    /// </summary>
    public GotoToken Goto(string targetName)
    {
        Builder.AddGoto(Id, targetName);
        return new GotoToken(Builder);
    }

    /// <summary> Finishes the DSL and produces an immutable <see cref="Graph"/>. </summary>
    public Graph Build()
    {
        return Builder.Build();
    }
}

/// <summary>
/// Returned by <see cref="StateToken.Goto"/>. The chain is closed — the only remaining
/// operation is producing the graph.
/// </summary>
public readonly struct GotoToken
{
    internal GotoToken(GraphBuilder builder)
    {
        Builder = builder;
    }

    public GraphBuilder Builder { get; }

    /// <summary> Finishes the DSL and produces an immutable <see cref="Graph"/>. </summary>
    public Graph Build()
    {
        return Builder.Build();
    }
}