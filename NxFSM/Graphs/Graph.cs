using NxFSM.Fsm;

namespace NxFSM.Graphs;

public sealed class Graph
{
    internal readonly Dictionary<NodeId, Node> Nodes = new();
    private readonly Dictionary<NodeId, Transition> _edges = new();
    public Node StartNode { get; private set; } =  Node.Empty;

    public void AddNode(Node node, bool isStart)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Nodes.TryAdd(node.Key, node))
            throw new InvalidOperationException($"Duplicate node key {node.Key}");
        if (isStart) StartNode = node;
    }

    public void AddEdge(NodeId from, NodeId to)
    {
        if (_edges.ContainsKey(from))
            throw new InvalidOperationException($"Node {from} already has a transition");
        _edges[from] = new Transition(to);
    }

    public Transition GetTransition(NodeId from)
        => _edges.TryGetValue(from, out Transition t) ? t : Transition.Empty;

    public void SetAgent<TAgent>(TAgent agent)
    {
        foreach (Node node in Nodes.Values)
            if (node.Logic is IAgentSettable<TAgent> settable)
                settable.SetAgent(agent);
    }
}