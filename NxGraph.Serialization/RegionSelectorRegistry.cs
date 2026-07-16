using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Default <see cref="IRegionSelectorRegistry"/>: a bidirectional key ↔ delegate map built
/// at registration time. Delegate identity is reference identity, so author the graph with
/// the instance <see cref="Register"/> returns:
/// <code>
/// var sel = registry.Register("battle", ctx => ...);
/// ... .Parallel(sel, region1, region2)
/// </code>
/// Two lambdas with identical bodies are distinct delegates — a graph authored with an
/// unregistered twin fails the write-side lookup.
/// </summary>
public sealed class RegionSelectorRegistry : IRegionSelectorRegistry
{
    private readonly Dictionary<string, Func<BlackboardContext, RegionMask>> _byKey = new();
    private readonly Dictionary<Func<BlackboardContext, RegionMask>, string> _byDelegate = new();

    /// <summary>
    /// Registers <paramref name="selector"/> under <paramref name="key"/> and returns the
    /// delegate, so authoring uses the registered instance naturally. Duplicate keys and
    /// duplicate delegates fail here, at setup, rather than at save time.
    /// </summary>
    public Func<BlackboardContext, RegionMask> Register(string key, Func<BlackboardContext, RegionMask> selector)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(selector);
        if (_byKey.ContainsKey(key))
        {
            throw new ArgumentException($"A selector is already registered under key '{key}'.", nameof(key));
        }

        if (_byDelegate.TryGetValue(selector, out string? existingKey))
        {
            throw new ArgumentException(
                $"This selector delegate is already registered under key '{existingKey}'.", nameof(selector));
        }

        _byKey.Add(key, selector);
        _byDelegate.Add(selector, key);
        return selector;
    }

    public bool TryGetKey(Func<BlackboardContext, RegionMask> selector, out string key)
    {
        if (_byDelegate.TryGetValue(selector, out string? found))
        {
            key = found;
            return true;
        }

        key = string.Empty;
        return false;
    }

    public bool TryGetSelector(string key, out Func<BlackboardContext, RegionMask> selector)
    {
        if (_byKey.TryGetValue(key, out Func<BlackboardContext, RegionMask>? found))
        {
            selector = found;
            return true;
        }

        selector = null!;
        return false;
    }
}
