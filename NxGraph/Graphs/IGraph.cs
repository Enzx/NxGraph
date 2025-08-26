namespace NxGraph.Graphs;

public interface IGraph
{
    /// <summary>
    /// Represents the unique identifier for the graph.
    /// </summary>
    NodeId Id { get; }
    /// <summary>
    /// Represents the start node of the graph, which is the entry point for execution.
    /// </summary>
    Node StartNode { get; }
    /// <summary>
    /// Represents the total number of nodes in the graph, including the start node.
    /// </summary>
    int NodeCount { get; }
    
    /// <summary>
    /// Represents the total number of transitions in the graph, which are connections between nodes.
    /// </summary>
    int TransitionCount { get; }
    /// <summary>
    /// Attempts to retrieve the transition from a given node.
    /// </summary>
    /// <param name="from">The source node ID from which the transition originates.</param>
    /// <param name="transition">The transition to the destination node, if found.</param>
    /// <returns><c>true</c> if the transition exists; otherwise, <c>false</c>.</returns>
    bool TryGetTransition(NodeId from, out Transition transition);
    /// <summary>
    /// Attempts to retrieve a node by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the node to retrieve.</param>
    /// <param name="node">The node corresponding to the given ID, if found; otherwise, <see cref="Node.Empty"/>.</param>
    /// <returns><c>true</c> if the node exists; otherwise, <c>false</c>.</returns>
    bool TryGetNode(NodeId id, out Node node);
    /// <summary>
    /// Sets the agent for the graph, allowing it to interact with the graph's nodes and transitions.
    /// </summary>
    /// <param name="agent">The agent instance to be set for the graph.</param>
    /// <typeparam name="TAgent">The type of the agent to set.</typeparam>
    void SetAgent<TAgent>(TAgent agent);
}