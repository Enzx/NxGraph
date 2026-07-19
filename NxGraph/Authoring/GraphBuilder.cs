using System.Runtime.CompilerServices;
using NxGraph.Blackboards;
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
    private readonly Dictionary<int, string> _names = new(); // display names, applied at Build()
    private readonly List<KeyValuePair<NodeId, string>> _pendingGotos = new(); // back-edges resolved by name at Build()
    private readonly Dictionary<int, RetryPolicy> _retryPolicies = new(); // sparse; materialized at Build()
    private readonly Dictionary<int, Action> _enterActions = new(); // sparse; attached to LogicNodes at Build()
    private readonly Dictionary<int, Action> _exitActions = new(); // sparse; attached to LogicNodes at Build()
    private readonly Dictionary<int, int> _outcomeCodes = new(); // sparse; materialized at Build()
    private readonly Dictionary<int, string> _outcomeNames = new(); // code -> display name
    private readonly Dictionary<int, Guid> _uids = new(); // sparse; materialized at Build()
    private BlackboardSchema? _graphSchema; // Graph-scoped declaration, baked into the Graph at Build()
    private BlackboardSchema? _globalSchema; // required Global-scoped schema, baked into the Graph at Build()
    private BlackboardSchema? _nodeSchema; // transient Node-scoped schema, baked into the Graph at Build()

    /// <summary>
    /// Declares a blackboard schema on the graph, routed by the schema's scope: a
    /// Graph-scoped schema becomes the graph's own declaration, a Global-scoped schema
    /// records the global board the graph requires. Declaring the same scope twice throws —
    /// declaration is one-time authoring intent (runtime rebinding is a machine concern).
    /// </summary>
    public GraphBuilder WithSchema(BlackboardSchema schema)
    {
        Guard.NotNull(schema, nameof(schema));

        switch (schema.Scope)
        {
            case BlackboardScope.Global:
                if (_globalSchema is not null)
                {
                    throw new InvalidOperationException(
                        "A Global-scoped schema has already been declared on this graph.");
                }

                _globalSchema = schema;
                break;
            case BlackboardScope.Node:
                if (_nodeSchema is not null)
                {
                    throw new InvalidOperationException(
                        "A Node-scoped schema has already been declared on this graph.");
                }

                _nodeSchema = schema;
                break;
            default:
                if (_graphSchema is not null)
                {
                    throw new InvalidOperationException(
                        "A Graph-scoped schema has already been declared on this graph.");
                }

                _graphSchema = schema;
                break;
        }

        return this;
    }

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

    /// <summary>Add the single outgoing success transition from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public GraphBuilder AddTransition(NodeId from, NodeId to)
    {
        if (HasPendingGoto(from))
        {
            throw new InvalidOperationException($"A transition from {from} is already defined.");
        }

        AddSuccessEdge(from, to);
        return this;
    }

    /// <summary>
    /// Add the failure transition from <paramref name="from"/> to <paramref name="to"/>:
    /// when the node returns <c>Failure</c>, the machine routes there instead of terminating.
    /// </summary>
    public GraphBuilder AddFailureTransition(NodeId from, NodeId to)
    {
        if (to == NodeId.Default)
        {
            throw new ArgumentException("Failure transition target cannot be NodeId.Default.", nameof(to));
        }

        if (_transitions.TryGetValue(from, out Transition existing))
        {
            if (existing.HasFailureDestination)
            {
                throw new InvalidOperationException($"A failure transition from {from} is already defined.");
            }

            _transitions[from] = new Transition(existing.Destination, to);
        }
        else
        {
            _transitions.Add(from, new Transition(NodeId.Default, to));
        }

        return this;
    }

    private void AddSuccessEdge(NodeId from, NodeId to)
    {
        if (_transitions.TryGetValue(from, out Transition existing))
        {
            if (!existing.IsEmpty)
            {
                throw new InvalidOperationException($"A transition from {from} is already defined.");
            }

            _transitions[from] = new Transition(to, existing.FailureDestination);
        }
        else
        {
            _transitions.Add(from, new Transition(to));
        }
    }

    /// <summary>
    /// Adds a transition from <paramref name="from"/> to the node whose display name is
    /// <paramref name="targetName"/>. The name is resolved at <c>Build()</c>, so it may be
    /// assigned later in the chain. Unknown or ambiguous names fail the build.
    /// </summary>
    public GraphBuilder AddGoto(NodeId from, string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("Goto target name cannot be null or whitespace.", nameof(targetName));
        }

        if (HasPendingGoto(from) ||
            (_transitions.TryGetValue(from, out Transition existing) && !existing.IsEmpty))
        {
            throw new InvalidOperationException($"A transition from {from} is already defined.");
        }

        _pendingGotos.Add(new KeyValuePair<NodeId, string>(from, targetName));
        return this;
    }

    private bool HasPendingGoto(NodeId from)
    {
        foreach (KeyValuePair<NodeId, string> pending in _pendingGotos)
        {
            if (pending.Key.Index == from.Index)
            {
                return true;
            }
        }

        return false;
    }

    private void ResolvePendingGotos()
    {
        foreach ((NodeId from, string targetName) in _pendingGotos)
        {
            int targetIndex = -1;
            foreach ((int index, string name) in _names)
            {
                if (!string.Equals(name, targetName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (targetIndex >= 0)
                {
                    throw new InvalidOperationException(
                        $"Goto target name '{targetName}' is ambiguous: nodes #{targetIndex} and #{index} both use it.");
                }

                targetIndex = index;
            }

            if (targetIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Goto target '{targetName}' does not match any named node. " +
                    "Name the target with SetName(...) before Build().");
            }

            AddSuccessEdge(from, new NodeId(targetIndex));
        }

        _pendingGotos.Clear();
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
        ResolvePendingGotos();

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

        if (_startNode is null)
        {
            throw new InvalidOperationException("No start node has been added to the graph.");
        }

        nodes[NodeId.Start.Index] = CreateNode(_startNode.Id, _startNode.AsyncLogic);

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
                nodes[idx] = CreateNode(id, logic);
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

            edges[idx] = new Transition(ApplyName(t.Destination), ApplyName(t.FailureDestination));
        }

        LogicNode CreateNode(NodeId id, IAsyncLogic logic)
        {
            _enterActions.TryGetValue(id.Index, out Action? enter);
            _exitActions.TryGetValue(id.Index, out Action? exit);
            return new LogicNode(ApplyName(id), logic, enter, exit);
        }

        RetryPolicy[]? retries = null;
        if (_retryPolicies.Count > 0)
        {
            retries = new RetryPolicy[length];
            foreach ((int idx, RetryPolicy policy) in _retryPolicies)
            {
                if ((uint)idx >= (uint)retries.Length)
                {
                    throw new InvalidOperationException($"Retry policy node index {idx} is out of bounds.");
                }

                retries[idx] = policy;
            }
        }

        int[]? outcomes = null;
        if (_outcomeCodes.Count > 0)
        {
            outcomes = new int[length];
            foreach ((int idx, int code) in _outcomeCodes)
            {
                if ((uint)idx >= (uint)outcomes.Length)
                {
                    throw new InvalidOperationException($"Outcome code node index {idx} is out of bounds.");
                }

                outcomes[idx] = code;
            }
        }

        Guid[]? uids = null;
        if (_uids.Count > 0)
        {
            uids = new Guid[length];
            foreach ((int idx, Guid uid) in _uids)
            {
                if ((uint)idx >= (uint)uids.Length)
                {
                    throw new InvalidOperationException($"Uid node index {idx} is out of bounds.");
                }

                uids[idx] = uid;
            }
        }

        IReadOnlyDictionary<int, string>? outcomeNames =
            _outcomeNames.Count > 0 ? new Dictionary<int, string>(_outcomeNames) : null;

        NodeId graphId = _next.Next();
        return new Graph(graphId, nodes, edges, logic: null, retries, outcomes, outcomeNames,
            _graphSchema, _globalSchema, _nodeSchema, uids);
    }


    /// <summary>
    /// Creates a new graph whose first (start) node runs <paramref name="startAsyncLogic"/> asynchronously.
    /// </summary>
    /// <param name="startAsyncLogic">The async logic that will be the starting point of the graph.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWithAsync(IAsyncLogic startAsyncLogic)
    {
        GraphBuilder builder = new();
        NodeId id = builder.AddNode(startAsyncLogic, true);
        return new StateToken(id, builder);
    }

    /// <summary>
    /// Creates a new graph whose first (start) node runs <paramref name="startSyncLogic"/> synchronously.
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
    /// Creates a new graph whose first (start) node executes <paramref name="run"/> asynchronously.
    /// </summary>
    /// <param name="run">The async function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWithAsync(Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return StartWithAsync(new AsyncRelayState(run));
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
    /// Creates a new graph whose first (start) node executes <paramref name="run"/>
    /// asynchronously, receiving the machine-bound routed blackboard context.
    /// </summary>
    /// <param name="run">The async function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWithAsync(Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return StartWithAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Creates a new graph whose first (start) node executes <paramref name="run"/>
    /// synchronously, receiving the machine-bound routed blackboard context.
    /// </summary>
    /// <param name="run">The synchronous function to execute in the start state.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the start node.</returns>
    public static StateToken StartWith(Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return StartWith(new RelayState(run));
    }

    /// <summary>
    /// Begins building a new graph without adding a start node yet.
    /// Chain with <c>.If()</c>, <c>.Switch()</c>, <c>.WaitForAsync()</c>, or <c>.To()</c> to define the start.
    /// </summary>
    /// <returns>A <see cref="StartToken"/> that allows fluent configuration of the first state.</returns>
    public static StartToken Start()
    {
        return new StartToken(new GraphBuilder());
    }

    /// <summary>
    /// Sets the display name of a node. Names are presentation data only — node identity
    /// is the index — and are applied to the emitted <see cref="NodeId"/>s at <c>Build()</c>.
    /// </summary>
    public void SetName(NodeId id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Node name cannot be null or whitespace.", nameof(name));
        }

        bool isStart = _startNode is not null && _startNode.Id.Index == id.Index;
        if (!isStart && !_nodes.ContainsKey(id))
        {
            throw new InvalidOperationException($"Node with index {id.Index} does not exist in the graph.");
        }

        _names[id.Index] = name;
    }

    private NodeId ApplyName(NodeId id)
        => _names.TryGetValue(id.Index, out string? name) ? id.WithName(name) : id;

    /// <summary>
    /// Attaches an action fired when the machine enters the node (once per visit,
    /// before its first execution).
    /// </summary>
    public GraphBuilder SetEnterAction(NodeId id, Action action)
    {
        Guard.NotNull(action, nameof(action));
        RequireExistingNode(id);
        _enterActions[id.Index] = action;
        return this;
    }

    /// <summary>
    /// Attaches an action fired when the machine leaves the node (once per visit,
    /// after its final execution, regardless of outcome).
    /// </summary>
    public GraphBuilder SetExitAction(NodeId id, Action action)
    {
        Guard.NotNull(action, nameof(action));
        RequireExistingNode(id);
        _exitActions[id.Index] = action;
        return this;
    }

    /// <summary>
    /// Declares the terminal outcome reported when a run ends at this node
    /// (e.g. "Approved" vs "Rejected"), with an optional display name for the code.
    /// </summary>
    public GraphBuilder SetOutcome(NodeId id, int code, string? name = null)
    {
        RequireExistingNode(id);
        _outcomeCodes[id.Index] = code;
        if (name is not null)
        {
            _outcomeNames[code] = name;
        }

        return this;
    }

    /// <summary>
    /// Assigns a stable UID to a node. UIDs are identity metadata for external tooling
    /// (editors persisting layouts, breakpoints, references across rebuilds) — runtime
    /// identity stays the index. <see cref="Guid.Empty"/> is reserved for "no uid";
    /// duplicate UIDs across nodes are a validator Error. Uniqueness scope is per-graph;
    /// nested subgraphs may reuse uids. Repeat calls for the same node overwrite.
    /// </summary>
    public GraphBuilder SetUid(NodeId id, Guid uid)
    {
        if (uid == Guid.Empty)
        {
            throw new ArgumentException("Node UID cannot be Guid.Empty (reserved for 'no uid').", nameof(uid));
        }

        RequireExistingNode(id);
        _uids[id.Index] = uid;
        return this;
    }

    private void RequireExistingNode(NodeId id)
    {
        bool isStart = _startNode is not null && _startNode.Id.Index == id.Index;
        if (!isStart && !_nodes.ContainsKey(id))
        {
            throw new InvalidOperationException($"Node with index {id.Index} does not exist in the graph.");
        }
    }

    /// <summary>
    /// Declares a retry policy for a node: on <c>Failure</c> the node is re-run in place
    /// until it succeeds or the policy's attempts are exhausted.
    /// </summary>
    public GraphBuilder SetRetryPolicy(NodeId id, RetryPolicy policy)
    {
        if (policy.MaxAttempts == 0)
        {
            throw new ArgumentException("Retry policy must allow at least one attempt.", nameof(policy));
        }

        bool isStart = _startNode is not null && _startNode.Id.Index == id.Index;
        if (!isStart && !_nodes.ContainsKey(id))
        {
            throw new InvalidOperationException($"Node with index {id.Index} does not exist in the graph.");
        }

        _retryPolicies[id.Index] = policy;
        return this;
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

    /// <summary>
    /// Wraps an already-added node in a <see cref="StateToken"/> so detached chains
    /// (e.g. failure handlers) can be built fluently with the same builder.
    /// </summary>
    public StateToken TokenFor(NodeId id)
    {
        bool isStart = _startNode is not null && _startNode.Id.Index == id.Index;
        if (!isStart && !_nodes.ContainsKey(id))
        {
            throw new InvalidOperationException($"Node with index {id.Index} does not exist in the graph.");
        }

        return new StateToken(id, this);
    }

    /// <summary>
    /// Returns every node id known to the builder — the start node plus all added nodes —
    /// with display names applied. A single-node graph returns its one-element list;
    /// <c>null</c> only when no start node has been added yet.
    /// </summary>
    public IReadOnlyList<NodeId>? GetAllNodeIds()
    {
        if (_startNode == null)
        {
            return null;
        }

        List<NodeId> ids = new(_nodes.Count + 1) { ApplyName(_startNode.Id) };
        foreach (NodeId id in _nodes.Keys)
        {
            ids.Add(ApplyName(id));
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