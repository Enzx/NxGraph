using NxGraph.Behaviors;
using NxGraph.Blackboards;

namespace NxGraph.Conditions;

/// <summary>
/// Standard condition: <see langword="true"/> when the value in <see cref="Key"/> equals
/// <see cref="Expected"/>, compared with <see cref="EqualityComparer{T}.Default"/>. The
/// expected side is a <see cref="BlackboardValue{T}"/> binding, so a rule may compare a key
/// against a literal <i>or</i> against another key.
/// <para>
/// Authored instances hold a live key; deserialized instances (<see cref="Unbound"/>) hold
/// only the key <b>name</b> and resolve it per evaluation against the machine's bound boards'
/// schemas (Graph, then Global, then Node) — the same name-based rebind as behavior bindings,
/// with the same targeted miss/type-mismatch throws.
/// </para>
/// </summary>
/// <typeparam name="T">The slot's value type.</typeparam>
public sealed class KeyEquals<T> : ICondition
{
    private readonly BlackboardKey<T> _key;
    private readonly string _keyName;

    /// <summary>Creates a comparison of <paramref name="key"/> against <paramref name="expected"/>.</summary>
    public KeyEquals(BlackboardKey<T> key, BlackboardValue<T> expected)
    {
        if (!key.IsValid)
        {
            throw new ArgumentException(
                "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).", nameof(key));
        }

        _key = key;
        _keyName = key.Name;
        Expected = expected;
    }

    private KeyEquals(string keyName, BlackboardValue<T> expected)
    {
        _key = default;
        _keyName = keyName;
        Expected = expected;
    }

    /// <summary>
    /// Creates a name-bound comparison — the deserialization rebind form. The tested key
    /// resolves per evaluation against the machine's bound boards' schemas.
    /// </summary>
    public static KeyEquals<T> Unbound(string keyName, BlackboardValue<T> expected)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            throw new ArgumentException("Key name cannot be null or empty.", nameof(keyName));
        }

        return new KeyEquals<T>(keyName, expected);
    }

    /// <summary>The live tested key; default (invalid) for name-bound instances.</summary>
    public BlackboardKey<T> Key => _key;

    /// <summary>The tested key's registered name — the serialization identity.</summary>
    public string KeyName => _keyName;

    /// <summary>The expected value — literal or key-bound.</summary>
    public BlackboardValue<T> Expected { get; }

    /// <inheritdoc />
    public bool Evaluate(in BehaviorContext ctx)
    {
        BlackboardContext bb = ctx.Bb;
        T actual = _key.IsValid
            ? bb.Get(_key)
            : bb.Get(BehaviorKeyResolver.Resolve<T>(in bb, _keyName));
        return EqualityComparer<T>.Default.Equals(actual, ctx.Resolve(Expected));
    }
}
