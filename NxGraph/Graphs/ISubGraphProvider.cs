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
    /// </summary>
    IEnumerable<Graph> SubGraphs { get; }
}
