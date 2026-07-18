namespace NxGraph.Blackboards;

/// <summary>
/// Type-erased view of one schema slot, exposed via <see cref="BlackboardSchema.Keys"/>.
/// This is the serialization contract: serializers enumerate the descriptors and use the
/// boxed accessors — boxing is fine here because save/restore are explicit non-loop
/// operations (same rule as <c>StateMachineSnapshot</c>).
/// </summary>
public abstract class BlackboardKeyDescriptor
{
    private protected BlackboardKeyDescriptor(string name, Type valueType, int ordinal)
    {
        Name = name;
        ValueType = valueType;
        Ordinal = ordinal;
    }

    /// <summary>The name the key was registered under.</summary>
    public string Name { get; }

    /// <summary>The slot's value type.</summary>
    public Type ValueType { get; }

    /// <summary>Registration order across the whole schema.</summary>
    public int Ordinal { get; }

    /// <summary>Reads the slot's current value from <paramref name="blackboard"/> (boxes value types).</summary>
    public abstract object? GetValue(Blackboard blackboard);

    /// <summary>
    /// Writes <paramref name="value"/> into the slot on <paramref name="blackboard"/>.
    /// Throws <see cref="InvalidCastException"/> when the value is not of <see cref="ValueType"/>.
    /// </summary>
    public abstract void SetValue(Blackboard blackboard, object? value);

    /// <summary>Resets the slot on <paramref name="blackboard"/> back to its registered default.</summary>
    public abstract void ResetValue(Blackboard blackboard);
}

/// <summary>Concrete descriptor bound to a typed key.</summary>
internal sealed class SlotDefinition<T> : BlackboardKeyDescriptor
{
    private readonly BlackboardKey<T> _key;
    private readonly T _defaultValue;

    internal SlotDefinition(BlackboardKey<T> key, T defaultValue)
        : base(key.Name, typeof(T), key.Ordinal)
    {
        _key = key;
        _defaultValue = defaultValue;
    }

    /// <summary>The typed key this slot was registered under (schema rebinding lookups).</summary>
    internal BlackboardKey<T> Key => _key;

    public override object? GetValue(Blackboard blackboard) => blackboard.Get(_key);

    public override void SetValue(Blackboard blackboard, object? value) => blackboard.Set(_key, (T)value!);

    public override void ResetValue(Blackboard blackboard) => blackboard.Set(_key, _defaultValue);
}
