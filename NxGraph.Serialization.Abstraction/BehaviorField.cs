namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Value kind of one serialized behavior field — the whole neutral field model (payload
/// version 8). Deliberately small: primitives, enums (as strings), and blackboard bindings.
/// Behaviors carrying richer literals implement their own encoding on top of these (or stay
/// codec-serialized); widening the model is a recorded-decision follow-up.
/// </summary>
public enum BehaviorFieldKind : byte
{
    /// <summary>A string (<see cref="BehaviorFieldValue.Text"/>; may be null).</summary>
    String = 0,

    /// <summary>A boolean (<see cref="BehaviorFieldValue.Flag"/>).</summary>
    Bool = 1,

    /// <summary>A 32-bit integer (<see cref="BehaviorFieldValue.Integer"/>).</summary>
    Int32 = 2,

    /// <summary>A 64-bit integer (<see cref="BehaviorFieldValue.Integer"/>).</summary>
    Int64 = 3,

    /// <summary>A 32-bit float (<see cref="BehaviorFieldValue.Number"/>).</summary>
    Single = 4,

    /// <summary>A 64-bit float (<see cref="BehaviorFieldValue.Number"/>).</summary>
    Double = 5,

    /// <summary>An enum rendered as its member name (<see cref="BehaviorFieldValue.Text"/>).</summary>
    Enum = 6,

    /// <summary>A blackboard binding (<see cref="BehaviorFieldValue.Binding"/>): key name or primitive literal.</summary>
    Binding = 7,

    /// <summary>
    /// A nested behavior entry list (<see cref="BehaviorFieldValue.Entries"/>) — bounded
    /// composition, carrying <c>Repeat</c>/<c>AsyncRepeat</c> bodies (payload version 9).
    /// </summary>
    Behaviors = 8,
}

/// <summary>
/// One serialized behavior field value — a codec-agnostic nested DTO, so JSON and MessagePack
/// both carry it with no wire-type coupling (unlike <see cref="IContainerCodec{TWire}"/>,
/// which is wire-typed by necessity). Exactly one payload slot is meaningful per
/// <see cref="Kind"/>.
/// </summary>
public sealed class BehaviorFieldValue(
    BehaviorFieldKind kind,
    string? text = null,
    bool flag = false,
    long integer = 0,
    double number = 0,
    BehaviorBinding? binding = null,
    BehaviorEntry[]? entries = null)
{
    /// <summary>The value kind, deciding which payload slot is meaningful.</summary>
    public BehaviorFieldKind Kind { get; } = kind;

    /// <summary>String/Enum payload; a binding's key name rides on <see cref="Binding"/> instead.</summary>
    public string? Text { get; } = text;

    /// <summary>Bool payload.</summary>
    public bool Flag { get; } = flag;

    /// <summary>Int32/Int64 payload.</summary>
    public long Integer { get; } = integer;

    /// <summary>Single/Double payload.</summary>
    public double Number { get; } = number;

    /// <summary>Binding payload; null for every other kind.</summary>
    public BehaviorBinding? Binding { get; } = binding;

    /// <summary>Nested behavior entries payload (payload version 9); null for every other kind.</summary>
    public BehaviorEntry[]? Entries { get; } = entries;
}

/// <summary>
/// One serialized behavior entry (payload version 9 lifted this out of the serializer's
/// internal DTOs so nested entries can ride the neutral field model): the behavior's
/// runtime-stable CLR type name — the same rendering the blackboard payloads use — plus its
/// fields. The recursion closure: a <see cref="BehaviorFieldKind.Behaviors"/> field carries
/// entries, and each entry carries fields.
/// </summary>
public sealed class BehaviorEntry(string behaviorTypeName, BehaviorField[] fields)
{
    /// <summary>The behavior's runtime-stable CLR type name — the registry's lookup identity.</summary>
    public string BehaviorTypeName { get; } = behaviorTypeName;

    /// <summary>The entry's fields in write order.</summary>
    public BehaviorField[] Fields { get; } = fields;
}

/// <summary>
/// A serialized blackboard binding: the key form carries only the key's registered
/// <see cref="KeyName"/> (rebound by name against the machine's bound boards at execution);
/// the literal form carries a primitive <see cref="Literal"/> value (whose kind is never
/// <see cref="BehaviorFieldKind.Binding"/>).
/// </summary>
public sealed class BehaviorBinding(string? keyName, BehaviorFieldValue? literal)
{
    /// <summary>The bound key's registered name, or null for the literal form.</summary>
    public string? KeyName { get; } = keyName;

    /// <summary>The primitive literal, or null for the key form.</summary>
    public BehaviorFieldValue? Literal { get; } = literal;
}

/// <summary>One named field of a serialized behavior.</summary>
public sealed class BehaviorField(string name, BehaviorFieldValue value)
{
    /// <summary>The field name — unique per behavior, the reader's lookup identity.</summary>
    public string Name { get; } = name;

    /// <summary>The field's value.</summary>
    public BehaviorFieldValue Value { get; } = value;
}
