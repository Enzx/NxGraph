using System.Runtime.CompilerServices;
using NxGraph.Compatibility;

namespace NxGraph.Blackboards;

/// <summary>
/// Batteries-included typed-key context store. Construction allocates all slot storage up
/// front; <see cref="Get{T}"/>/<see cref="Set{T}"/>/<see cref="TryGet{T}"/>/<see cref="GetRef{T}"/>
/// are zero-allocation and zero-boxing thereafter — including first use.
/// <para>
/// Owned by a single runner at a time (not thread-safe), like the state machines.
/// </para>
/// <para>
/// Anti-pattern: do <b>not</b> reach for <c>Dictionary&lt;string, object&gt;</c> as a context —
/// that costs string hashing plus boxing on every access. A <see cref="BlackboardKey{T}"/>
/// slot is a schema-checked array read instead.
/// </para>
/// </summary>
public sealed class Blackboard
{
    private readonly BlackboardSchema _schema;
    private readonly ValueStorage[] _storages;

    /// <summary>
    /// Creates a board over <paramref name="schema"/>, freezing the schema and eagerly
    /// materializing every slot with its registered default.
    /// </summary>
    public Blackboard(BlackboardSchema schema)
    {
        _schema = Guard.NotNull(schema, nameof(schema));
        schema.Freeze();
        _storages = schema.CreateStorages();
    }

    /// <summary>The frozen schema this board was created from.</summary>
    public BlackboardSchema Schema => _schema;

    /// <summary>Reads the slot for <paramref name="key"/>. Throws for an invalid or foreign-schema key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get<T>(BlackboardKey<T> key)
    {
        ValidateKey(key);
        return ((ValueStorage<T>)_storages[key.StorageIndex]).Values[key.SlotIndex];
    }

    /// <summary>Writes the slot for <paramref name="key"/>. Throws for an invalid or foreign-schema key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<T>(BlackboardKey<T> key, T value)
    {
        ValidateKey(key);
        ((ValueStorage<T>)_storages[key.StorageIndex]).Values[key.SlotIndex] = value;
    }

    /// <summary>
    /// Returns a reference into the slot array for in-place mutation of struct values.
    /// Throws for an invalid or foreign-schema key.
    /// </summary>
    public ref T GetRef<T>(BlackboardKey<T> key)
    {
        ValidateKey(key);
        return ref ((ValueStorage<T>)_storages[key.StorageIndex]).Values[key.SlotIndex];
    }

    /// <summary>
    /// Reads the slot for <paramref name="key"/>; returns <see langword="false"/> (instead of
    /// throwing) for a default-constructed key or a key from another schema.
    /// </summary>
    public bool TryGet<T>(BlackboardKey<T> key, out T value)
    {
        if (!ReferenceEquals(key.Schema, _schema) || key.Schema is null)
        {
            value = default!;
            return false;
        }

        value = ((ValueStorage<T>)_storages[key.StorageIndex]).Values[key.SlotIndex];
        return true;
    }

    /// <summary>Resets every slot back to its registered default. Explicit non-loop operation.</summary>
    public void ResetToDefaults()
    {
        foreach (ValueStorage storage in _storages)
        {
            storage.ResetToDefaults();
        }
    }

    // ── Key validation (throw paths never inline into the hot path) ────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateKey<T>(in BlackboardKey<T> key)
    {
        if (!ReferenceEquals(key.Schema, _schema))
        {
            ThrowInvalidKey(key.Schema, key.Name, _schema);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidKey(BlackboardSchema? keySchema, string? keyName, BlackboardSchema boardSchema)
    {
        if (keySchema is null)
        {
            throw new InvalidOperationException(
                "Uninitialized blackboard key — obtain keys via BlackboardSchema.Register<T>(...).");
        }

        throw new InvalidOperationException(
            $"Key '{keyName}' belongs to schema '{keySchema.Name ?? "<unnamed>"}' but this blackboard " +
            $"uses schema '{boardSchema.Name ?? "<unnamed>"}'.");
    }
}
