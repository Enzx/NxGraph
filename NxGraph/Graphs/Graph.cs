using System.Runtime.CompilerServices;
using NxGraph.Fsm;

namespace NxGraph.Graphs;

/// <summary>
/// Represents a directed graph structure used in finite state machines (FSMs) or other graph-based systems.
/// </summary>
public sealed class Graph : INode, IGraph
{
    private readonly INode[] _nodes; // index = NodeId.Index
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
    public INode StartNode { get; }

    /// <summary>
    /// The number of nodes in the graph, including the start node.
    /// </summary>
    public int NodeCount => _nodes.Length;

    public int TransitionCount => _edges.Length;


    /// <summary>
    /// Initializes a new instance of the <see cref="Graph"/> class with the specified nodes and edges.
    /// </summary>
    /// <param name="id">The unique identifier for the graph.</param>
    /// <param name="nodes">The array of nodes in the graph. Must be non-empty and start with the start node at index 0.</param>
    /// <param name="edges">The array of transitions (edges) in the graph. Must be non-empty and have the same length as the nodes array.</param>
    /// <exception cref="ArgumentException">Thrown when the nodes or edges arrays are empty, have unequal lengths, or the first node is not the start node.</exception>
    public Graph(NodeId id, INode[] nodes, Transition[] edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        if (nodes[0].Id != NodeId.Start)
        {
            throw new ArgumentException("Start node (nodes[0]) Id must be NodeId.Start (index 0).", nameof(nodes));
        }

        if (nodes.Length == 0 || edges.Length == 0 || nodes.Length != edges.Length)
        {
            throw new ArgumentException("Nodes/edges arrays must be non-empty and have equal length.");
        }

        Id = id;
        StartNode = nodes[0];
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
    /// <param name="node">The node corresponding to the given ID, if found; otherwise, <see cref="LogicNode.Empty"/>.</param>
    /// <returns><c>true</c> if the node exists; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNode(NodeId id, out INode node)
    {
        if ((uint)id.Index >= (uint)_nodes.Length)
        {
            node = LogicNode.Empty;
            return false;
        }

        INode candidate = _nodes[id.Index];
        if (candidate.Id == id)
        {
            node = candidate;
            return true;
        }

        node = LogicNode.Empty;
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
            if (_nodes[i] is not LogicNode logicNode) continue;
            ILogic logic = logicNode.Logic;
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

    public INode GetNodeByIndex(int index)
    {
        if (index < 0 || index >= _nodes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index),
                "Index must be within the range of the graph's nodes.");
        }

        return _nodes[index];
    }

    public Transition GetTransitionByIndex(int index)
    {
        if (index < 0 || index >= _edges.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index),
                "Index must be within the range of the graph's transitions.");
        }

        return _edges[index];
    }
}