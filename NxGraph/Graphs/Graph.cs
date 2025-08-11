using System.Runtime.CompilerServices;
using NxGraph.Fsm;

namespace NxGraph.Graphs;

/// <summary>
/// Represents a directed graph structure used in finite state machines (FSMs) or other graph-based systems.
/// </summary>
public sealed class Graph : IGraph
{
    private readonly Node[] _nodes; // index = NodeId.Index
    private readonly Transition[] _edges; // index = from.Index

    /// <summary>
    /// The unique identifier for this graph. 
    /// </summary>
    /// <remarks>
    /// This ID is immutable after construction and may differ from its node IDs if this graph is a subgraph of another.
    /// </remarks>
    public NodeId Id { get; internal set; }

    /// <summary>
    /// The start node of the graph, which is always NodeId.Start (index 0).
    /// </summary>
    public Node StartNode { get; }

    /// <summary>
    /// The number of nodes in the graph, including the start node.
    /// </summary>
    public int NodeCount => _nodes.Length;


    internal Graph(NodeId id, Node start, Node[] nodes, Transition[] edges)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        if (start.Id != NodeId.Start)
        {
            throw new ArgumentException("Start node must be NodeId.Start (index 0).", nameof(start));
        }

        if (nodes.Length == 0 || edges.Length == 0 || nodes.Length != edges.Length)
        {
            throw new ArgumentException("Nodes/edges arrays must be non-empty and have equal length.");
        }

        Id = id;
        StartNode = start;
        _nodes = nodes;
        _edges = edges;
    }

    /// <summary>
    /// Attempts to retrieve the transition from a given node.
    /// </summary>
    /// <param name="from">The source node ID from which the transition originates.</param>
    /// <param name="transition">The transition to the destination node, if found.</param>
    /// <returns><c>true</c> if the transition exists; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTransition(NodeId from, out Transition transition)
    {
        if ((uint)from.Index >= (uint)_edges.Length)
        {
            transition = Transition.Empty;
            return false;
        }

        transition = _edges[from.Index];
        return true;
    }

    /// <summary>
    /// Attempts to retrieve a node by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the node to retrieve.</param>
    /// <param name="node">The node corresponding to the given ID, if found; otherwise, <see cref="Node.Empty"/>.</param>
    /// <returns><c>true</c> if the node exists; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNode(NodeId id, out Node node)
    {
        if ((uint)id.Index >= (uint)_nodes.Length)
        {
            node = Node.Empty;
            return false;
        }

        Node candidate = _nodes[id.Index];
        if (candidate.Id == id)
        {
            node = candidate;
            return true;
        }

        node = Node.Empty;
        return false;
    }

    /// <summary>
    /// Sets the agent for all nodes in the graph that implement <see cref="IAgentSettable{TAgent}"/>.
    /// </summary>
    /// <param name="agent">The agent to set for the nodes.</param>
    /// <typeparam name="TAgent">The type of the agent to set.</typeparam>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="agent"/> is null.</exception>
    public void SetAgent<TAgent>(TAgent agent)
    {
        if (agent is null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        bool found = false;
        for (int i = 0; i < _nodes.Length; i++)
        {
            INode logic = _nodes[i].Logic;
            if (logic is not IAgentSettable<TAgent> settable)
            {
                continue;
            }

            found = true;
            settable.SetAgent(agent);
        }

        if (!found)
        {
            throw new InvalidOperationException($"No nodes in the graph implement {nameof(IAgentSettable<TAgent>)}.");
        }
    }
}