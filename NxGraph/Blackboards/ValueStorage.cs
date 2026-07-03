namespace NxGraph.Blackboards;

/// <summary>
/// Non-generic base for the per-type slot arrays backing a <see cref="Blackboard"/>.
/// </summary>
internal abstract class ValueStorage
{
    /// <summary>Copies the schema's default template back into the live slots.</summary>
    internal abstract void ResetToDefaults();
}

/// <summary>
/// Slot array for all keys of value type <typeparamref name="T"/> registered on a schema.
/// The live <see cref="Values"/> array is per-blackboard; the defaults template is shared
/// with the schema.
/// </summary>
internal sealed class ValueStorage<T> : ValueStorage
{
    internal readonly T[] Values;
    private readonly T[] _defaults;

    internal ValueStorage(T[] defaults)
    {
        _defaults = defaults;
        Values = new T[defaults.Length];
        Array.Copy(defaults, Values, defaults.Length);
    }

    internal override void ResetToDefaults() => Array.Copy(_defaults, Values, Values.Length);
}
