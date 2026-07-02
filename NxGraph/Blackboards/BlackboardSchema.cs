using NxGraph.Compatibility;

namespace NxGraph.Blackboards;

/// <summary>
/// Reusable, immutable-after-freeze layout of typed slots. Register all keys up front
/// (typically in static initializers); the schema freezes when the first
/// <see cref="Blackboard"/> is constructed from it. One schema can back any number of
/// independent blackboards — the sanctioned "one graph template, N entities" pattern.
/// <para>
/// Registration is not thread-safe; do it from a single thread during setup.
/// </para>
/// </summary>
public sealed class BlackboardSchema
{
    private readonly Dictionary<Type, int> _storageIndexByType = new();
    private readonly List<StorageBuilder> _storageBuilders = [];
    private readonly Dictionary<string, int> _ordinalByName = new(StringComparer.Ordinal);
    private readonly List<BlackboardKeyDescriptor> _descriptors = [];

    /// <summary>
    /// Creates a schema. <paramref name="scope"/> decides which <see cref="BlackboardContext"/>
    /// slot the schema's keys route to; <see cref="BlackboardScope.Node"/> is reserved and rejected.
    /// </summary>
    public BlackboardSchema(string? name = null, BlackboardScope scope = BlackboardScope.Graph)
    {
        if (scope == BlackboardScope.Node)
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope,
                "BlackboardScope.Node is reserved for transient per-node keys and is not yet implemented.");
        }

        if (scope is not (BlackboardScope.Global or BlackboardScope.Graph))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Undefined blackboard scope.");
        }

        Name = name;
        Scope = scope;
    }

    /// <summary>Optional display name, used in diagnostics and serialization payloads.</summary>
    public string? Name { get; }

    /// <summary>The binding slot this schema's keys route to.</summary>
    public BlackboardScope Scope { get; }

    /// <summary><see langword="true"/> once the first <see cref="Blackboard"/> has been constructed.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>Number of registered keys.</summary>
    public int KeyCount => _descriptors.Count;

    /// <summary>All registered slots in registration order (index == ordinal).</summary>
    public IReadOnlyList<BlackboardKeyDescriptor> Keys => _descriptors;

    /// <summary>Registers a slot whose default value is <c>default(T)</c>.</summary>
    public BlackboardKey<T> Register<T>(string name) => Register<T>(name, default!);

    /// <summary>Registers a slot with an explicit default value. Names must be unique per schema.</summary>
    public BlackboardKey<T> Register<T>(string name, T defaultValue)
    {
        Guard.NotNull(name, nameof(name));

        if (IsFrozen)
        {
            throw new InvalidOperationException(
                $"Schema '{Name ?? "<unnamed>"}' is frozen — register all keys before constructing the first " +
                "Blackboard. Evolve schemas by adding keys in code; old payloads restore with defaults for new keys.");
        }

        if (_ordinalByName.ContainsKey(name))
        {
            throw new ArgumentException($"Key '{name}' is already registered on schema '{Name ?? "<unnamed>"}'.",
                nameof(name));
        }

        if (!_storageIndexByType.TryGetValue(typeof(T), out int storageIndex))
        {
            storageIndex = _storageBuilders.Count;
            _storageBuilders.Add(new StorageBuilder<T>());
            _storageIndexByType.Add(typeof(T), storageIndex);
        }

        StorageBuilder<T> builder = (StorageBuilder<T>)_storageBuilders[storageIndex];
        int slotIndex = builder.Add(defaultValue);
        int ordinal = _descriptors.Count;

        BlackboardKey<T> key = new(this, name, storageIndex, slotIndex, ordinal);
        _ordinalByName.Add(name, ordinal);
        _descriptors.Add(new SlotDefinition<T>(key, defaultValue));
        return key;
    }

    /// <summary>Looks up a slot descriptor by registered name.</summary>
    public bool TryGetKey(string name, out BlackboardKeyDescriptor descriptor)
    {
        if (_ordinalByName.TryGetValue(name, out int ordinal))
        {
            descriptor = _descriptors[ordinal];
            return true;
        }

        descriptor = null!;
        return false;
    }

    internal void Freeze() => IsFrozen = true;

    /// <summary>Produces a fresh, independent storage set for one <see cref="Blackboard"/>.</summary>
    internal ValueStorage[] CreateStorages()
    {
        ValueStorage[] storages = new ValueStorage[_storageBuilders.Count];
        for (int i = 0; i < storages.Length; i++)
        {
            storages[i] = _storageBuilders[i].CreateStorage();
        }

        return storages;
    }

    // ── Per-type default templates ──────────────────────────────────────

    private abstract class StorageBuilder
    {
        internal abstract ValueStorage CreateStorage();
    }

    private sealed class StorageBuilder<T> : StorageBuilder
    {
        private readonly List<T> _defaults = [];
        private T[]? _template;

        internal int Add(T defaultValue)
        {
            _defaults.Add(defaultValue);
            return _defaults.Count - 1;
        }

        internal override ValueStorage CreateStorage()
        {
            _template ??= _defaults.ToArray();
            return new ValueStorage<T>(_template);
        }
    }
}
