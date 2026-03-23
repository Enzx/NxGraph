using System.Runtime.CompilerServices;
using NxGraph.Fsm;
using NxGraph.Graphs;
#if NETSTANDARD2_1
using ArgumentNullException = System.ArgumentNullExceptionShim;
#endif

namespace NxGraph.Authoring;

/// <summary>
/// A builder class for constructing a finite state machine graph.
/// </summary>
public sealed partial class GraphBuilder
{
    private NodeId _next = NodeId.Start; // next id to hand out; Start is reserved for the first call with isStart=true
    private LogicNode? _startNode;

    private readonly Dictionary<NodeId, IAsyncLogic> _nodes = new(); // non-start nodes only
    private readonly Dictionary<IAsyncLogic, NodeId> _byLogic = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NodeId, Transition> _transitions = new();

    /// <summary>Add a node. If <paramref name="isStart"/> is true, this becomes the Start node (index 0).</summary>
    public NodeId AddNode(IAsyncLogic asyncLogic, bool isStart = false)
    {
        ArgumentNullException.ThrowIfNull(asyncLogic);

        if (isStart)
        {
            if (_startNode is not null)
            {
                throw new InvalidOperationException("A start node has already been added.");
            }

            _startNode = new LogicNode(NodeId.Start, asyncLogic);
            _byLogic[asyncLogic] = NodeId.Start; // so future AddNode(sameLogic) returns Start
            return NodeId.Start;
        }

        if (_startNode?.AsyncLogic == asyncLogic)
        {
            return NodeId.Start;
        }

        if (_byLogic.TryGetValue(asyncLogic, out NodeId existing))
        {
            return existing;
        }


        _next = _next.Next();
        _nodes[_next] = asyncLogic;
        _byLogic[asyncLogic] = _next;
        return _next;
    }

    /// <summary>Add a single outgoing transition from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public GraphBuilder AddTransition(NodeId from, NodeId to)
    {
        if (!_transitions.TryAdd(from, new Transition(to)))
        {
            throw new InvalidOperationException($"A transition from {from} is already defined.");
        }

        return this;
    }

    /// <summary>
    ///  Builds the graph from the added nodes and transitions.
    /// </summary>
    /// <returns>A <see cref="Graph"/> instance of a graph with the defined nodes and transitions.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no start node has been added or if there are inconsistencies in the graph structure.</exception>
    public Graph InternalBuild()
    {
        int maxIndex = 0;
        foreach (NodeId id in _nodes.Keys)
        {
            if (id.Index > maxIndex)
            {
                maxIndex = id.Index;
            }
        }

        int length = Math.Max(1, maxIndex + 1);

        LogicNode[] nodes = new LogicNode[length];
        Transition[] edges = new Transition[length];

        for (int i = 0; i < edges.Length; i++)
        {
            edges[i] = Transition.Empty;
        }

        nodes[NodeId.Start.Index] =
            _startNode ?? throw new InvalidOperationException("No start node has been added to the graph.");

        // Place the rest (null-safe checks)
        foreach ((NodeId id, IAsyncLogic logic) in _nodes)
        {
            int idx = id.Index;

            if ((uint)idx >= (uint)nodes.Length)
            {
                throw new InvalidOperationException($"Node {id} is out of bounds.");
            }

            if (nodes[idx] != null)
            {
                throw new InvalidOperationException($"Node slot {idx} already occupied (duplicate NodeId index).");
            }

            nodes[idx] = new LogicNode(id, logic);
        }

        //transitions > dense array
        foreach ((NodeId from, Transition t) in _transitions)
        {
            int idx = from.Index;
            if ((uint)idx >= (uint)edges.Length)
            {
                throw new InvalidOperationException($"Transition 'from' {from} is out of bounds.");
            }

            edges[idx] = t;
        }

        NodeId graphId = _next.Next();
        return new Graph(graphId, nodes, edges);
    }


    /// <summary>
    /// Creates a new graph whose first (start) node runs <paramref name="startAsyncLogic"/>.
    /// </summary>
    /// <param name="startAsyncLogic">The logic that will be the starting point of the graph.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(IAsyncLogic startAsyncLogic)
    {
        GraphBuilder builder = new();
        NodeId id = builder.AddNode(startAsyncLogic, true);
        return new StateToken(id, builder);
    }

    /// <summary>
    /// Creates a new graph whose first (start) node executes <paramref name="run"/>.
    /// </summary>
    /// <param name="run">The function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(Func<CancellationToken, ValueTask<Result>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return StartWith(new RelayState(run));
    }

    /// <summary>
    /// Creates a new graph whose first (start) node executes <paramref name="run"/> synchronously.
    /// </summary>
    /// <param name="run">The synchronous function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return StartWith(new SyncRelayState(run));
    }

    /// <summary>
    /// Begins building a new graph without adding a start node yet.
    /// Chain with <c>.If()</c>, <c>.Switch()</c>, <c>.WaitFor()</c>, or <c>.To()</c> to define the start.
    /// </summary>
    /// <returns>A <see cref="StartToken"/> that allows fluent configuration of the first state.</returns>
    public static StartToken Start()
    {
        return new StartToken(new GraphBuilder());
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
            _startNode = new LogicNode(_startNode.Id.WithName(name), _startNode.AsyncLogic);
            return;
        }

        if (_nodes.Remove(newId, out IAsyncLogic? node))
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

    public static void SetName(Graph graph, string name)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Graph name cannot be null or whitespace.", nameof(name));
        }

        graph.Id = graph.Id.WithName(name);
    }

    public IReadOnlyList<NodeId>? GetAllNodeIds()
    {
        if (_nodes.Count == 0 || _startNode == null)
        {
            return null;
        }

        List<NodeId> ids = new(_nodes.Keys.Count) { _startNode.Id };
        foreach (NodeId id in _nodes.Keys)
        {
            ids.Add(id);
        }

        return ids;
    }
}

/// <summary>
/// Reference equality comparer for INode keys so AddNode(same instance) dedupes, but identical delegates do not.
/// </summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    public new bool Equals(object? x, object? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(object obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}

public static class GraphBuilderExtensions
{
    /// <summary>
    /// Converts a Graph to an AsyncStateMachine.
    /// </summary>
    /// <param name="graph">The Graph to be converted.</param>
    /// <param name="observer">Optional observer for state changes.</param>
    /// <returns>An AsyncStateMachine instance.</returns>
    public static AsyncStateMachine ToAsyncStateMachine(this Graph graph, IAsyncStateMachineObserver? observer = null)
    {
        return new AsyncStateMachine(graph, observer);
    }

    /// <summary>
    ///  Converts a Graph to an AsyncStateMachine with a specific agent type.
    /// </summary>
    /// <param name="graph">The Graph to be converted.</param>
    /// <param name="observer">Optional observer for state changes.</param>
    /// <typeparam name="TAgent">The type of the agent to be used in the state machine.</typeparam>
    /// <returns>An AsyncStateMachine instance with the specified agent type.</returns>
    public static AsyncStateMachine<TAgent> ToAsyncStateMachine<TAgent>(this Graph graph,
        IAsyncStateMachineObserver? observer = null)
    {
        return new AsyncStateMachine<TAgent>(graph, observer);
    }

    public static AsyncStateMachine<TAgent> Add<TAgent>(this AsyncStateMachine<TAgent> sm, TAgent agent)
    {
        sm.SetAgent(agent);
        return sm;
    }

    /// <summary>
    /// Converts a Graph to a StateMachine.
    /// </summary>
    public static StateMachine ToStateMachine(this Graph graph, IStateMachineObserver? observer = null)
    {
        return new StateMachine(graph, observer);
    }

    /// <summary>
    /// Converts a Graph to a typed StateMachine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this Graph graph,
        IStateMachineObserver? observer = null)
    {
        return new StateMachine<TAgent>(graph, observer);
    }

    public static StateMachine<TAgent> Add<TAgent>(this StateMachine<TAgent> sm, TAgent agent)
    {
        sm.SetAgent(agent);
        return sm;
    }
}