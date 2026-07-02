namespace NxGraph.Blackboards;

/// <summary>
/// Typed handle to one slot in a <see cref="BlackboardSchema"/>. Obtain via
/// <see cref="BlackboardSchema.Register{T}(string)"/>; a default-constructed key is invalid
/// and any access through it throws.
/// <para>
/// Identity is the owning schema (by reference) plus the registration ordinal; the
/// <see cref="Name"/> is carried for diagnostics and serialization only.
/// </para>
/// </summary>
public readonly struct BlackboardKey<T> : IEquatable<BlackboardKey<T>>
{
    internal readonly BlackboardSchema? Schema;
    internal readonly int StorageIndex;
    internal readonly int SlotIndex;
    internal readonly int Ordinal;

    internal BlackboardKey(BlackboardSchema schema, string name, int storageIndex, int slotIndex, int ordinal)
    {
        Schema = schema;
        Name = name;
        StorageIndex = storageIndex;
        SlotIndex = slotIndex;
        Ordinal = ordinal;
    }

    /// <summary>The name the key was registered under (diagnostics/serialization identity).</summary>
    public string Name { get; }

    /// <summary><see langword="false"/> for a default-constructed key.</summary>
    public bool IsValid => Schema is not null;

    /// <inheritdoc />
    public bool Equals(BlackboardKey<T> other) =>
        ReferenceEquals(Schema, other.Schema) && Ordinal == other.Ordinal;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BlackboardKey<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        Schema is null ? Ordinal : (Schema.GetHashCode() * 397) ^ Ordinal;

    public static bool operator ==(BlackboardKey<T> left, BlackboardKey<T> right) => left.Equals(right);

    public static bool operator !=(BlackboardKey<T> left, BlackboardKey<T> right) => !left.Equals(right);

    /// <summary>Diagnostics only — "name:Type".</summary>
    public override string ToString() =>
        Schema is null ? $"<invalid>:{typeof(T).Name}" : $"{Name}:{typeof(T).Name}";
}
