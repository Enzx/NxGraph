using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Optional extension points for <see cref="GraphSerializer"/> (payload version 6). With
/// both left null the serializer behaves exactly like the single-argument constructor:
/// dynamic-parallel composites and custom containers fail with targeted errors naming the
/// option that unlocks them. Token fork/join nodes need no configuration — their structure
/// is plain data and always rides.
/// </summary>
public sealed class GraphSerializerOptions
{
    /// <summary>
    /// Resolves dynamic-parallel region selectors to named keys (write) and back (read).
    /// Required to serialize or deserialize graphs containing
    /// <c>AsyncDynamicParallelState</c> / <c>DynamicParallelState</c> nodes.
    /// </summary>
    public IRegionSelectorRegistry? SelectorRegistry { get; init; }

    /// <summary>
    /// Owns the reconstruction recipe for custom <c>ISubGraphProvider</c> containers
    /// (including <c>AsyncCompositeState</c> subclasses). Its wire type must match the
    /// logic codec's — the container payload rides in the same node DTO slot.
    /// </summary>
    public IContainerCodec? ContainerCodec { get; init; }
}
