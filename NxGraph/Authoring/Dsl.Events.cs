using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public sealed partial class GraphBuilder
{
    /// <summary>
    /// Begins building an event-entry graph (spec 013): one graph that responds to several
    /// externally-raised, typed events, each entering the flow at its own entry chain.
    /// Register entries with <see cref="EventsToken.On{TEvent}"/> — each binds a CLR event
    /// type to a chain through a Graph-scoped <see cref="BlackboardKey{TEvent}"/> that
    /// carries the payload — and optionally declare an <see cref="EventsToken.Otherwise"/>
    /// chain for plain (non-raised) runs. <c>Build()</c> seeds the dispatcher
    /// (<see cref="EventEntryState"/>) as the start node; raise events through the machines'
    /// typed overloads (<c>ExecuteAsync&lt;TEvent&gt;(evt)</c> /
    /// <c>Execute&lt;TEvent&gt;(evt)</c> / <c>StepAsync&lt;TEvent&gt;(evt)</c>).
    /// </summary>
    public static EventsToken StartWithEvents()
    {
        return new EventsToken(new GraphBuilder());
    }
}

/// <summary>
/// Seed handed to each entry lambda of <see cref="EventsToken.On{TEvent}"/> /
/// <see cref="EventsToken.Otherwise"/>. The first <c>To</c>/<c>ToAsync</c> call creates the
/// chain's head node (the dispatch target); it returns an ordinary <see cref="StateToken"/>
/// for continued chaining with the full DSL vocabulary (<c>.OnError</c>, <c>.Retry</c>,
/// <c>.WithOutcome</c>, ports, composites, …). Chains may converge on shared nodes — route
/// several chains to the same logic instance (or an existing <see cref="StateToken"/>) and
/// the builder's reference dedup merges them.
/// </summary>
public sealed class EventBranch
{
    private readonly GraphBuilder _builder;
    private NodeId _head = NodeId.Default;
    private bool _hasHead;

    internal EventBranch(GraphBuilder builder)
    {
        _builder = builder;
    }

    internal NodeId Head => _hasHead
        ? _head
        : throw new InvalidOperationException(
            "An event entry chain must add at least one state — call To(...)/ToAsync(...) inside the entry lambda.");

    private StateToken Record(NodeId head)
    {
        if (_hasHead)
        {
            throw new InvalidOperationException(
                "An event entry chain declares its head once — continue the chain on the returned StateToken.");
        }

        _head = head;
        _hasHead = true;
        return new StateToken(head, _builder);
    }

    /// <summary>Creates the chain head from sync logic. Routing several chains to the same instance converges them on one node.</summary>
    public StateToken To(ILogic syncLogic)
    {
        Guard.NotNull(syncLogic, nameof(syncLogic));
        return Record(_builder.AddNode(syncLogic));
    }

    /// <summary>Creates the chain head from async logic.</summary>
    public StateToken ToAsync(IAsyncLogic asyncLogic)
    {
        Guard.NotNull(asyncLogic, nameof(asyncLogic));
        return Record(_builder.AddNode(asyncLogic));
    }

    /// <summary>Creates the chain head from a sync function.</summary>
    public StateToken To(Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return To(new RelayState(run));
    }

    /// <summary>Creates the chain head from an async function.</summary>
    public StateToken ToAsync(Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return ToAsync(new AsyncRelayState(run));
    }

    /// <summary>Creates the chain head from a sync function receiving the machine-bound routed blackboard context.</summary>
    public StateToken To(Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return To(new RelayState(run));
    }

    /// <summary>Creates the chain head from an async function receiving the machine-bound routed blackboard context.</summary>
    public StateToken ToAsync(Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return ToAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Creates the chain head as a port-consuming step (spec 010 sugar): the relay reads
    /// <paramref name="input"/> — typically the entry's own event key — and hands the value
    /// (plus the routed context) to the lambda. Node-scoped keys are rejected at wiring time.
    /// </summary>
    public StateToken To<TIn>(BlackboardKey<TIn> input, Func<TIn, BlackboardContext, Result> step)
    {
        Guard.NotNull(step, nameof(step));
        Dsl.ThrowIfNotPortScope(input, nameof(input));
        return To(new PortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn}(BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public StateToken ToAsync<TIn>(BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> step)
    {
        Guard.NotNull(step, nameof(step));
        Dsl.ThrowIfNotPortScope(input, nameof(input));
        return ToAsync(new AsyncPortConsumerRelayState<TIn>(input, step));
    }

    /// <summary>Points the chain at an already-added node (e.g. a shared handler).</summary>
    public StateToken To(StateToken existing)
    {
        return Record(existing.Id);
    }
}

/// <summary>
/// Returned by <see cref="GraphBuilder.StartWithEvents"/>. Accumulates typed event entries and
/// seeds the <see cref="EventEntryState"/> dispatcher at index 0 when <see cref="Build"/> is
/// called — the dispatcher stays the single start node, so the core graph invariants are
/// untouched.
/// </summary>
public readonly struct EventsToken
{
    private sealed class EventsState
    {
        internal readonly List<EventRegistration> Registrations = [];
        internal readonly HashSet<Type> EventTypes = [];
        internal readonly HashSet<(BlackboardSchema Schema, int Ordinal)> KeyIdentities = [];
        internal NodeId OtherwiseTarget = NodeId.Default;
        internal bool HasOtherwise;
    }

    private readonly GraphBuilder _builder;
    private readonly EventsState _state;

    internal EventsToken(GraphBuilder builder)
    {
        _builder = builder;
        _state = new EventsState();
    }

    public GraphBuilder Builder => _builder;

    /// <summary>
    /// Registers an entry: raising a <typeparamref name="TEvent"/> starts a run at the chain
    /// built by <paramref name="entry"/>, with the payload delivered through
    /// <paramref name="key"/> (an ordinary Graph-scoped <see cref="BlackboardKey{TEvent}"/> —
    /// handlers read it via <c>Bb.Get(key)</c> or the spec-010 consumer sugar). Dispatch is by
    /// CLR event type: a second <c>On</c> for the same type throws at wiring time, as do
    /// Node-scoped or schema-less keys and a key already bound to another entry. Global-scoped
    /// keys are allowed but shared across machines — prefer Graph scope.
    /// </summary>
    public EventsToken On<TEvent>(BlackboardKey<TEvent> key, Func<EventBranch, StateToken> entry)
    {
        Guard.NotNull(entry, nameof(entry));
        EventRegistration.ValidateEventKey(key, nameof(key));

        // Key check first: reusing the exact key implies reusing the type, and the key
        // message is the more precise diagnosis for that case.
        if (!_state.KeyIdentities.Add((key.Schema!, key.Ordinal)))
        {
            throw new ArgumentException(
                $"Event key '{key.Name}' is already bound to an entry — one delivery key per entry.",
                nameof(key));
        }

        if (!_state.EventTypes.Add(typeof(TEvent)))
        {
            throw new ArgumentException(
                $"An entry for event type '{typeof(TEvent)}' is already registered — dispatch is by CLR event " +
                "type, one entry per type. Route variants inside one entry chain (e.g. with .If/.Switch).",
                nameof(key));
        }

        EventBranch seed = new(_builder);
        entry(seed);
        _state.Registrations.Add(new EventRegistration<TEvent>(key, seed.Head));
        return this;
    }

    /// <summary>
    /// Declares the chain a plain (non-raised) run routes to — the optional default entry.
    /// Without one, a plain <c>ExecuteAsync()</c>/<c>Execute()</c> throws pointing at the
    /// typed raise overloads. At most one <c>Otherwise</c> chain may be declared.
    /// </summary>
    public EventsToken Otherwise(Func<EventBranch, StateToken> entry)
    {
        Guard.NotNull(entry, nameof(entry));
        if (_state.HasOtherwise)
        {
            throw new InvalidOperationException(
                "An Otherwise chain has already been declared — an event graph has at most one plain-run entry.");
        }

        EventBranch seed = new(_builder);
        entry(seed);
        _state.OtherwiseTarget = seed.Head;
        _state.HasOtherwise = true;
        return this;
    }

    /// <inheritdoc cref="Dsl.WithSchema(StartToken, BlackboardSchema)"/>
    public EventsToken WithSchema(BlackboardSchema schema)
    {
        _builder.WithSchema(schema);
        return this;
    }

    /// <summary>
    /// Seeds the <see cref="EventEntryState"/> dispatcher as the start node and produces the
    /// immutable <see cref="Graph"/>. Throws when no entry has been registered.
    /// </summary>
    public Graph Build(bool throwOnError = false)
    {
        if (_state.Registrations.Count == 0)
        {
            throw new InvalidOperationException(
                "An event graph needs at least one event entry — register entries with On(key, e => ...) " +
                "before Build().");
        }

        EventEntryState dispatcher = new(_state.Registrations, _state.OtherwiseTarget);
        _builder.AddNode((IAsyncLogic)dispatcher, isStart: true);
        return _builder.Build(throwOnError);
    }
}
