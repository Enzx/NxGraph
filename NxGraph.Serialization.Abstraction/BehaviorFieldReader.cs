using NxGraph.Behaviors;

namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Reads a behavior's fields back from the neutral field model (payload version 8) — the
/// counterpart handed to registry factories on deserialization. Lookups are by name; a
/// missing field or kind mismatch throws a targeted error, because a behavior payload that
/// lost a field is corrupt, not evolvable (schema evolution happens in the factory, which can
/// probe with <see cref="Has"/>).
/// </summary>
public sealed class BehaviorFieldReader
{
    private readonly IReadOnlyList<BehaviorField> _fields;
    private readonly IBehaviorEntryCodec? _entryCodec;

    /// <summary>Wraps a field list (in write order) — standalone, no nested-entry support (see <see cref="ReadBehaviors"/>).</summary>
    public BehaviorFieldReader(IReadOnlyList<BehaviorField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = fields;
    }

    internal BehaviorFieldReader(IReadOnlyList<BehaviorField> fields, IBehaviorEntryCodec? entryCodec)
        : this(fields)
    {
        _entryCodec = entryCodec;
    }

    /// <summary><see langword="true"/> when a field named <paramref name="name"/> exists.</summary>
    public bool Has(string name) => Find(name) is not null;

    /// <summary>Reads a string field.</summary>
    public string? ReadString(string name) => Require(name, BehaviorFieldKind.String).Text;

    /// <summary>Reads a boolean field.</summary>
    public bool ReadBool(string name) => Require(name, BehaviorFieldKind.Bool).Flag;

    /// <summary>Reads a 32-bit integer field.</summary>
    public int ReadInt32(string name) => checked((int)Require(name, BehaviorFieldKind.Int32).Integer);

    /// <summary>Reads a 64-bit integer field.</summary>
    public long ReadInt64(string name) => Require(name, BehaviorFieldKind.Int64).Integer;

    /// <summary>Reads a 32-bit float field.</summary>
    public float ReadSingle(string name) => (float)Require(name, BehaviorFieldKind.Single).Number;

    /// <summary>Reads a 64-bit float field.</summary>
    public double ReadDouble(string name) => Require(name, BehaviorFieldKind.Double).Number;

    /// <summary>Reads an enum field from its member name.</summary>
    public TEnum ReadEnum<TEnum>(string name) where TEnum : struct, Enum
    {
        BehaviorFieldValue value = Require(name, BehaviorFieldKind.Enum);
        if (value.Text is not null && Enum.TryParse(value.Text, out TEnum parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Behavior field '{name}' carries enum member '{value.Text}', which is not defined on " +
            $"'{typeof(TEnum)}'.");
    }

    /// <summary>
    /// Reads a blackboard binding: the key form rebuilds as a name-bound
    /// <see cref="BlackboardValue{T}"/> (resolved against the machine's bound boards per
    /// execution); the literal form rebuilds the primitive, checked against
    /// <typeparamref name="T"/>.
    /// </summary>
    public BlackboardValue<T> ReadBinding<T>(string name)
    {
        BehaviorFieldValue value = Require(name, BehaviorFieldKind.Binding);
        BehaviorBinding binding = value.Binding ?? throw new InvalidOperationException(
            $"Behavior field '{name}' is a binding but carries no binding payload.");

        if (binding.KeyName is { } keyName)
        {
            return BlackboardValue<T>.Bound(keyName);
        }

        BehaviorFieldValue literal = binding.Literal ?? throw new InvalidOperationException(
            $"Behavior field '{name}' is a binding but carries neither a key name nor a literal.");
        if (literal.Kind == BehaviorFieldKind.Binding)
        {
            throw new InvalidOperationException(
                $"Behavior field '{name}' nests a binding inside a binding literal — corrupt payload.");
        }

        return LiteralOf<T>(name, literal);
    }

    /// <summary>
    /// Reads a nested behavior entry list (payload version 9) back as <b>live instances</b> —
    /// a <c>Repeat</c> body. Each entry is reconstructed recursively through the serializer's
    /// registry-first per-entry dispatch, with the usual targeted error for unregistered
    /// names. Only operates inside a <c>GraphSerializer</c> payload session — the serializer
    /// wires the entry codec into the readers it creates; a standalone reader throws a
    /// targeted error.
    /// </summary>
    public object[] ReadBehaviors(string name)
    {
        if (_entryCodec is null)
        {
            throw new InvalidOperationException(
                "ReadBehaviors only operates inside a GraphSerializer payload session — nested behavior " +
                "entries are reconstructed by the serializer's entry codec, which is not wired on a " +
                "standalone BehaviorFieldReader.");
        }

        BehaviorFieldValue value = Require(name, BehaviorFieldKind.Behaviors);
        BehaviorEntry[] entries = value.Entries ?? throw new InvalidOperationException(
            $"Behavior field '{name}' is a behavior list but carries no entries payload.");

        object[] live = new object[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            live[i] = _entryCodec.ReadEntry(entries[i]);
        }

        return live;
    }

    private static BlackboardValue<T> LiteralOf<T>(string name, BehaviorFieldValue literal)
    {
        if (typeof(T).IsEnum)
        {
            if (literal.Kind != BehaviorFieldKind.Enum || literal.Text is null)
            {
                throw LiteralMismatch<T>(name, literal.Kind);
            }

            try
            {
                return (T)Enum.Parse(typeof(T), literal.Text);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    $"Behavior field '{name}' carries enum member '{literal.Text}', which is not defined on " +
                    $"'{typeof(T)}'.");
            }
        }

        object? raw = literal.Kind switch
        {
            BehaviorFieldKind.String => literal.Text,
            BehaviorFieldKind.Bool => literal.Flag,
            BehaviorFieldKind.Int32 => (int)literal.Integer,
            BehaviorFieldKind.Int64 => literal.Integer,
            BehaviorFieldKind.Single => (float)literal.Number,
            BehaviorFieldKind.Double => literal.Number,
            _ => throw LiteralMismatch<T>(name, literal.Kind),
        };

        if (raw is null)
        {
            // Only the string kind writes null literals, and only for T == string.
            return typeof(T) == typeof(string)
                ? default(T)!
                : throw LiteralMismatch<T>(name, literal.Kind);
        }

        if (raw is T typed)
        {
            return typed;
        }

        throw LiteralMismatch<T>(name, literal.Kind);
    }

    private static InvalidOperationException LiteralMismatch<T>(string name, BehaviorFieldKind kind) =>
        new($"Behavior field '{name}' carries a {kind} binding literal, which does not match the expected " +
            $"value type '{typeof(T)}'.");

    private BehaviorFieldValue Require(string name, BehaviorFieldKind kind)
    {
        BehaviorFieldValue? value = Find(name);
        if (value is null)
        {
            throw new InvalidOperationException($"Behavior field '{name}' is missing from the payload.");
        }

        if (value.Kind != kind)
        {
            throw new InvalidOperationException(
                $"Behavior field '{name}' has kind {value.Kind}, expected {kind}.");
        }

        return value;
    }

    private BehaviorFieldValue? Find(string name)
    {
        foreach (BehaviorField field in _fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                return field.Value;
            }
        }

        return null;
    }
}
