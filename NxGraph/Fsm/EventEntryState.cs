using System.Runtime.CompilerServices;
using System.Text;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// One event entry of an <see cref="EventEntryState"/>: binds a CLR event type to the entry
/// chain that handles it, through the Graph-scoped blackboard key that carries the payload.
/// Authored registrations are the typed <see cref="EventRegistration{TEvent}"/>; deserialized
/// graphs rebuild <i>unbound</i> registrations (<see cref="Unbound"/>) whose delivery key is
/// resolved by name against the machine's bound board at raise time.
/// </summary>
public abstract class EventRegistration
{
    private protected EventRegistration(string keyName, string eventTypeName, string eventTypeShortName,
        NodeId target)
    {
        KeyName = keyName;
        EventTypeName = eventTypeName;
        EventTypeShortName = eventTypeShortName;
        Target = target;
    }

    /// <summary>The name of the blackboard key the event payload is delivered through.</summary>
    public string KeyName { get; }

    /// <summary>
    /// The event's runtime-stable CLR type name (version-agnostic rendering, the same rules
    /// blackboard payloads use) — the dispatch identity that rides serialization payloads.
    /// </summary>
    public string EventTypeName { get; }

    /// <summary>The head node of the entry chain a raise of this event starts at.</summary>
    public NodeId Target { get; }

    /// <summary>Short display name of the event type (diagnostics/Mermaid labels).</summary>
    internal string EventTypeShortName { get; }

    /// <summary>The live CLR event type, or <c>null</c> for unbound (deserialized) registrations.</summary>
    internal abstract Type? EventType { get; }

    /// <summary>The schema of the live delivery key, or <c>null</c> for unbound registrations.</summary>
    internal abstract BlackboardSchema? KeySchema { get; }

    /// <summary>
    /// Creates an unbound registration from serialized identity data. The delivery key is
    /// resolved per raise against the machine's bound Graph board schema by
    /// <see cref="BlackboardSchema.TryResolve{T}"/>; the event type is matched by its
    /// runtime-stable name.
    /// </summary>
    public static EventRegistration Unbound(string keyName, string eventTypeName, NodeId target)
    {
        Guard.NotNull(keyName, nameof(keyName));
        Guard.NotNull(eventTypeName, nameof(eventTypeName));
        if (keyName.Length == 0)
        {
            throw new ArgumentException("Event key name cannot be empty.", nameof(keyName));
        }

        if (eventTypeName.Length == 0)
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        if (target == NodeId.Default)
        {
            throw new ArgumentException("An event entry target cannot be NodeId.Default.", nameof(target));
        }

        return new UnboundEventRegistration(keyName, eventTypeName, target);
    }

    /// <summary>
    /// Renders a runtime-stable type name — the same rendering rules the blackboard payloads
    /// use, so event identities survive runtime upgrades (no core-lib assembly versions for
    /// constructed generics).
    /// </summary>
    internal static string StableTypeName(Type type)
    {
        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            return StableTypeName(type.GetElementType()!) + (rank == 1 ? "[]" : $"[{new string(',', rank - 1)}]");
        }

        if (!type.IsConstructedGenericType)
        {
            return type.FullName ?? type.Name;
        }

        Type definition = type.GetGenericTypeDefinition();
        Type[] arguments = type.GetGenericArguments();
        StringBuilder sb = new(definition.FullName ?? definition.Name);
        sb.Append('[');
        for (int i = 0; i < arguments.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(StableTypeName(arguments[i]));
        }

        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>Extracts the short display name from a stable type name string.</summary>
    private protected static string ShortNameOf(string typeName)
    {
        int generic = typeName.IndexOf('[');
        string core = generic < 0 ? typeName : typeName.Substring(0, generic);
        int cut = Math.Max(core.LastIndexOf('.'), core.LastIndexOf('+'));
        return cut < 0 ? core : core.Substring(cut + 1);
    }

    /// <summary>
    /// Wiring-time key validation shared by the typed registration constructor and the
    /// authoring DSL — ports precedent (<c>Dsl.Ports.cs</c>): Node-scoped keys are rejected
    /// because Node scratch resets before the entry chain runs.
    /// </summary>
    internal static void ValidateEventKey<TEvent>(in BlackboardKey<TEvent> key, string paramName)
    {
        if (key.Schema is null)
        {
            throw new ArgumentException(
                "Invalid event key — obtain keys via BlackboardSchema.Register<T>(...) on a Graph-scoped schema.",
                paramName);
        }

        if (key.Schema.Scope == BlackboardScope.Node)
        {
            throw new ArgumentException(
                $"Event key '{key.Name}' is Node-scoped — Node scratch resets before the entry chain runs, " +
                "so the payload would be gone when the handler reads it. Register event keys on a " +
                "Graph-scoped schema.", paramName);
        }
    }

    private sealed class UnboundEventRegistration : EventRegistration
    {
        internal UnboundEventRegistration(string keyName, string eventTypeName, NodeId target)
            : base(keyName, eventTypeName, ShortNameOf(eventTypeName), target)
        {
        }

        internal override Type? EventType => null;

        internal override BlackboardSchema? KeySchema => null;
    }
}

/// <summary>
/// Typed event registration: the CLR event type is the dispatch identity, and the
/// Graph-scoped <see cref="BlackboardKey{TEvent}"/> is both that identity's payload slot and
/// its serialization name. Global-scoped keys are allowed but shared across machines — two
/// machines over one graph template overwrite each other's payloads, so prefer Graph scope.
/// </summary>
public sealed class EventRegistration<TEvent> : EventRegistration
{
    /// <summary>
    /// Creates a registration delivering <typeparamref name="TEvent"/> through
    /// <paramref name="key"/> into the chain headed by <paramref name="target"/>.
    /// Node-scoped and invalid keys are rejected at wiring time.
    /// </summary>
    public EventRegistration(BlackboardKey<TEvent> key, NodeId target)
        : base(ValidatedName(key), StableTypeName(typeof(TEvent)), typeof(TEvent).Name, target)
    {
        if (target == NodeId.Default)
        {
            throw new ArgumentException("An event entry target cannot be NodeId.Default.", nameof(target));
        }

        Key = key;
    }

    private static string ValidatedName(in BlackboardKey<TEvent> key)
    {
        ValidateEventKey(key, nameof(key));
        return key.Name;
    }

    /// <summary>The typed delivery key the raise path writes the payload through.</summary>
    public BlackboardKey<TEvent> Key { get; }

    internal override Type? EventType => typeof(TEvent);

    internal override BlackboardSchema? KeySchema => Key.Schema;
}

/// <summary>
/// Event dispatcher node (spec 013): sits at index 0 of an event graph and routes each run to
/// the entry chain registered for the raised event's CLR type. An event is a <b>run
/// trigger</b>, not an interrupt — one event starts exactly one ordinary run (the machine must
/// be idle; queueing and preemption stay host concerns). Raise events through the machines'
/// typed overloads (<c>ExecuteAsync&lt;TEvent&gt;(evt)</c> / <c>Execute&lt;TEvent&gt;(evt)</c>
/// / <c>StepAsync&lt;TEvent&gt;(evt)</c>); the payload is delivered through the
/// registration's Graph-scoped blackboard key, so durability falls out of the ordinary
/// blackboard artifacts.
/// <para>
/// The pending entry is <b>machine-stamped</b>: each machine stamps its own pending entry onto
/// the dispatcher at every run start (unconditionally, <see cref="NodeId.Default"/> when no
/// raise armed one), so several machines can share one <see cref="Graph"/> with distinct
/// boards and events as long as their runs do not overlap — the same sharing contract as
/// agents and blackboards. A plain (non-raised) run routes to the <see cref="DefaultTarget"/>
/// (<c>Otherwise</c>) chain, else selection throws pointing at the raise API.
/// </para>
/// <para>
/// Implements both director interfaces on both logic slots (the <c>ForkState</c> shape), so
/// one class serves both runtimes and its branches are visible to reachability validation and
/// Mermaid export via <c>EnumerateStaticTargets()</c>.
/// </para>
/// </summary>
public sealed class EventEntryState : ILogic, IAsyncLogic, IDirector, IAsyncDirector
{
    private readonly EventRegistration[] _registrations;
    private readonly Dictionary<Type, EventRegistration> _byType;
    private readonly Dictionary<string, EventRegistration>? _unboundByTypeName;
    private readonly NodeId _defaultTarget;
    private readonly NodeId[] _staticTargets;
    private NodeId _pendingEntry = NodeId.Default;

    /// <summary>Creates a dispatcher with no <c>Otherwise</c> chain — plain runs throw.</summary>
    public EventEntryState(IReadOnlyList<EventRegistration> registrations)
        : this(registrations, NodeId.Default)
    {
    }

    /// <summary>
    /// Creates a dispatcher. <paramref name="defaultTarget"/> is the <c>Otherwise</c> chain a
    /// plain run routes to; pass <see cref="NodeId.Default"/> for none.
    /// </summary>
    public EventEntryState(IReadOnlyList<EventRegistration> registrations, NodeId defaultTarget)
    {
        Guard.NotNull(registrations, nameof(registrations));
        if (registrations.Count == 0)
        {
            throw new ArgumentException(
                "An event entry needs at least one registration — register entries with On(key, e => ...).",
                nameof(registrations));
        }

        _registrations = new EventRegistration[registrations.Count];
        _byType = new Dictionary<Type, EventRegistration>(registrations.Count);
        Dictionary<string, EventRegistration>? unbound = null;
        HashSet<string> typeNames = new(StringComparer.Ordinal);
        for (int i = 0; i < _registrations.Length; i++)
        {
            EventRegistration registration = registrations[i] ?? throw new ArgumentException(
                "Event registrations cannot be null.", nameof(registrations));
            if (!typeNames.Add(registration.EventTypeName))
            {
                throw new ArgumentException(
                    $"An entry for event type '{registration.EventTypeName}' is already registered — " +
                    "dispatch is by CLR event type, one entry per type.", nameof(registrations));
            }

            if (registration.EventType is { } eventType)
            {
                _byType.Add(eventType, registration);
            }
            else
            {
                (unbound ??= new Dictionary<string, EventRegistration>(StringComparer.Ordinal))
                    .Add(registration.EventTypeName, registration);
            }

            _registrations[i] = registration;
        }

        _unboundByTypeName = unbound;
        _defaultTarget = defaultTarget;

        bool hasDefault = defaultTarget != NodeId.Default;
        NodeId[] targets = new NodeId[_registrations.Length + (hasDefault ? 1 : 0)];
        for (int i = 0; i < _registrations.Length; i++)
        {
            targets[i] = _registrations[i].Target;
        }

        if (hasDefault)
        {
            targets[targets.Length - 1] = defaultTarget;
        }

        _staticTargets = targets;
    }

    /// <summary>The registered entries, in registration order.</summary>
    public IReadOnlyList<EventRegistration> Registrations => _registrations;

    /// <summary>
    /// The <c>Otherwise</c> chain a plain (non-raised) run routes to, or
    /// <see cref="NodeId.Default"/> when none is declared (plain runs then throw).
    /// </summary>
    public NodeId DefaultTarget => _defaultTarget;

    /// <summary>
    /// Stamps the pending entry for the next selection. Called by the machines at every run
    /// start — unconditionally, <see cref="NodeId.Default"/> when no raise armed one — so a
    /// stale entry can never leak into a later plain run.
    /// </summary>
    internal void SetPendingEntry(NodeId entry) => _pendingEntry = entry;

    /// <summary>
    /// Resolves <typeparamref name="TEvent"/> against the dispatch table, delivers the payload
    /// through the registration's key into <paramref name="boards"/> (typed end-to-end — struct
    /// events never box), and returns the entry target for the machine to arm. Unbound
    /// (deserialized) registrations resolve the delivery key by name against the bound Graph
    /// board's schema per raise.
    /// </summary>
    internal NodeId RaiseInto<TEvent>(in BlackboardContext boards, TEvent evt)
    {
        if (_byType.TryGetValue(typeof(TEvent), out EventRegistration? registration))
        {
            // The cast is guaranteed by construction: the Type table is keyed by each typed
            // registration's own event type, one entry per type.
            boards.Set(((EventRegistration<TEvent>)registration).Key, evt);
            return registration.Target;
        }

        return RaiseUnbound(boards, evt);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private NodeId RaiseUnbound<TEvent>(BlackboardContext boards, TEvent evt)
    {
        if (_unboundByTypeName is null ||
            !_unboundByTypeName.TryGetValue(EventRegistration.StableTypeName(typeof(TEvent)),
                out EventRegistration? registration))
        {
            throw UnregisteredEventType(typeof(TEvent));
        }

        Blackboard? board = boards.Graph;
        if (board is null)
        {
            throw new InvalidOperationException(
                $"graph key '{registration.KeyName}' used but no graph blackboard bound — " +
                "bind one via WithBlackboard(...).");
        }

        BlackboardSchema schema = board.Schema;
        if (!schema.TryResolve(registration.KeyName, out BlackboardKey<TEvent> key))
        {
            if (schema.TryGetKey(registration.KeyName, out BlackboardKeyDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Event entry key '{registration.KeyName}' is declared as '{descriptor.ValueType}' on the " +
                    $"bound Graph schema '{schema.Name ?? "<unnamed>"}' but the event was raised as " +
                    $"'{typeof(TEvent)}'.");
            }

            throw new InvalidOperationException(
                $"Event entry key '{registration.KeyName}' does not exist on the bound Graph schema " +
                $"'{schema.Name ?? "<unnamed>"}' — a deserialized event graph resolves delivery keys by name " +
                "against the machine's bound board.");
        }

        boards.Set(key, evt);
        return registration.Target;
    }

    private InvalidOperationException UnregisteredEventType(Type eventType)
    {
        StringBuilder sb = new();
        for (int i = 0; i < _registrations.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(_registrations[i].EventTypeName);
        }

        return new InvalidOperationException(
            $"No event entry is registered for event type '{eventType}'. Registered event types: {sb}.");
    }

    private NodeId SelectNextCore()
    {
        NodeId pending = _pendingEntry;
        if (pending != NodeId.Default)
        {
            return pending;
        }

        return _defaultTarget != NodeId.Default ? _defaultTarget : ThrowNoPendingEntry();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static NodeId ThrowNoPendingEntry() =>
        throw new InvalidOperationException(
            "EventEntryState has no pending event entry and no Otherwise chain — start runs with the typed " +
            "raise overloads (ExecuteAsync<TEvent>(evt) / Execute<TEvent>(evt) / StepAsync<TEvent>(evt)), " +
            "or declare an Otherwise(...) chain for plain runs.");

    Result ILogic.Execute() => Result.Success;

    ValueTask<Result> IAsyncLogic.ExecuteAsync(CancellationToken ct) => ResultHelpers.Success;

    NodeId IDirector.SelectNext() => SelectNextCore();

    ValueTask<NodeId> IAsyncDirector.SelectNextAsync(CancellationToken ct) => new(SelectNextCore());

    IEnumerable<NodeId> IDirector.EnumerateStaticTargets() => _staticTargets;

    IEnumerable<NodeId> IAsyncDirector.EnumerateStaticTargets() => _staticTargets;
}
