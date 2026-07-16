using NxGraph.Blackboards;
using NxGraph.Fsm;

namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Maps dynamic-parallel region selectors to stable string keys so they can ride the graph
/// payload (payload version 6): the write side turns the authored delegate into a key, the
/// read side turns the key back into a delegate. Delegates themselves cannot ride a wire
/// payload — the registry is the durable identity for them, analogous to how an
/// <see cref="ILogicCodec"/> keys node logic.
/// <para>
/// Both lookups follow the try-pattern so the serializer owns the error messages and can
/// include node context. The trust model matches <see cref="ILogicCodec"/>: the registry
/// restores <i>a</i> delegate for the key — nothing verifies it behaves like the delegate
/// the graph was authored with; that contract belongs to the user.
/// </para>
/// <para>
/// Delegate identity is reference identity: two lambdas with identical bodies are distinct
/// delegates, so the graph must be authored with the <b>registered instance</b> for the
/// write-side lookup to succeed.
/// </para>
/// </summary>
public interface IRegionSelectorRegistry
{
    /// <summary>Write side: resolves the key an authored selector was registered under.</summary>
    bool TryGetKey(Func<BlackboardContext, RegionMask> selector, out string key);

    /// <summary>Read side: resolves the selector registered under a payload key.</summary>
    bool TryGetSelector(string key, out Func<BlackboardContext, RegionMask> selector);
}
