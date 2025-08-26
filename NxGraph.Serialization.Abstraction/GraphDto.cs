using System.Runtime.Serialization;

namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Minimal DTO for a graph that can be serialized/deserialized.
/// Keeps indices stable and captures names and edges.
/// </summary>
[DataContract]
public sealed class GraphDto
{
    /// <summary>
    /// Minimal DTO for a graph that can be serialized/deserialized.
    /// Keeps indices stable and captures names and edges.
    /// </summary>
    public GraphDto(INodeDto[] nodes, TransitionDto[] transitions, string? name)
    {
        if( nodes.Length != transitions.Length)
            throw new ArgumentException("Nodes and transitions must have the same length.", nameof(transitions));
        Nodes = nodes;
        Transitions = transitions;
        Name = name;
    }

    [DataMember(Order = 0)] public INodeDto[] Nodes { get; set; }
    [DataMember(Order = 1)] public TransitionDto[] Transitions { get; set; }
    [DataMember(Order = 2)] public string? Name { get; set; }
}