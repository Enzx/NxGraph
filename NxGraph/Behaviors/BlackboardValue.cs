using System.Runtime.CompilerServices;
using NxGraph.Blackboards;

namespace NxGraph.Behaviors;

/// <summary>
/// The one binding primitive of the behavior model: a field that is either a <b>literal</b>
/// value or a <b>key binding</b> resolved against the machine-bound blackboards per
/// execution. Both forms convert implicitly, so behavior fields read naturally:
/// <c>new Log(LogSeverity.Info, greetingKey)</c>. A <c>default</c> value is a literal
/// <c>default(T)</c>.
/// <para>
/// Any key scope binds — Node scratch included: behavior bindings resolve <b>within one
/// visit</b>, so the ports rule (spec 010) that rejects Node-scoped keys deliberately does
/// not apply here. Resolution is a branch plus a typed <c>Get</c> — zero boxing, zero
/// allocation. Deserialized bindings hold only the key <b>name</b>
/// (<see cref="Bound(string)"/>) and resolve it against the bound boards' schemas
/// (Graph, then Global, then Node) at execution, with targeted miss/type-mismatch errors.
/// </para>
/// </summary>
public readonly struct BlackboardValue<T>
{
    private readonly T _literal;
    private readonly BlackboardKey<T> _key;
    private readonly string? _keyName;

    private BlackboardValue(T literal)
    {
        _literal = literal;
        _key = default;
        _keyName = null;
    }

    private BlackboardValue(in BlackboardKey<T> key)
    {
        _literal = default!;
        _key = key;
        _keyName = key.Name;
    }

    private BlackboardValue(string keyName)
    {
        _literal = default!;
        _key = default;
        _keyName = keyName;
    }

    /// <summary>Wraps a literal value.</summary>
    public static implicit operator BlackboardValue<T>(T literal) => new(literal);

    /// <summary>
    /// Binds to a blackboard key; resolution reads the key per execution. Invalid
    /// (default-constructed) keys are rejected at wiring time.
    /// </summary>
    public static implicit operator BlackboardValue<T>(BlackboardKey<T> key)
    {
        if (!key.IsValid)
        {
            ThrowInvalidKey();
        }

        return new BlackboardValue<T>(in key);
    }

    /// <summary>
    /// Creates a name-bound binding — the deserialization rebind form. The name resolves per
    /// execution against the machine's bound boards' schemas (Graph, then Global, then Node)
    /// via <see cref="BlackboardSchema.TryResolve{T}"/>, throwing targeted errors when the
    /// name is missing or declared with a different value type.
    /// </summary>
    public static BlackboardValue<T> Bound(string keyName)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            throw new ArgumentException("Binding key name cannot be null or empty.", nameof(keyName));
        }

        return new BlackboardValue<T>(keyName);
    }

    /// <summary><see langword="true"/> when key-backed (live key or name-bound); <see langword="false"/> for a literal.</summary>
    public bool IsBound => _keyName is not null;

    /// <summary>The bound key's name, or <see langword="null"/> for a literal — the serialization identity of the key form.</summary>
    public string? KeyName => _keyName;

    /// <summary>The literal value; meaningful only when <see cref="IsBound"/> is <see langword="false"/>.</summary>
    public T Literal => _literal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T Resolve(in BlackboardContext bb)
    {
        if (_keyName is null)
        {
            return _literal;
        }

        return _key.IsValid ? bb.Get(_key) : bb.Get(BehaviorKeyResolver.Resolve<T>(in bb, _keyName));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidKey() =>
        throw new ArgumentException(
            "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).");

    /// <summary>Diagnostics only — the key name or the literal.</summary>
    public override string ToString() =>
        _keyName is not null ? $"@{_keyName}" : _literal?.ToString() ?? "<null>";
}

/// <summary>
/// Resolves name-bound behavior keys (deserialized bindings and <see cref="SetValue{T}"/>
/// targets) against the machine's bound boards, probing schemas in Graph → Global → Node
/// order. Cold-ish path (one dictionary lookup per execution, zero allocation); failures are
/// targeted <c>NoInlining</c> throws in the event-entry rebind style.
/// </summary>
internal static class BehaviorKeyResolver
{
    internal static BlackboardKey<T> Resolve<T>(in BlackboardContext bb, string name)
    {
        if (bb.Graph is { } graph && graph.Schema.TryResolve(name, out BlackboardKey<T> key))
        {
            return key;
        }

        if (bb.Global is { } global && global.Schema.TryResolve(name, out key))
        {
            return key;
        }

        if (bb.Node is { } node && node.Schema.TryResolve(name, out key))
        {
            return key;
        }

        return ThrowUnresolved<T>(bb, name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BlackboardKey<T> ThrowUnresolved<T>(in BlackboardContext bb, string name)
    {
        // A same-name declaration with a different value type gets the precise mismatch
        // message; otherwise the name is simply absent from every bound schema.
        ReportMismatch<T>(bb.Graph, name, "Graph");
        ReportMismatch<T>(bb.Global, name, "Global");
        ReportMismatch<T>(bb.Node, name, "Node");

        throw new InvalidOperationException(
            $"Behavior binding key '{name}' does not exist on any bound blackboard schema (checked Graph, " +
            "Global, and Node scopes) — a deserialized behavior resolves its keys by name against the " +
            "machine's bound boards.");
    }

    private static void ReportMismatch<T>(Blackboard? board, string name, string scopeWord)
    {
        if (board is not null && board.Schema.TryGetKey(name, out BlackboardKeyDescriptor descriptor))
        {
            throw new InvalidOperationException(
                $"Behavior binding key '{name}' is declared as '{descriptor.ValueType}' on the bound " +
                $"{scopeWord} schema '{board.Schema.Name ?? "<unnamed>"}' but the behavior expects " +
                $"'{typeof(T)}'.");
        }
    }
}
