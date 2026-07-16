using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Authoring;

/// <summary>
/// Seed handed to each branch lambda of <see cref="TokenDsl.ForkTo(StateToken, Func{ForkBranch, StateToken}[])"/>.
/// The first <c>To</c>/<c>ToAsync</c> call creates the branch's head node (the fork target);
/// it returns an ordinary <see cref="StateToken"/> for continued chaining.
/// </summary>
public sealed class ForkBranch
{
    private readonly GraphBuilder _builder;
    private NodeId _head = NodeId.Default;
    private bool _hasHead;

    internal ForkBranch(GraphBuilder builder)
    {
        _builder = builder;
    }

    internal NodeId Head => _hasHead
        ? _head
        : throw new InvalidOperationException(
            "A fork branch must add at least one state — call To(...)/ToAsync(...) inside the branch lambda.");

    private StateToken Record(NodeId head)
    {
        if (_hasHead)
        {
            throw new InvalidOperationException(
                "A fork branch declares its head once — continue the chain on the returned StateToken.");
        }

        _head = head;
        _hasHead = true;
        return new StateToken(head, _builder);
    }

    /// <summary>Creates the branch head from sync logic. Routing several branches to the same instance (e.g. one shared <see cref="JoinState"/>) converges them on one node.</summary>
    public StateToken To(ILogic syncLogic)
    {
        Guard.NotNull(syncLogic, nameof(syncLogic));
        return Record(_builder.AddNode(syncLogic));
    }

    /// <summary>Creates the branch head from async logic.</summary>
    public StateToken ToAsync(IAsyncLogic asyncLogic)
    {
        Guard.NotNull(asyncLogic, nameof(asyncLogic));
        return Record(_builder.AddNode(asyncLogic));
    }

    /// <summary>Creates the branch head from a sync function.</summary>
    public StateToken To(Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return To(new RelayState(run));
    }

    /// <summary>Creates the branch head from an async function.</summary>
    public StateToken ToAsync(Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return ToAsync(new AsyncRelayState(run));
    }

    /// <summary>Creates the branch head from a sync function receiving the machine-bound routed blackboard context.</summary>
    public StateToken To(Func<Blackboards.BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return To(new RelayState(run));
    }

    /// <summary>Creates the branch head from an async function receiving the machine-bound routed blackboard context.</summary>
    public StateToken ToAsync(Func<Blackboards.BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return ToAsync(new AsyncRelayState(run));
    }

    /// <summary>Points the branch at an already-added node (e.g. a shared handler or join).</summary>
    public StateToken To(StateToken existing)
    {
        return Record(existing.Id);
    }
}

/// <summary>
/// Returned by <c>ForkTo(...)</c>. The fork's chain is closed — branches continue inside
/// their lambdas — so the only remaining operations are graph-level.
/// </summary>
public readonly struct ForkToken
{
    internal ForkToken(NodeId id, GraphBuilder builder)
    {
        Id = id;
        Builder = builder;
    }

    public GraphBuilder Builder { get; }

    /// <summary>The fork node's id (useful with <see cref="GraphBuilder.TokenFor"/>-style helpers).</summary>
    public NodeId Id { get; }

    /// <summary>Sets the fork node's display name.</summary>
    public ForkToken SetName(string name)
    {
        Builder.SetName(Id, name);
        return this;
    }

    /// <summary>Finishes the DSL and produces an immutable <see cref="Graph"/>.</summary>
    public Graph Build()
    {
        return Builder.Build();
    }
}

/// <summary>
/// Authoring surface for the token runtime (spec 007): fork fan-out and graph→machine
/// factories. Join convergence needs no dedicated DSL — construct one <see cref="JoinState"/>
/// and route any number of chains to it with <c>.To(join)</c> (nodes dedupe by logic
/// reference).
/// </summary>
public static class TokenDsl
{
    private static ForkToken BuildFork(GraphBuilder builder, NodeId? from, bool isStart,
        Func<ForkBranch, StateToken>[] branches)
    {
        Guard.NotNull(branches, nameof(branches));
        if (branches.Length < 1)
        {
            throw new ArgumentException("A fork needs at least one branch.", nameof(branches));
        }

        // Branch heads are created first so the fork can be constructed immutably.
        NodeId[] heads = new NodeId[branches.Length];
        for (int i = 0; i < branches.Length; i++)
        {
            Guard.NotNull(branches[i], nameof(branches));
            ForkBranch seed = new(builder);
            branches[i](seed);
            heads[i] = seed.Head;
        }

        ForkState fork = new(heads);
        NodeId forkId = builder.AddNode((IAsyncLogic)fork, isStart);
        if (from is { } fromId)
        {
            builder.AddTransition(fromId, forkId);
        }

        return new ForkToken(forkId, builder);
    }

    /// <summary>
    /// Wires this state to a <see cref="ForkState"/>: when a token completes this state, it
    /// continues into the first branch and one new token spawns per remaining branch. Each
    /// lambda builds one branch chain; converge branches by routing them to the same
    /// <see cref="JoinState"/> instance. Token machines only — the FSM runtimes throw on
    /// fork nodes.
    /// </summary>
    public static ForkToken ForkTo(this StateToken prev, params Func<ForkBranch, StateToken>[] branches)
    {
        return BuildFork(prev.Builder, prev.Id, isStart: false, branches);
    }

    /// <summary>Starts the graph with a fork: the root token fans out immediately at run start.</summary>
    public static ForkToken ForkTo(this StartToken root, params Func<ForkBranch, StateToken>[] branches)
    {
        return BuildFork(root.Builder, from: null, isStart: true, branches);
    }

    // ── Graph → machine factories (mirroring To{Async,}StateMachine) ────

    /// <summary>Creates a sync <see cref="TokenMachine"/> over the graph.</summary>
    public static TokenMachine ToTokenMachine(this Graph graph, ITokenMachineObserver? observer = null,
        int maxTokens = TokenMachine.DefaultMaxTokens)
    {
        return new TokenMachine(graph, observer, maxTokens);
    }

    /// <summary>Creates a typed sync <see cref="TokenMachine{TAgent}"/> over the graph.</summary>
    public static TokenMachine<TAgent> ToTokenMachine<TAgent>(this Graph graph,
        ITokenMachineObserver? observer = null, int maxTokens = TokenMachine.DefaultMaxTokens)
    {
        return new TokenMachine<TAgent>(graph, observer, maxTokens);
    }

    /// <summary>Creates an <see cref="AsyncTokenMachine"/> over the graph.</summary>
    public static AsyncTokenMachine ToAsyncTokenMachine(this Graph graph,
        IAsyncTokenMachineObserver? observer = null, int maxTokens = TokenMachine.DefaultMaxTokens)
    {
        return new AsyncTokenMachine(graph, observer, maxTokens);
    }

    /// <summary>Creates a typed <see cref="AsyncTokenMachine{TAgent}"/> over the graph.</summary>
    public static AsyncTokenMachine<TAgent> ToAsyncTokenMachine<TAgent>(this Graph graph,
        IAsyncTokenMachineObserver? observer = null, int maxTokens = TokenMachine.DefaultMaxTokens)
    {
        return new AsyncTokenMachine<TAgent>(graph, observer, maxTokens);
    }
}
