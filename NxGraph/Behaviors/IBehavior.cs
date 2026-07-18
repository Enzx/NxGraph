namespace NxGraph.Behaviors;

/// <summary>
/// One entry of a behavior composite (<see cref="BehaviorState"/>): a small, reusable,
/// data-shaped unit of node logic. Behaviors are <b>sub-node</b> logic — the node keeps the
/// whole fault model (retry, failure edge, outcome codes), and a behavior failure is a plain
/// node <c>Failure</c>. Fields should be literals or <see cref="BlackboardValue{T}"/>
/// bindings so instances stay inspectable, rebindable, and (via the serialization packages)
/// wire-able without user codecs.
/// <para>
/// Behaviors should be idempotent or tolerate re-execution: a per-node <c>.Retry(...)</c>
/// re-runs the composite's <b>whole</b> sequence in place. Output belongs on the report
/// channel (<see cref="BehaviorContext.Report"/>) or the blackboard — never the console.
/// </para>
/// </summary>
public interface IBehavior
{
    /// <summary>Executes the behavior against the owning composite's context.</summary>
    Result Execute(in BehaviorContext ctx);
}

/// <summary>
/// Async twin of <see cref="IBehavior"/> — one entry of an
/// <see cref="AsyncBehaviorState"/> sequence. Same contract: sub-node logic under the node's
/// fault model, idempotent under retry, report channel instead of console.
/// </summary>
public interface IAsyncBehavior
{
    /// <summary>Executes the behavior against the owning composite's context.</summary>
    ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct);
}

/// <summary>
/// Marker for agent-typed sync behaviors: an entry implementing this interface requires an
/// agent-typed composite (<see cref="BehaviorState{TAgent}"/> /
/// <c>ToBehaviors&lt;TAgent&gt;</c>) — the untyped composites reject it at wiring time.
/// Behaviors are never <c>IAgentSettable</c> themselves: the agent is a call parameter,
/// passed per execution by the composite, so behavior instances stay shareable data objects
/// with no mutable runtime state.
/// </summary>
public interface IAgentBehavior : IBehavior;

/// <summary>
/// Agent-typed sync behavior: receives the machine-bound agent as a call parameter per
/// execution, mirroring the <c>State&lt;TAgent&gt;</c> channel. The inherited untyped
/// <see cref="IBehavior.Execute"/> is sealed off with a throwing default implementation —
/// a typed behavior only runs inside an agent-typed composite.
/// </summary>
/// <typeparam name="TAgent">The agent type the behavior acts on.</typeparam>
public interface IBehavior<in TAgent> : IAgentBehavior
{
    /// <summary>Executes the behavior with the machine-bound agent.</summary>
    Result Execute(TAgent agent, in BehaviorContext ctx);

    Result IBehavior.Execute(in BehaviorContext ctx) =>
        throw new NotSupportedException(
            $"'{GetType().Name}' is an agent-typed behavior (IBehavior<{typeof(TAgent).Name}>) — run it inside " +
            "an agent-typed composite (ToBehaviors<TAgent>(...) / BehaviorState<TAgent>), which passes the " +
            "machine-bound agent per execution.");
}

/// <summary>
/// Marker for agent-typed async behaviors — the async twin of <see cref="IAgentBehavior"/>.
/// </summary>
public interface IAsyncAgentBehavior : IAsyncBehavior;

/// <summary>
/// Agent-typed async behavior — the async twin of <see cref="IBehavior{TAgent}"/>. The
/// inherited untyped <see cref="IAsyncBehavior.ExecuteAsync"/> is sealed off with a throwing
/// default implementation.
/// </summary>
/// <typeparam name="TAgent">The agent type the behavior acts on.</typeparam>
public interface IAsyncBehavior<in TAgent> : IAsyncAgentBehavior
{
    /// <summary>Executes the behavior with the machine-bound agent.</summary>
    ValueTask<Result> ExecuteAsync(TAgent agent, BehaviorContext ctx, CancellationToken ct);

    ValueTask<Result> IAsyncBehavior.ExecuteAsync(BehaviorContext ctx, CancellationToken ct) =>
        throw new NotSupportedException(
            $"'{GetType().Name}' is an agent-typed behavior (IAsyncBehavior<{typeof(TAgent).Name}>) — run it " +
            "inside an agent-typed composite (ToBehaviorsAsync<TAgent>(...) / AsyncBehaviorState<TAgent>), " +
            "which passes the machine-bound agent per execution.");
}

/// <summary>
/// Non-generic serialization surface implemented by all four behavior composites
/// (<see cref="BehaviorState"/>/<see cref="BehaviorState{TAgent}"/> and the async twins), so
/// the graph serializer can detect a behavior node and enumerate its entries without knowing
/// the closed agent type. Never consulted by any runtime.
/// </summary>
public interface IBehaviorComposite
{
    /// <summary><see langword="true"/> for the sync composites (wire marker "BehaviorState").</summary>
    bool IsSync { get; }

    /// <summary>The closed agent type of a typed composite, or <see langword="null"/> when untyped.</summary>
    Type? AgentType { get; }

    /// <summary>The behavior entries in sequence order (each an <see cref="IBehavior"/> or <see cref="IAsyncBehavior"/>).</summary>
    IReadOnlyList<object> Entries { get; }
}
