using System.Runtime.CompilerServices;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

/// <summary>
/// A builder class for constructing a finite state machine graph.
/// </summary>
public sealed partial class GraphBuilder
{
    private NodeId _next = NodeId.Start; // next id to hand out; Start is reserved for the first call with isStart=true
    private LogicNode? _startNode;

    private readonly Dictionary<NodeId, IAsyncLogic?> _nodes = new(); // non-start nodes only
    private readonly Dictionary<object, NodeId> _byLogic = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NodeId, Transition> _transitions = new();

    /// <summary>Add a node. If <paramref name="isStart"/> is true, this becomes the Start node (index 0).</summary>
    public NodeId AddNode(IAsyncLogic asyncLogic, bool isStart = false)
    {
        Guard.NotNull(asyncLogic, nameof(asyncLogic));

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

    /// <summary>Add a sync-only node. Wraps it in a <see cref="SyncLogicAdapter"/>.</summary>
    public NodeId AddNode(ILogic syncLogic, bool isStart = false)
    {
        Guard.NotNull(syncLogic, nameof(syncLogic));

        // Deduplicate by the original ILogic instance.
        if (_byLogic.TryGetValue(syncLogic, out NodeId existing))
        {
            return existing;
        }

        NodeId id = AddNode(new SyncLogicAdapter(syncLogic), isStart);
        _byLogic[syncLogic] = id; // also track by original logic for dedup
        return id;
    }

    /// <summary>
    ///  Builds the graph from the added nodes and transitions.
    /// </summary>
    /// <returns>A <see cref="Graph"/> instance of a graph with the defined nodes and transitions.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no start node has been added or if there are inconsistencies in the graph structure.</exception>
    private Graph InternalBuild()
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

        INode[] nodes = new INode[length];
        Transition[] edges = new Transition[length];

        for (int i = 0; i < edges.Length; i++)
        {
            edges[i] = Transition.Empty;
        }

        nodes[NodeId.Start.Index] =
            _startNode ?? throw new InvalidOperationException("No start node has been added to the graph.");

        // Place the rest (null-safe checks)
        foreach ((NodeId id, IAsyncLogic? logic) in _nodes)
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

            if (logic != null)
            {
                nodes[idx] = new LogicNode(id, logic);
            }
            else
            {
                throw new InvalidOperationException($"Node {id} has no logic assigned.");
            }
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
    /// Creates a new graph whose first (start) node runs <paramref name="startSyncLogic"/>.
    /// </summary>
    /// <param name="startSyncLogic">The synchronous logic that will be the starting point of the graph.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(ILogic startSyncLogic)
    {
        GraphBuilder builder = new();
        NodeId id = builder.AddNode(startSyncLogic, true);
        return new StateToken(id, builder);
    }

    /// <summary>
    /// Creates a new graph whose first (start) node executes <paramref name="run"/>.
    /// </summary>
    /// <param name="run">The function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return StartWith(new AsyncRelayState(run));
    }

    /// <summary>
    /// Creates a new graph whose first (start) node executes <paramref name="run"/> synchronously.
    /// </summary>
    /// <param name="run">The synchronous function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return StartWith(new RelayState(run));
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
    public void SetName(NodeId id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Node name cannot be null or whitespace.", nameof(name));
        }

        NodeId oldId = id;
        NodeId namedId = oldId.WithName(name);

        bool isStart = _startNode is not null && _startNode.Id.Index == oldId.Index;
        if (isStart)
        {
            if (_startNode != null)
            {
                _startNode = new LogicNode(namedId, _startNode.AsyncLogic);
            }
            else
            {
                throw new InvalidOperationException("Start node is not defined.");
            }
        }
        else if (_nodes.Remove(oldId, out IAsyncLogic? existingLogic))
        {
            // Normal path when callers pass in the exact key we stored
            _nodes[namedId] = existingLogic;
        }
        else
        {
            // Fallback: look up by index only (in case callers passed an id with the right
            // index but a different name than what we have as the key).
            NodeId? keyToRename = null;
            foreach (NodeId key in _nodes.Keys)
            {
                if (key.Index == oldId.Index)
                {
                    keyToRename = key;
                    break;
                }
            }

            if (keyToRename is null)
            {
                throw new InvalidOperationException($"Node with index {oldId.Index} does not exist in the graph.");
            }

            IAsyncLogic? logic = _nodes[keyToRename.Value];
            if (logic == null)
            {
                throw new InvalidOperationException($"Node with index {oldId.Index} does not exist in the graph.");
            }
            _nodes.Remove(keyToRename.Value);
            _nodes[namedId] = logic;
        }

        // Update logic->id map so future AddNode(sameLogic) returns the named id
        foreach (KeyValuePair<object, NodeId> pair in _byLogic.ToList())
        {
            if (pair.Value.Index == oldId.Index)
            {
                _byLogic[pair.Key] = namedId;
            }
        }

        // Update transitions: sources and destinations that reference this index
        foreach ((NodeId from, Transition t) in _transitions.ToList())
        {
            bool fromMatches = from.Index == oldId.Index;
            bool toMatches = t.Destination.Index == oldId.Index;

            NodeId newFrom = fromMatches ? namedId : from;
            Transition newTransition = toMatches ? new Transition(namedId) : t;

            if (fromMatches)
            {
                _transitions.Remove(from);
                _transitions[newFrom] = newTransition;
            }
            else if (toMatches)
            {
                _transitions[from] = newTransition;
            }
        }
    }

    public static void SetName(Graph graph, string name)
    {
        Guard.NotNull(graph, nameof(graph));
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