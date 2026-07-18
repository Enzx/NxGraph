using NxGraph.Behaviors;

namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Collects a behavior's fields into the neutral field model (payload version 8). Field
/// names must be unique per behavior; the matching <see cref="BehaviorFieldReader"/> reads
/// them back by name. Cold path by nature — serialization only.
/// </summary>
public sealed class BehaviorFieldWriter
{
    private readonly List<BehaviorField> _fields = [];

    /// <summary>Writes a string field (null allowed).</summary>
    public void WriteString(string name, string? value) =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.String, text: value));

    /// <summary>Writes a boolean field.</summary>
    public void WriteBool(string name, bool value) =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Bool, flag: value));

    /// <summary>Writes a 32-bit integer field.</summary>
    public void WriteInt32(string name, int value) =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Int32, integer: value));

    /// <summary>Writes a 64-bit integer field.</summary>
    public void WriteInt64(string name, long value) =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Int64, integer: value));

    /// <summary>Writes a 32-bit float field.</summary>
    public void WriteSingle(string name, float value) =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Single, number: value));

    /// <summary>Writes a 64-bit float field.</summary>
    public void WriteDouble(string name, double value) =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Double, number: value));

    /// <summary>Writes an enum field as its member name.</summary>
    public void WriteEnum<TEnum>(string name, TEnum value) where TEnum : struct, Enum =>
        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Enum, text: value.ToString()));

    /// <summary>
    /// Writes a blackboard binding. The key form serializes only the key's registered
    /// <b>name</b> (rebound by name at execution on the read side); the literal form requires
    /// a primitive from the field model — string, bool, int, long, float, double, or an enum
    /// — and throws a targeted <see cref="NotSupportedException"/> for anything richer.
    /// </summary>
    public void WriteBinding<T>(string name, in BlackboardValue<T> value)
    {
        if (value.IsBound)
        {
            Add(name, new BehaviorFieldValue(BehaviorFieldKind.Binding,
                binding: new BehaviorBinding(value.KeyName, literal: null)));
            return;
        }

        Add(name, new BehaviorFieldValue(BehaviorFieldKind.Binding,
            binding: new BehaviorBinding(keyName: null, LiteralValue(value.Literal))));
    }

    /// <summary>Drains the collected fields in write order.</summary>
    public BehaviorField[] ToFields() => _fields.ToArray();

    private static BehaviorFieldValue LiteralValue<T>(T literal)
    {
        if (literal is null)
        {
            if (typeof(T) == typeof(string))
            {
                return new BehaviorFieldValue(BehaviorFieldKind.String, text: null);
            }

            throw LiteralOutsideModel(typeof(T));
        }

        return literal switch
        {
            string text => new BehaviorFieldValue(BehaviorFieldKind.String, text: text),
            bool flag => new BehaviorFieldValue(BehaviorFieldKind.Bool, flag: flag),
            int int32 => new BehaviorFieldValue(BehaviorFieldKind.Int32, integer: int32),
            long int64 => new BehaviorFieldValue(BehaviorFieldKind.Int64, integer: int64),
            float single => new BehaviorFieldValue(BehaviorFieldKind.Single, number: single),
            double number => new BehaviorFieldValue(BehaviorFieldKind.Double, number: number),
            Enum enumValue => new BehaviorFieldValue(BehaviorFieldKind.Enum, text: enumValue.ToString()),
            _ => throw LiteralOutsideModel(typeof(T)),
        };
    }

    private static NotSupportedException LiteralOutsideModel(Type type) =>
        new($"Binding literal of type '{type}' is outside the behavior field model (string, bool, int, long, " +
            "float, double, enum). Bind the field to a blackboard key instead, or keep the behavior " +
            "codec-serialized.");

    private void Add(string name, BehaviorFieldValue value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Field name cannot be null or empty.", nameof(name));
        }

        foreach (BehaviorField field in _fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Field '{name}' has already been written.", nameof(name));
            }
        }

        _fields.Add(new BehaviorField(name, value));
    }
}
