using NxGraph.Graphs;

namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Non-generic marker for container codecs — mirrors <see cref="ILogicCodec"/>. A container
/// codec (payload version 6) serializes custom <see cref="ISubGraphProvider"/> containers
/// (including <c>AsyncCompositeState</c> subclasses) that the graph serializer has no
/// built-in recipe for.
/// </summary>
public interface IContainerCodec;

/// <summary>
/// Division of labor with the graph serializer: the <b>serializer</b> owns child-graph
/// recursion — it walks the container's <see cref="ISubGraphProvider.SubGraphs"/> onto the
/// wire and rebuilds them on read; the <b>codec</b> owns the reconstruction recipe — its
/// opaque payload rides in the node's ordinary logic slot (typically a key plus config,
/// reusing the user's logic-codec keys for any non-graph children).
/// <para>
/// Order contract (normative): <see cref="ISubGraphProvider.SubGraphs"/> enumeration order
/// = wire order = the <c>children</c> order passed to
/// <see cref="Deserialize"/> — order is identity, exactly as region order is for
/// <c>RegionMask</c> bits. The rebuilt container must surface the <b>new</b>
/// <see cref="Graph"/> instances via its own <c>SubGraphs</c> and keep
/// <c>IBlackboardSettable</c> forwarding — those walks are what agent stamping and
/// blackboard binding reach (the standing user-container contract). A container that hides
/// graphs from <c>SubGraphs</c> serializes without them; such a container is already broken
/// for stamping, and the serializer inherits that contract rather than validating it.
/// </para>
/// <para>
/// <see cref="Serialize"/> receives the <b>unwrapped</b> container (a sync-only container
/// is unwrapped from its <c>SyncLogicAdapter</c> first); a codec rebuilding a sync-only
/// container must wrap the result in the public <c>SyncLogicAdapter</c> itself so the node
/// stays sync-runnable.
/// </para>
/// </summary>
/// <typeparam name="TWire">
/// The wire type — must match the logic codec's wire type, because the container payload
/// rides in the same node DTO slot; the graph serializer rejects a mismatch at construction.
/// </typeparam>
public interface IContainerCodec<TWire> : IContainerCodec
{
    /// <summary>Encodes the reconstruction recipe for <paramref name="container"/> (not its child graphs).</summary>
    TWire Serialize(ISubGraphProvider container);

    /// <summary>
    /// Rebuilds the container from its <paramref name="payload"/> and the already-rebuilt
    /// <paramref name="children"/> (in <c>SubGraphs</c> enumeration order). Must not return null.
    /// </summary>
    IAsyncLogic Deserialize(TWire payload, IReadOnlyList<Graph> children);
}
