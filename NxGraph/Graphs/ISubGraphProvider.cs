namespace NxGraph.Graphs;

/// <summary>
/// Implemented by node logic that embeds child graphs (nested state machines, history and
/// parallel composites, user-defined containers). Graph-wide operations that must reach
/// every node — agent injection today, tooling walks tomorrow — discover nested graphs
/// through this interface instead of hard-coding each composite type.
/// </summary>
public interface ISubGraphProvider
{
    /// <summary>
    /// The directly-embedded child graphs (one level deep — nested providers inside a
    /// child graph are discovered recursively by the caller).
    /// <para>
    /// Hard requirement: enumeration must be <b>stable and deterministic</b> — every
    /// enumeration yields the same graphs in the same order. Order is identity for this
    /// walk's consumers: the graph serializer writes children to the wire in enumeration
    /// order and hands them back to the container codec in that order on read, and the
    /// agent/blackboard stamping walks visit in it. Backing this property with an unordered
    /// collection corrupts serialized reconstruction silently (the serializer verifies the
    /// contract in DEBUG builds with a second enumeration).
    /// </para>
    /// </summary>
    IEnumerable<Graph> SubGraphs { get; }
}
