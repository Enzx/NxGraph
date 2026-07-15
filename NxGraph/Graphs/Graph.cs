using System.Runtime.CompilerServices;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Graphs;

/// <summary>
/// Represents a directed graph structure used in finite state machines (FSMs) or other graph-based systems.
/// </summary>
public sealed class Graph : INode, IGraph
{
    private readonly INode[] _nodes; // index = NodeId.Index
    private readonly Transition[] _edges; // index = from.Index
    private readonly RetryPolicy[]? _retryPolicies; // index = NodeId.Index; null when no node has a policy
    private readonly int[]? _outcomeCodes; // index = NodeId.Index; null when no node declares an outcome

    /// <summary>
    /// The unique identifier for this graph. 
    /// </summary>
    /// <remarks>
    /// This ID is immutable after construction and may differ from its node IDs if this graph is a subgraph of another.
    /// </remarks>
    public NodeId Id { get; internal set; }

    /// <summary>
    ///  The logic associated with the graph.
    /// </summary>
    public IAsyncLogic AsyncLogic { get; }

    /// <summary>
    /// Synchronous logic is not applicable for a Graph node; always <c>null</c>.
    /// </summary>
    public ILogic? Logic => null;

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
    /// <param name="logic">The logic associated with the graph. If null, an empty logic is used.</param>
    /// <exception cref="ArgumentException">Thrown when the nodes or edges arrays are empty, have unequal lengths, or the first node is not the start node.</exception>
    public Graph(NodeId id, INode[] nodes, Transition[] edges, IAsyncLogic? logic = null,
        RetryPolicy[]? retryPolicies = null, int[]? outcomeCodes = null,
        IReadOnlyDictionary<int, string>? outcomeNames = null,
        BlackboardSchema? schema = null, BlackboardSchema? globalSchema = null,
        BlackboardSchema? nodeSchema = null)
    {
        if (schema is not null && schema.Scope != BlackboardScope.Graph)
        {
            throw new ArgumentException("The graph-owned schema must be Graph-scoped.", nameof(schema));
        }

        if (globalSchema is not null && globalSchema.Scope != BlackboardScope.Global)
        {
            throw new ArgumentException("The required global schema must be Global-scoped.", nameof(globalSchema));
        }

        if (nodeSchema is not null && nodeSchema.Scope != BlackboardScope.Node)
        {
            throw new ArgumentException("The transient node schema must be Node-scoped.", nameof(nodeSchema));
        }

        Guard.NotNull(nodes, nameof(nodes));
        Guard.NotNull(edges, nameof(edges));

        // Length checks first: an empty nodes array must produce the documented
        // ArgumentException, not an IndexOutOfRangeException from nodes[0] below.
        if (nodes.Length == 0 || edges.Length == 0 || nodes.Length != edges.Length)
        {
            throw new ArgumentException("Nodes/edges arrays must be non-empty and have equal length.");
        }

        if (nodes[0] is null || nodes[0].Id != NodeId.Start)
        {
            throw new ArgumentException("Start node (nodes[0]) Id must be NodeId.Start (index 0).", nameof(nodes));
        }

        if (retryPolicies is not null && retryPolicies.Length != nodes.Length)
        {
            throw new ArgumentException("Retry policy array must match the nodes array length.",
                nameof(retryPolicies));
        }

        if (outcomeCodes is not null && outcomeCodes.Length != nodes.Length)
        {
            throw new ArgumentException("Outcome code array must match the nodes array length.",
                nameof(outcomeCodes));
        }

        Id = id;
        StartNode = nodes[0];
        _nodes = nodes;
        _edges = edges;
        _retryPolicies = retryPolicies;
        _outcomeCodes = outcomeCodes;
        OutcomeNames = outcomeNames;
        Schema = schema;
        GlobalSchema = globalSchema;
        NodeSchema = nodeSchema;
        AsyncLogic = logic ?? new EmptyAsyncLogic();
    }

    /// <summary>
    /// The Graph-scoped <see cref="BlackboardSchema"/> declared at authoring time via
    /// <c>WithSchema(...)</c>, or <c>null</c> when the graph declares none. Declarations are
    /// opt-in validation for machine binding; a graph round-tripped through serialization
    /// has no declarations and binds permissively.
    /// </summary>
    public BlackboardSchema? Schema { get; }

    /// <summary>
    /// The Global-scoped schema this graph requires, or <c>null</c> when it declares none.
    /// </summary>
    public BlackboardSchema? GlobalSchema { get; }

    /// <summary>
    /// The Node-scoped (transient per-visit) schema declared via <c>WithSchema(...)</c>, or
    /// <c>null</c> when the graph declares none. Machines auto-create their own board from
    /// this schema — Node boards are never user-bound, shared, or serialized.
    /// </summary>
    public BlackboardSchema? NodeSchema { get; }

    /// <summary>
    /// The per-node retry policies indexed by <see cref="NodeId.Index"/>, or <c>null</c>
    /// when no node declares one. Exposed for the runtimes and serializers.
    /// </summary>
    public RetryPolicy[]? RetryPolicies => _retryPolicies;

    /// <summary>
    /// Per-node terminal outcome codes indexed by <see cref="NodeId.Index"/>, or <c>null</c>
    /// when no node declares one. When a run terminates at a node, the machine reports the
    /// node's code (default 0) as <c>LastOutcome</c>.
    /// </summary>
    public int[]? OutcomeCodes => _outcomeCodes;

    /// <summary>
    /// Optional display names for outcome codes, resolved lazily for reporting —
    /// never on the run loop.
    /// </summary>
    public IReadOnlyDictionary<int, string>? OutcomeNames { get; }

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

        node = _nodes[id.Index];
        return true;
    }

    /// <summary>
    /// Attempts to retrieve a node by its index.
    /// </summary>
    /// <param name="index">The index of the node to retrieve.</param>
    /// <param name="node">The node corresponding to the given index, if found; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the node exists; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNodeByIndex(int index, out INode? node)
    {
        if ((uint)index >= (uint)_nodes.Length)
        {
            node = null;
            return false;
        }

        node = _nodes[index];
        return true;
    }


    /// <summary>
    /// Sets the agent for all nodes in the graph that implement <see cref="IAgentSettable{TAgent}"/>.
    /// </summary>
    /// <param name="agent">The agent to set for the nodes.</param>
    /// <typeparam name="TAgent">The type of the agent to set.</typeparam>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="agent"/> is null.</exception>
    public void SetAgent<TAgent>(TAgent agent)
    {
        Guard.NotNull(agent, nameof(agent));

        if (!SetAgentRecursive(agent))
        {
            throw new InvalidOperationException($"No nodes in the graph implement {nameof(IAgentSettable<TAgent>)}.");
        }
    }

    /// <summary>
    /// Cap on composite nesting for the agent/blackboard stamping walks. Indirect graph
    /// cycles (A nests a machine over B, B nests one over A) are constructible with public
    /// APIs; without a cap the walk would recurse to a process-killing StackOverflowException.
    /// Depth-capping instead of a visited set keeps run-start stamping allocation-free.
    /// </summary>
    private const int MaxStampingDepth = 64;

    private bool SetAgentRecursive<TAgent>(TAgent agent) => SetAgentRecursive(agent, 0);

    private bool SetAgentRecursive<TAgent>(TAgent agent, int depth)
    {
        if (depth > MaxStampingDepth)
        {
            throw new InvalidOperationException(
                $"Composite nesting exceeds {MaxStampingDepth} levels while stamping the agent — " +
                "check for a cycle between graphs nested as composites.");
        }

        bool found = false;
        for (int i = 0; i < _nodes.Length; i++)
        {
            if (_nodes[i] is not LogicNode logicNode) continue;

            // Check the async logic first, then the sync logic (for States wrapped in SyncLogicAdapter).
            IAgentSettable<TAgent>? settable =
                logicNode.AsyncLogic as IAgentSettable<TAgent>
                ?? logicNode.Logic as IAgentSettable<TAgent>;

            if (settable is not null)
            {
                settable.SetAgent(agent);
                found = true;
                // Generic StateMachine<TAgent>/AsyncStateMachine<TAgent> already re-walk
                // their inner graph via SetAgent — no additional recursion needed.
                continue;
            }

            // Composite logic (nested machines, history/parallel composites, user-defined
            // containers) surfaces its children via ISubGraphProvider — walk them without
            // knowing the concrete types, so new composites need no changes here.
            ISubGraphProvider? provider =
                logicNode.AsyncLogic as ISubGraphProvider
                ?? logicNode.Logic as ISubGraphProvider;
            if (provider is null)
            {
                continue;
            }

            foreach (Graph nested in provider.SubGraphs)
            {
                if (!ReferenceEquals(nested, this) && nested.SetAgentRecursive(agent, depth + 1))
                {
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Stamps the routed blackboard context onto every node that implements
    /// <see cref="IBlackboardSettable"/>, recursing into composites via
    /// <see cref="ISubGraphProvider"/>. Unlike <see cref="SetAgent{TAgent}"/> this never
    /// throws when nothing accepts: the state base classes accept automatically, so zero
    /// acceptors carries no bug signal — the lint lives in the graph validator instead.
    /// </summary>
    public void SetBlackboards(in BlackboardContext context)
    {
        SetBlackboardsRecursive(in context);
    }

    private void SetBlackboardsRecursive(in BlackboardContext context) => SetBlackboardsRecursive(in context, 0);

    private void SetBlackboardsRecursive(in BlackboardContext context, int depth)
    {
        if (depth > MaxStampingDepth)
        {
            throw new InvalidOperationException(
                $"Composite nesting exceeds {MaxStampingDepth} levels while stamping blackboards — " +
                "check for a cycle between graphs nested as composites.");
        }

        for (int i = 0; i < _nodes.Length; i++)
        {
            if (_nodes[i] is not LogicNode logicNode) continue;

            // Check the async logic first, then the sync logic (for States wrapped in SyncLogicAdapter).
            IBlackboardSettable? settable =
                logicNode.AsyncLogic as IBlackboardSettable
                ?? logicNode.Logic as IBlackboardSettable;

            if (settable is not null)
            {
                settable.SetBlackboards(in context);
                // Nested machines validate and re-walk their inner graph via their own
                // SetBlackboards — no additional recursion needed.
                continue;
            }

            ISubGraphProvider? provider =
                logicNode.AsyncLogic as ISubGraphProvider
                ?? logicNode.Logic as ISubGraphProvider;
            if (provider is null)
            {
                continue;
            }

            foreach (Graph nested in provider.SubGraphs)
            {
                if (!ReferenceEquals(nested, this))
                {
                    nested.SetBlackboardsRecursive(in context, depth + 1);
                }
            }
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