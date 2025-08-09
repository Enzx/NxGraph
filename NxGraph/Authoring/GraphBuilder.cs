using System.Runtime.CompilerServices;
using NxGraph.Fsm;
using NxGraph.Graphs;

// ReSharper disable UnusedMethodReturnValue.Global

namespace NxGraph.Authoring;

/// <summary>
/// A builder class for constructing a finite state machine graph.
/// </summary>
public sealed class GraphBuilder
{
    private NodeId _next = NodeId.Start; // next id to hand out; Start is reserved for the first call with isStart=true
    private Node? _startNode;

    private readonly Dictionary<NodeId, INode> _nodes = new(); // non-start nodes only
    private readonly Dictionary<INode, NodeId> _byLogic = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NodeId, Transition> _transitions = new();

    /// <summary>Add a node. If <paramref name="isStart"/> is true, this becomes the Start node (index 0).</summary>
    public NodeId AddNode(INode logic, bool isStart = false)
    {
        ArgumentNullException.ThrowIfNull(logic);

        if (isStart)
        {
            if (_startNode is not null)
                throw new InvalidOperationException("A start node has already been added.");

            _startNode = new Node(NodeId.Start, logic);
            _byLogic[logic] = NodeId.Start; // so future AddNode(sameLogic) returns Start
            return NodeId.Start;
        }

        if (_startNode?.Logic == logic)
            return NodeId.Start;

        if (_byLogic.TryGetValue(logic, out NodeId existing))
            return existing;


        _next = _next.Next();
        _nodes[_next] = logic;
        _byLogic[logic] = _next;
        return _next;
    }

    /// <summary>Add a single outgoing transition from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public GraphBuilder AddTransition(NodeId from, NodeId to)
    {
        if (!_transitions.TryAdd(from, new Transition(to)))
            throw new InvalidOperationException($"A transition from {from} is already defined.");
        return this;
    }

    /// <summary>
    ///  Builds the graph from the added nodes and transitions.
    /// </summary>
    /// <returns>A <see cref="Graph"/> instance of a graph with the defined nodes and transitions.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no start node has been added or if there are inconsistencies in the graph structure.</exception>
    public Graph Build()
    {
        if (_startNode is null)
            throw new InvalidOperationException("No start node has been added to the graph.");

        int maxIndex = 0;
        foreach (NodeId id in _nodes.Keys)
            if (id.Index > maxIndex)
                maxIndex = id.Index;

        int length = Math.Max(1, maxIndex + 1);

        Node[] nodes = new Node[length];
        Transition[] edges = new Transition[length];

        for (int i = 0; i < edges.Length; i++)
            edges[i] = Transition.Empty;

        nodes[NodeId.Start.Index] = _startNode;

        // Place the rest (null-safe checks)
        foreach ((NodeId id, INode logic) in _nodes)
        {
            int idx = id.Index;

            if ((uint)idx >= (uint)nodes.Length)
                throw new InvalidOperationException($"Node {id} is out of bounds.");

            if (nodes[idx] != null)
                throw new InvalidOperationException($"Node slot {idx} already occupied (duplicate NodeId index).");

            nodes[idx] = new Node(id, logic);
        }

        //transitions > dense array
        foreach ((NodeId from, Transition t) in _transitions)
        {
            int idx = from.Index;
            if ((uint)idx >= (uint)edges.Length)
                throw new InvalidOperationException($"Transition 'from' {from} is out of bounds.");
            edges[idx] = t;
        }

        return new Graph(nodes[0], nodes, edges);
    }


    /// <summary>
    /// Creates a new state token that starts with the given node.
    /// </summary>
    /// <param name="startNode">The node that will be the starting point of the state token.</param>
    /// <returns>A new StateToken initialized with the specified start node.</returns>
    public static StateToken StartWith(INode startNode)
    {
        return FsmDsl.StartWith(startNode);
    }

    public static StateToken StartWith(Func<CancellationToken, ValueTask<Result>> run)
    {
        INode startNode = new RelayState(run);
        return FsmDsl.StartWith(startNode);
    }

    /// <summary>
    /// Creates a new state token that starts with the default start node.
    /// </summary>
    /// <returns>A new StateToken initialized with the default start node.</returns>
    public static StartToken Start()
    {
        return FsmDsl.Start();
    }

    /// <summary>
    /// Sets the name of a node in the graph.
    /// </summary>
    /// <param name="newId">The new ID of the node, which must already exist in the graph.</param>
    /// <param name="name">The new name to assign to the node.</param>
    /// <exception cref="ArgumentException">Thrown if the provided name is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the node with the specified ID does not exist in the graph.</exception>
    public void SetName(NodeId newId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Node name cannot be null or whitespace.", nameof(name));
        }

        if (_startNode != null && _startNode.Id == newId)
        {
            _startNode = new Node(_startNode.Id.WithName(name), _startNode.Logic);
            return;
        }

        if (_nodes.Remove(newId, out INode? node))
        {
            newId = newId.WithName(name);

            _nodes[newId] = node;
        }
        else
        {
            throw new InvalidOperationException($"Node with ID {newId} does not exist in the graph.");
        }

        // Update transitions to reflect the new node ID
        foreach (KeyValuePair<NodeId, Transition> transition in _transitions.ToList())
        {
            if (transition.Key == newId)
            {
                _transitions.Remove(transition.Key);
                _transitions.Add(newId, transition.Value);
            }
            else if (transition.Value.Destination == newId)
            {
                _transitions[transition.Key] = new Transition(newId);
            }
        }
    }
}

/// <summary>
/// Reference equality comparer for INode keys so AddNode(same instance) dedupes, but identical delegates do not.
/// </summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();
    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}

public static class GraphBuilderExtensions
{
    /// <summary>
    /// Converts a Graph to a StateMachine.
    /// </summary>
    /// <param name="graph">The Graph to be converted.</param>
    /// <param name="observer">Optional observer for state changes.</param>
    /// <returns>A StateMachine instance.</returns>
    public static StateMachine ToStateMachine(this Graph graph, IAsyncStateObserver? observer = null)
    {
        return new StateMachine(graph, observer);
    }

    /// <summary>
    ///  Converts a Graph to a StateMachine with a specific agent type.
    /// </summary>
    /// <param name="graph">The Graph to be converted.</param>
    /// <param name="observer">Optional observer for state changes.</param>
    /// <typeparam name="TAgent">The type of the agent to be used in the state machine.</typeparam>
    /// <returns>A StateMachine instance with the specified agent type.</returns>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this Graph graph, IAsyncStateObserver? observer = null)
    {
        return new StateMachine<TAgent>(graph, observer);
    }

    public static StateMachine<TAgent> Add<TAgent>(this StateMachine<TAgent> sm, TAgent agent)
    {
        sm.SetAgent(agent);
        return sm;
    }
}