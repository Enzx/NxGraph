using NxGraph.Fsm;
using NxGraph.Graphs;

// ReSharper disable UnusedMethodReturnValue.Global

namespace NxGraph.Authoring;

/// <summary>
/// A builder class for constructing a finite state machine graph.
/// </summary>
public sealed class GraphBuilder
{
    private readonly Graph _graph = new();
    private NodeId _id = NodeId.Default;

    // ReSharper disable once NullableWarningSuppressionIsUsed
    private Node? _startNode;
    private readonly Dictionary<NodeId, INode> _nodes = new();
    private readonly Dictionary<INode, NodeId> _nodesToId = new();

    /// <summary>
    /// Adds a node to the graph with the given logic.
    /// </summary>
    /// <param name="logic">The logic to be executed by the node.</param>
    /// <param name="isStart">Indicates if this node is the starting node of the graph.</param>
    /// <returns>The NodeId of the newly added node.</returns>
    public NodeId AddNode(INode logic, bool isStart = false)
    {
        if (isStart)
        {
            _id = _id.Next();
            if (_startNode != null)
            {
                throw new InvalidOperationException("A start node has already been added.");
            }

            _startNode = new Node(_id, logic);
            return _id;
        }

        if (_startNode?.Logic == logic)
        {
            return _startNode.Key;
        }

        if (_nodesToId.TryGetValue(logic, out NodeId existingId))
        {
            return existingId; // Return the existing ID if the node already exists
        }

        _id = _id.Next();
        _nodes[_id] = logic;
        _nodesToId[logic] = _id;
        return _id;
    }

    /// <summary>
    ///  Adds a transition from one node to another.
    /// </summary>
    /// <param name="from">NodeId from</param>
    /// <param name="to">NodeId to</param>
    /// <returns>The current instance of GraphBuilder for fluent chaining.</returns>
    public GraphBuilder AddTransition(NodeId from, NodeId to)
    {
        _graph.AddEdge(from, to);
        return this;
    }

    /// <summary>
    /// Builds the graph and returns it.
    /// </summary>
    /// <returns>The constructed Graph object.</returns>
    public Graph Build()
    {
        if (_startNode == null)
        {
            throw new InvalidOperationException("No start node has been added to the graph.");
        }

        _graph.AddNode(_startNode, isStart: true);
        foreach (KeyValuePair<NodeId, INode> node in _nodes)
        {
            _graph.AddNode(new Node(node.Key, node.Value), false);
        }

        return _graph;
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