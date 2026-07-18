using System.Reflection;
using NxGraph.Behaviors;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Default <see cref="IBehaviorRegistry"/>: user factories keyed by runtime-stable behavior
/// type name, with the standard set (<c>Log</c> and every closed <c>SetValue&lt;T&gt;</c>)
/// built in — so standard-set graphs round-trip with <b>zero options configured</b>
/// (<see cref="GraphSerializer"/> falls back to a fresh instance of this class when no
/// <see cref="GraphSerializerOptions.BehaviorRegistry"/> is given). <c>SetValue&lt;T&gt;</c>
/// closes its generic on read via cold-path reflection over the stable type name
/// (<c>BlackboardSerializer</c> precedent). User factories are consulted first, so a factory
/// registered under a standard name overrides the built-in handling.
/// </summary>
public sealed class BehaviorRegistry : IBehaviorRegistry
{
    private static readonly string LogTypeName = BlackboardSerializer.StableTypeName(typeof(Log));
    private static readonly string SetValuePrefix = typeof(SetValue<>).FullName + "[";

    private readonly Dictionary<string, Func<BehaviorFieldReader, object>> _factories = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a reconstruction factory under <paramref name="behaviorTypeName"/> — the
    /// behavior's runtime-stable type name, the identity its payload entries carry. The
    /// factory receives the entry's fields and returns the live behavior instance. Duplicate
    /// names fail here, at setup, rather than at load time.
    /// </summary>
    public void Register(string behaviorTypeName, Func<BehaviorFieldReader, object> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(behaviorTypeName);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(behaviorTypeName, factory))
        {
            throw new ArgumentException(
                $"A behavior factory is already registered under '{behaviorTypeName}'.",
                nameof(behaviorTypeName));
        }
    }

    /// <inheritdoc />
    public bool TryRead(string behaviorTypeName, BehaviorFieldReader fields, out object? behavior)
    {
        if (_factories.TryGetValue(behaviorTypeName, out Func<BehaviorFieldReader, object>? factory))
        {
            behavior = factory(fields) ?? throw new InvalidOperationException(
                $"The behavior factory registered under '{behaviorTypeName}' returned null.");
            return true;
        }

        if (string.Equals(behaviorTypeName, LogTypeName, StringComparison.Ordinal))
        {
            behavior = new Log(fields.ReadBinding<LogSeverity>("severity"), fields.ReadBinding<string>("message"));
            return true;
        }

        if (behaviorTypeName.StartsWith(SetValuePrefix, StringComparison.Ordinal) &&
            behaviorTypeName.EndsWith("]", StringComparison.Ordinal))
        {
            behavior = ReadSetValue(behaviorTypeName, fields);
            return true;
        }

        behavior = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryWrite(object behavior, BehaviorFieldWriter fields)
    {
        if (behavior is Log log)
        {
            fields.WriteBinding("severity", log.Severity);
            fields.WriteBinding("message", log.Message);
            return true;
        }

        Type type = behavior.GetType();
        if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(SetValue<>))
        {
            MethodInfo writer = typeof(BehaviorRegistry)
                .GetMethod(nameof(WriteSetValue), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type.GetGenericArguments()[0]);
            writer.Invoke(null, [behavior, fields]);
            return true;
        }

        return false;
    }

    private static object ReadSetValue(string behaviorTypeName, BehaviorFieldReader fields)
    {
        string valueTypeName =
            behaviorTypeName.Substring(SetValuePrefix.Length, behaviorTypeName.Length - SetValuePrefix.Length - 1);
        if (!StableTypeResolver.TryResolve(valueTypeName, out Type valueType))
        {
            throw new InvalidOperationException(
                $"Behavior payload names '{behaviorTypeName}', but value type '{valueTypeName}' cannot be " +
                "resolved — ensure the assembly declaring it is loaded.");
        }

        MethodInfo reader = typeof(BehaviorRegistry)
            .GetMethod(nameof(ReadSetValueGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(valueType);
        return reader.Invoke(null, [fields])!;
    }

    private static SetValue<T> ReadSetValueGeneric<T>(BehaviorFieldReader fields)
    {
        string keyName = fields.ReadString("key") ?? throw new InvalidOperationException(
            "SetValue payload carries a null key name.");
        return SetValue<T>.Unbound(keyName, fields.ReadBinding<T>("value"));
    }

    private static void WriteSetValue<T>(SetValue<T> behavior, BehaviorFieldWriter fields)
    {
        fields.WriteString("key", behavior.KeyName);
        fields.WriteBinding("value", behavior.Value);
    }
}
