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
    public GraphDto(INodeDto[] nodes, TransitionDto[] transitions, string? name)
    {
        if (nodes.Length != transitions.Length)
            throw new ArgumentException("Nodes and transitions must have the same length.", nameof(transitions));
        Nodes = nodes;
        Transitions = transitions;
        Name = name;
    }

    public INodeDto[] Nodes { get; set; }
    public TransitionDto[] Transitions { get; set; }
    public string? Name { get; set; }
}