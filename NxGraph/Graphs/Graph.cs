using System.Runtime.CompilerServices;
using NxGraph.Fsm;

namespace NxGraph.Graphs;

public sealed class Graph : IGraph
{
    private readonly Node[] _nodes; // index = NodeId.Index
    private readonly Transition[] _edges; // index = from.Index
    public Node StartNode { get; } // convenience reference

    internal Graph(Node start, Node[] nodes, Transition[] edges)
    {
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

        StartNode = start;
        _nodes = nodes;
        _edges = edges;
    }

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

    public void SetAgent<TAgent>(TAgent agent)
    {
        if (agent is null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        for (int i = 0; i < _nodes.Length; i++)
        {
            INode logic = _nodes[i].Logic;
            if (logic is IAgentSettable<TAgent> settable)
            {
                settable.SetAgent(agent);
            }
        }
    }

    public int NodeCount => _nodes.Length;
}