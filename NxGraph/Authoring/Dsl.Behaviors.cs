using NxGraph.Behaviors;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    // ── Behaviors: declarative, reusable, blackboard-bound state composition ──
    //
    // .ToBehaviors(...) authors a node as a sequence of small data-shaped behaviors instead
    // of an opaque lambda or a hand-written State subclass. Explicit names (not .To
    // overloads): params interface arrays next to the single-logic To(ILogic) overloads
    // would invite overload-resolution surprises.

    /// <summary>
    /// Starts the graph with a node that runs <paramref name="behaviors"/> <b>in order,
    /// fail-fast</b>: the first non-<c>Success</c> entry stops the sequence and the node
    /// returns <c>Failure</c> (see <see cref="BehaviorState"/> — the deliberate opposite of
    /// <c>ToAll</c>'s run-all-then-combine, because sequence entries may depend on earlier
    /// entries' writes). The node keeps the whole fault model: <c>.Retry(...)</c> re-runs the
    /// whole list, <c>.OnError(...)</c> reroutes, <c>.WithOutcome(...)</c> codes. An empty or
    /// null array throws <see cref="ArgumentException"/> at wiring time, as do null entries
    /// and agent-typed (<see cref="IAgentBehavior"/>) entries — those need
    /// <see cref="ToBehaviors{TAgent}(StartToken, IBehavior[])"/>. The node is plain sync
    /// logic and also runs under the async machine via the sync-logic adapter.
    /// </summary>
    public static StateToken ToBehaviors(this StartToken token, params IBehavior[] behaviors)
    {
        return token.To(new BehaviorState(behaviors));
    }

    /// <inheritdoc cref="ToBehaviors(StartToken, IBehavior[])"/>
    public static StateToken ToBehaviors(this StateToken prev, params IBehavior[] behaviors)
    {
        return prev.To(new BehaviorState(behaviors));
    }

    /// <inheritdoc cref="ToBehaviors(StartToken, IBehavior[])"/>
    public static BranchBuilder ToBehaviors(this BranchBuilder branch, params IBehavior[] behaviors)
    {
        return branch.To(new BehaviorState(behaviors));
    }

    /// <inheritdoc cref="ToBehaviors(StartToken, IBehavior[])"/>
    public static StateToken ToBehaviors(this BranchEnd branchEnd, params IBehavior[] behaviors)
    {
        return branchEnd.To(new BehaviorState(behaviors));
    }

    /// <summary>
    /// Starts the graph with the async twin of
    /// <see cref="ToBehaviors(StartToken, IBehavior[])"/>: the same in-order, fail-fast
    /// sequence over <see cref="IAsyncBehavior"/> entries (see
    /// <see cref="AsyncBehaviorState"/>). Dual-interface behaviors (the standard set) pass to
    /// either overload. Agent-typed (<see cref="IAsyncAgentBehavior"/>) entries need
    /// <see cref="ToBehaviorsAsync{TAgent}(StartToken, IAsyncBehavior[])"/>.
    /// </summary>
    public static StateToken ToBehaviorsAsync(this StartToken token, params IAsyncBehavior[] behaviors)
    {
        return token.ToAsync(new AsyncBehaviorState(behaviors));
    }

    /// <inheritdoc cref="ToBehaviorsAsync(StartToken, IAsyncBehavior[])"/>
    public static StateToken ToBehaviorsAsync(this StateToken prev, params IAsyncBehavior[] behaviors)
    {
        return prev.ToAsync(new AsyncBehaviorState(behaviors));
    }

    /// <inheritdoc cref="ToBehaviorsAsync(StartToken, IAsyncBehavior[])"/>
    public static BranchBuilder ToBehaviorsAsync(this BranchBuilder branch, params IAsyncBehavior[] behaviors)
    {
        return branch.ToAsync(new AsyncBehaviorState(behaviors));
    }

    /// <inheritdoc cref="ToBehaviorsAsync(StartToken, IAsyncBehavior[])"/>
    public static StateToken ToBehaviorsAsync(this BranchEnd branchEnd, params IAsyncBehavior[] behaviors)
    {
        return branchEnd.ToAsync(new AsyncBehaviorState(behaviors));
    }

    /// <summary>
    /// Starts the graph with an <b>agent-typed</b> behavior node (see
    /// <see cref="BehaviorState{TAgent}"/>): plain and <see cref="IBehavior{TAgent}"/> entries
    /// may mix — typed entries receive the machine-bound agent as a call parameter per
    /// execution (the agent is never stamped onto behavior instances), plain entries run
    /// agent-blind. The composite participates in the standard agent stamping walk, so
    /// <c>Graph.SetAgent</c> counts it as an acceptor and machines sharing one graph each
    /// deliver their own agent. An <see cref="IAgentBehavior"/> entry of a different agent
    /// type throws at wiring time naming both types. Sequence semantics match the untyped
    /// overloads (in order, fail-fast).
    /// </summary>
    public static StateToken ToBehaviors<TAgent>(this StartToken token, params IBehavior[] behaviors)
    {
        return token.To(new BehaviorState<TAgent>(behaviors));
    }

    /// <inheritdoc cref="ToBehaviors{TAgent}(StartToken, IBehavior[])"/>
    public static StateToken ToBehaviors<TAgent>(this StateToken prev, params IBehavior[] behaviors)
    {
        return prev.To(new BehaviorState<TAgent>(behaviors));
    }

    /// <inheritdoc cref="ToBehaviors{TAgent}(StartToken, IBehavior[])"/>
    public static BranchBuilder ToBehaviors<TAgent>(this BranchBuilder branch, params IBehavior[] behaviors)
    {
        return branch.To(new BehaviorState<TAgent>(behaviors));
    }

    /// <inheritdoc cref="ToBehaviors{TAgent}(StartToken, IBehavior[])"/>
    public static StateToken ToBehaviors<TAgent>(this BranchEnd branchEnd, params IBehavior[] behaviors)
    {
        return branchEnd.To(new BehaviorState<TAgent>(behaviors));
    }

    /// <summary>
    /// Starts the graph with the async twin of
    /// <see cref="ToBehaviors{TAgent}(StartToken, IBehavior[])"/> (see
    /// <see cref="AsyncBehaviorState{TAgent}"/>).
    /// </summary>
    public static StateToken ToBehaviorsAsync<TAgent>(this StartToken token, params IAsyncBehavior[] behaviors)
    {
        return token.ToAsync(new AsyncBehaviorState<TAgent>(behaviors));
    }

    /// <inheritdoc cref="ToBehaviorsAsync{TAgent}(StartToken, IAsyncBehavior[])"/>
    public static StateToken ToBehaviorsAsync<TAgent>(this StateToken prev, params IAsyncBehavior[] behaviors)
    {
        return prev.ToAsync(new AsyncBehaviorState<TAgent>(behaviors));
    }

    /// <inheritdoc cref="ToBehaviorsAsync{TAgent}(StartToken, IAsyncBehavior[])"/>
    public static BranchBuilder ToBehaviorsAsync<TAgent>(this BranchBuilder branch,
        params IAsyncBehavior[] behaviors)
    {
        return branch.ToAsync(new AsyncBehaviorState<TAgent>(behaviors));
    }

    /// <inheritdoc cref="ToBehaviorsAsync{TAgent}(StartToken, IAsyncBehavior[])"/>
    public static StateToken ToBehaviorsAsync<TAgent>(this BranchEnd branchEnd,
        params IAsyncBehavior[] behaviors)
    {
        return branchEnd.ToAsync(new AsyncBehaviorState<TAgent>(behaviors));
    }
}
