using NxGraph.Blackboards;

namespace NxGraph.Behaviors;

/// <summary>
/// Standard behavior: writes a resolved <see cref="BlackboardValue{T}"/> to a blackboard key
/// — the typed copy/constant/key-to-key primitive (<c>SetValue(target, 42)</c>,
/// <c>SetValue(target, sourceKey)</c>). Always succeeds; idempotent under retry by
/// construction (re-running re-writes the same resolved value). One class implements both
/// behavior interfaces, so a single instance authors either runtime.
/// <para>
/// Authored instances hold a live key; deserialized instances (<see cref="Unbound"/>) hold
/// only the key <b>name</b> and resolve it per execution against the machine's bound boards'
/// schemas — the same name-based rebind as key-form bindings.
/// </para>
/// </summary>
/// <typeparam name="T">The slot's value type.</typeparam>
public sealed class SetValue<T> : IBehavior, IAsyncBehavior
{
    private readonly BlackboardKey<T> _key;
    private readonly string _keyName;

    /// <summary>Creates a write of <paramref name="value"/> (literal or key-bound) to <paramref name="key"/>.</summary>
    public SetValue(BlackboardKey<T> key, BlackboardValue<T> value)
    {
        if (!key.IsValid)
        {
            throw new ArgumentException(
                "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).", nameof(key));
        }

        _key = key;
        _keyName = key.Name;
        Value = value;
    }

    private SetValue(string keyName, BlackboardValue<T> value)
    {
        _key = default;
        _keyName = keyName;
        Value = value;
    }

    /// <summary>
    /// Creates a name-bound write — the deserialization rebind form. The target key resolves
    /// per execution against the machine's bound boards' schemas (Graph, then Global, then
    /// Node), with targeted miss/type-mismatch errors.
    /// </summary>
    public static SetValue<T> Unbound(string keyName, BlackboardValue<T> value)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            throw new ArgumentException("Key name cannot be null or empty.", nameof(keyName));
        }

        return new SetValue<T>(keyName, value);
    }

    /// <summary>The live target key; default (invalid) for name-bound instances.</summary>
    public BlackboardKey<T> Key => _key;

    /// <summary>The target key's registered name — the serialization identity.</summary>
    public string KeyName => _keyName;

    /// <summary>The value binding — literal or key.</summary>
    public BlackboardValue<T> Value { get; }

    /// <inheritdoc />
    public Result Execute(in BehaviorContext ctx)
    {
        Apply(in ctx);
        return Result.Success;
    }

    /// <inheritdoc />
    public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
    {
        Apply(in ctx);
        return ResultHelpers.Success;
    }

    private void Apply(in BehaviorContext ctx)
    {
        T value = ctx.Resolve(Value);
        BlackboardContext bb = ctx.Bb;
        if (_key.IsValid)
        {
            bb.Set(_key, value);
            return;
        }

        bb.Set(BehaviorKeyResolver.Resolve<T>(in bb, _keyName), value);
    }
}
