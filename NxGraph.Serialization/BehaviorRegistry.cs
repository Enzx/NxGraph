using System.Reflection;
using NxGraph.Behaviors;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Default <see cref="IBehaviorRegistry"/>: user factories keyed by runtime-stable behavior
/// type name, with the standard set (<c>Log</c>, every closed <c>SetValue&lt;T&gt;</c>, and
/// all four repeat forms — <c>Repeat</c>/<c>AsyncRepeat</c> plus the <c>&lt;TAgent&gt;</c>
/// twins) built in — so standard-set graphs round-trip with <b>zero options configured</b>
/// (<see cref="GraphSerializer"/> falls back to a fresh instance of this class when no
/// <see cref="GraphSerializerOptions.BehaviorRegistry"/> is given). Generic forms close on
/// read via cold-path reflection over the stable type name (<c>BlackboardSerializer</c>
/// precedent). Repeat bodies ride as nested entry lists (payload version 9) encoded through
/// the serializer's entry codec, and reconstruct through the <c>Unbound</c> forms with a
/// targeted error naming the offending entry when a body mixes runtime families. User
/// factories are consulted first, so a factory registered under a standard name overrides
/// the built-in handling.
/// </summary>
public sealed class BehaviorRegistry : IBehaviorRegistry
{
    private static readonly string LogTypeName = BlackboardSerializer.StableTypeName(typeof(Log));
    private static readonly string SetValuePrefix = typeof(SetValue<>).FullName + "[";
    private static readonly string RepeatTypeName = BlackboardSerializer.StableTypeName(typeof(Repeat));
    private static readonly string AsyncRepeatTypeName = BlackboardSerializer.StableTypeName(typeof(AsyncRepeat));
    private static readonly string RepeatPrefix = typeof(Repeat<>).FullName + "[";
    private static readonly string AsyncRepeatPrefix = typeof(AsyncRepeat<>).FullName + "[";

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
            behaviorTypeName.EndsWith(']'))
        {
            behavior = ReadSetValue(behaviorTypeName, fields);
            return true;
        }

        if (string.Equals(behaviorTypeName, RepeatTypeName, StringComparison.Ordinal))
        {
            behavior = Repeat.Unbound(fields.ReadBinding<int>("count"), fields.ReadString("indexKey"),
                RepeatBody<IBehavior>(fields, "Repeat"));
            return true;
        }

        if (string.Equals(behaviorTypeName, AsyncRepeatTypeName, StringComparison.Ordinal))
        {
            behavior = AsyncRepeat.Unbound(fields.ReadBinding<int>("count"), fields.ReadString("indexKey"),
                RepeatBody<IAsyncBehavior>(fields, "AsyncRepeat"));
            return true;
        }

        if (behaviorTypeName.StartsWith(RepeatPrefix, StringComparison.Ordinal) &&
            behaviorTypeName.EndsWith(']'))
        {
            behavior = ReadTypedRepeat(behaviorTypeName, RepeatPrefix, nameof(ReadRepeatGeneric), fields);
            return true;
        }

        if (behaviorTypeName.StartsWith(AsyncRepeatPrefix, StringComparison.Ordinal) &&
            behaviorTypeName.EndsWith(']'))
        {
            behavior = ReadTypedRepeat(behaviorTypeName, AsyncRepeatPrefix, nameof(ReadAsyncRepeatGeneric), fields);
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

        if (behavior is Repeat repeat)
        {
            WriteRepeatFields(fields, repeat.Count, repeat.IndexKeyName, repeat.Body);
            return true;
        }

        if (behavior is AsyncRepeat asyncRepeat)
        {
            WriteRepeatFields(fields, asyncRepeat.Count, asyncRepeat.IndexKeyName, asyncRepeat.Body);
            return true;
        }

        Type type = behavior.GetType();
        if (!type.IsConstructedGenericType)
        {
            return false;
        }

        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(SetValue<>))
        {
            MethodInfo writer = typeof(BehaviorRegistry)
                .GetMethod(nameof(WriteSetValue), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type.GetGenericArguments()[0]);
            writer.Invoke(null, [behavior, fields]);
            return true;
        }

        if (definition == typeof(Repeat<>) || definition == typeof(AsyncRepeat<>))
        {
            // Cold path: reflection only reaches the typed twin's identical field shape —
            // the agent type parameter never affects the payload beyond the type name.
            string writerName = definition == typeof(Repeat<>)
                ? nameof(WriteRepeatGeneric)
                : nameof(WriteAsyncRepeatGeneric);
            MethodInfo writer = typeof(BehaviorRegistry)
                .GetMethod(writerName, BindingFlags.NonPublic | BindingFlags.Static)!
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

    // ── Repeat (payload version 9) ──────────────────────────────────────
    // All four forms share one field shape: `count` (binding), `indexKey` (string, null when
    // absent), `body` (nested behavior entries via the serializer's entry codec).

    private static void WriteRepeatFields(BehaviorFieldWriter fields, BlackboardValue<int> count,
        string? indexKeyName, IReadOnlyList<object> body)
    {
        fields.WriteBinding("count", count);
        fields.WriteString("indexKey", indexKeyName);
        fields.WriteBehaviors("body", body);
    }

    private static void WriteRepeatGeneric<TAgent>(Repeat<TAgent> behavior, BehaviorFieldWriter fields) =>
        WriteRepeatFields(fields, behavior.Count, behavior.IndexKeyName, behavior.Body);

    private static void WriteAsyncRepeatGeneric<TAgent>(AsyncRepeat<TAgent> behavior, BehaviorFieldWriter fields) =>
        WriteRepeatFields(fields, behavior.Count, behavior.IndexKeyName, behavior.Body);

    private static object ReadTypedRepeat(string behaviorTypeName, string prefix, string readerName,
        BehaviorFieldReader fields)
    {
        string agentTypeName =
            behaviorTypeName.Substring(prefix.Length, behaviorTypeName.Length - prefix.Length - 1);
        if (!StableTypeResolver.TryResolve(agentTypeName, out Type agentType))
        {
            throw new InvalidOperationException(
                $"Behavior payload names '{behaviorTypeName}', but agent type '{agentTypeName}' cannot be " +
                "resolved — ensure the assembly declaring it is loaded.");
        }

        MethodInfo reader = typeof(BehaviorRegistry)
            .GetMethod(readerName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(agentType);
        return reader.Invoke(null, [fields])!;
    }

    private static Repeat<TAgent> ReadRepeatGeneric<TAgent>(BehaviorFieldReader fields) =>
        Repeat<TAgent>.Unbound(fields.ReadBinding<int>("count"), fields.ReadString("indexKey"),
            RepeatBody<IBehavior>(fields, $"Repeat<{typeof(TAgent).Name}>"));

    private static AsyncRepeat<TAgent> ReadAsyncRepeatGeneric<TAgent>(BehaviorFieldReader fields) =>
        AsyncRepeat<TAgent>.Unbound(fields.ReadBinding<int>("count"), fields.ReadString("indexKey"),
            RepeatBody<IAsyncBehavior>(fields, $"AsyncRepeat<{typeof(TAgent).Name}>"));

    /// <summary>
    /// Reads a repeat body and casts each reconstructed entry to the family's entry
    /// interface, with a targeted error naming the offender on mismatch — a crafted payload
    /// putting a sync-only entry in an async repeat body must not surface as a cast
    /// exception.
    /// </summary>
    private static TEntry[] RepeatBody<TEntry>(BehaviorFieldReader fields, string family) where TEntry : class
    {
        object[] body = fields.ReadBehaviors("body");
        TEntry[] typed = new TEntry[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            typed[i] = body[i] as TEntry ?? throw new InvalidOperationException(
                $"{family} body entry {i} ('{body[i].GetType().Name}') does not implement " +
                $"{typeof(TEntry).Name}, required by the {family} family.");
        }

        return typed;
    }
}
