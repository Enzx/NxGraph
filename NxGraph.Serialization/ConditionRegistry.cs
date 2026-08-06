using System.Reflection;
using NxGraph.Behaviors;
using NxGraph.Conditions;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Default <see cref="IConditionRegistry"/>: user factories keyed by runtime-stable condition
/// type name, with the standard set (<c>IsTrue</c>, <c>Not</c>, every closed
/// <c>KeyEquals&lt;T&gt;</c>) built in — so branching graphs round-trip with <b>zero options
/// configured</b> (<see cref="GraphSerializer"/> falls back to a fresh instance of this class
/// when no <see cref="GraphSerializerOptions.ConditionRegistry"/> is given). The branch twin of
/// <see cref="BehaviorRegistry"/>, down to the mechanics: generic forms close on read via
/// cold-path reflection over the stable type name, <c>KeyEquals&lt;T&gt;</c> rebuilds through
/// its <c>Unbound</c> form (the key rides as a name and resolves against the machine's bound
/// schemas per evaluation), and <c>Not</c>'s inner condition rides as a nested entry list
/// encoded through the serializer's entry codec. User factories are consulted first, so a
/// factory registered under a standard name overrides the built-in handling.
/// </summary>
public sealed class ConditionRegistry : IConditionRegistry
{
    private static readonly string IsTrueTypeName = BlackboardSerializer.StableTypeName(typeof(IsTrue));
    private static readonly string NotTypeName = BlackboardSerializer.StableTypeName(typeof(Not));
    private static readonly string KeyEqualsPrefix = typeof(KeyEquals<>).FullName + "[";

    private readonly Dictionary<string, Func<BehaviorFieldReader, object>> _factories = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a reconstruction factory under <paramref name="conditionTypeName"/> — the
    /// condition's runtime-stable type name, the identity its payload entries carry. The
    /// factory receives the entry's fields and returns the live condition instance. Duplicate
    /// names fail here, at setup, rather than at load time.
    /// </summary>
    public void Register(string conditionTypeName, Func<BehaviorFieldReader, object> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(conditionTypeName);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(conditionTypeName, factory))
        {
            throw new ArgumentException(
                $"A condition factory is already registered under '{conditionTypeName}'.",
                nameof(conditionTypeName));
        }
    }

    /// <inheritdoc />
    public bool TryRead(string conditionTypeName, BehaviorFieldReader fields, out object? condition)
    {
        if (_factories.TryGetValue(conditionTypeName, out Func<BehaviorFieldReader, object>? factory))
        {
            condition = factory(fields) ?? throw new InvalidOperationException(
                $"The condition factory registered under '{conditionTypeName}' returned null.");
            return true;
        }

        if (string.Equals(conditionTypeName, IsTrueTypeName, StringComparison.Ordinal))
        {
            condition = new IsTrue(fields.ReadBinding<bool>("value"));
            return true;
        }

        if (string.Equals(conditionTypeName, NotTypeName, StringComparison.Ordinal))
        {
            condition = new Not(SingleInner(fields));
            return true;
        }

        if (conditionTypeName.StartsWith(KeyEqualsPrefix, StringComparison.Ordinal) &&
            conditionTypeName.EndsWith(']'))
        {
            condition = ReadKeyEquals(conditionTypeName, fields);
            return true;
        }

        condition = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryWrite(object condition, BehaviorFieldWriter fields)
    {
        if (condition is IsTrue isTrue)
        {
            fields.WriteBinding("value", isTrue.Value);
            return true;
        }

        if (condition is Not not)
        {
            // The condition model's one nesting shape: the inner condition rides as a
            // one-element entry list, encoded by the same per-entry dispatch as the top level.
            fields.WriteConditions("inner", [not.Inner]);
            return true;
        }

        Type type = condition.GetType();
        if (!type.IsConstructedGenericType || type.GetGenericTypeDefinition() != typeof(KeyEquals<>))
        {
            return false;
        }

        MethodInfo writer = typeof(ConditionRegistry)
            .GetMethod(nameof(WriteKeyEquals), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.GetGenericArguments()[0]);
        writer.Invoke(null, [condition, fields]);
        return true;
    }

    /// <summary>
    /// Reads <c>Not</c>'s body, which must hold exactly one condition — the arity is the
    /// type's contract, so a payload carrying anything else is crafted or corrupt.
    /// </summary>
    private static ICondition SingleInner(BehaviorFieldReader fields)
    {
        object[] inner = fields.ReadConditions("inner");
        if (inner.Length != 1)
        {
            throw new InvalidOperationException(
                $"Not payload carries {inner.Length} inner conditions, expected exactly 1.");
        }

        return inner[0] as ICondition ?? throw new InvalidOperationException(
            $"Not payload's inner entry ('{inner[0].GetType().Name}') does not implement ICondition.");
    }

    private static object ReadKeyEquals(string conditionTypeName, BehaviorFieldReader fields)
    {
        string valueTypeName = conditionTypeName.Substring(KeyEqualsPrefix.Length,
            conditionTypeName.Length - KeyEqualsPrefix.Length - 1);
        if (!StableTypeResolver.TryResolve(valueTypeName, out Type valueType))
        {
            throw new InvalidOperationException(
                $"Condition payload names '{conditionTypeName}', but value type '{valueTypeName}' cannot be " +
                "resolved — ensure the assembly declaring it is loaded.");
        }

        MethodInfo reader = typeof(ConditionRegistry)
            .GetMethod(nameof(ReadKeyEqualsGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(valueType);
        return reader.Invoke(null, [fields])!;
    }

    private static KeyEquals<T> ReadKeyEqualsGeneric<T>(BehaviorFieldReader fields)
    {
        string keyName = fields.ReadString("key") ?? throw new InvalidOperationException(
            "KeyEquals payload carries a null key name.");
        return KeyEquals<T>.Unbound(keyName, fields.ReadBinding<T>("expected"));
    }

    private static void WriteKeyEquals<T>(KeyEquals<T> condition, BehaviorFieldWriter fields)
    {
        fields.WriteString("key", condition.KeyName);
        fields.WriteBinding("expected", condition.Expected);
    }
}
