namespace NxGraph.Graphs;

/// <summary>
/// Represents a transition in a finite state machine (FSM) graph.
/// </summary>
/// <param name="Destination">The destination node ID for the transition.</param>
public readonly struct Transition(NodeId destination)
{
    public NodeId Destination { get; } = destination;

    public static readonly Transition Empty = new(NodeId.Default);
    public bool IsEmpty => Destination == NodeId.Default;
}