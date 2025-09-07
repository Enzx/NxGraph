namespace NxGraph.Serialization;

/// <summary>
/// Minimal DTO for a graph that can be serialized/deserialized.
/// Keeps indices stable and captures names and edges.
/// </summary>
internal sealed class GraphDto
{
    /// <summary>
    /// Minimal DTO for a graph that can be serialized/deserialized.
    /// Keeps indices stable and captures names and edges.
    /// </summary>
    public GraphDto(INodeDto[] nodes, TransitionDto[] transitions,SubGraphDto[]? subGraphs = null, int index = -1, string? name = null)
    {
        if (nodes.Length != transitions.Length)
            throw new ArgumentException("Nodes and transitions must have the same length.", nameof(transitions));
        Nodes = nodes;
        Transitions = transitions;
        SubGraphs =   subGraphs ?? [];
        Name = name;
        Index = index;
    }

    public static int Version => SerializationVersion.Version;

    public INodeDto[] Nodes { get; set; }
    public TransitionDto[] Transitions { get; set; }
    public SubGraphDto[] SubGraphs { get; set; }
    public string? Name { get; set; }
    public int Index { get; set; }

}