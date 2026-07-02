using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into a new state token with an async relay state.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="run">A function that defines the async logic to be executed in the relay state.</param>
    /// <returns>A new state token that includes the relay state logic.</returns>
    public static StateToken ToAsync(this StateToken prev, Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        IAsyncLogic asyncLogic = new AsyncRelayState(run);
        return prev.ToAsync(asyncLogic);
    }

    /// <summary>
    /// Adds a child graph as a composite state and wires a transition to it. The child runs
    /// to completion within the parent's step. With <paramref name="history"/> enabled, a
    /// failed child resumes at its last-active node when the parent re-enters the composite
    /// (see <see cref="AsyncHistoryState"/>); without it, re-entry restarts the child.
    /// </summary>
    public static StateToken SubGraph(this StateToken prev, Graph child, bool history = false)
    {
        Guard.NotNull(child, nameof(child));
        return prev.ToAsync(history ? new AsyncHistoryState(child) : new AsyncStateMachine(child));
    }

    /// <summary>
    /// Starts the graph with a child graph as its first (composite) state.
    /// </summary>
    public static StateToken SubGraph(this StartToken root, Graph child, bool history = false)
    {
        Guard.NotNull(child, nameof(child));
        IAsyncLogic composite = history ? new AsyncHistoryState(child) : new AsyncStateMachine(child);
        NodeId id = root.Builder.AddNode(composite, true);
        return new StateToken(id, root.Builder);
    }

    /// <summary>
    /// Adds an orthogonal-regions composite and wires a transition to it: every region graph
    /// progresses one node per round (cooperative interleaving) until all reach a terminal
    /// result. Succeeds only when every region succeeded (see <see cref="AsyncParallelState"/>).
    /// </summary>
    public static StateToken Parallel(this StateToken prev, params Graph[] regions)
    {
        return prev.ToAsync(new AsyncParallelState(regions));
    }

    /// <summary>
    /// Starts the graph with an orthogonal-regions composite as its first state.
    /// </summary>
    public static StateToken Parallel(this StartToken root, params Graph[] regions)
    {
        NodeId id = root.Builder.AddNode(new AsyncParallelState(regions), true);
        return new StateToken(id, root.Builder);
    }

    /// <summary>
    /// Adds a <b>sync</b> orthogonal-regions composite and wires a transition to it — the
    /// runtime-parity twin of the async overload (see <see cref="ParallelState"/>).
    /// <paramref name="mode"/> decides whether the join completes within one tick
    /// (<see cref="ParallelStepMode.RunToJoin"/>) or spreads one round per tick across frames
    /// (<see cref="ParallelStepMode.RoundPerTick"/>, sync runtime only).
    /// </summary>
    public static StateToken Parallel(this StateToken prev, ParallelStepMode mode, params Graph[] regions)
    {
        return prev.To(new ParallelState(mode, regions));
    }

    /// <summary>
    /// Starts the graph with a sync orthogonal-regions composite as its first state
    /// (see <see cref="ParallelState"/>).
    /// </summary>
    public static StateToken Parallel(this StartToken root, ParallelStepMode mode, params Graph[] regions)
    {
        return root.To(new ParallelState(mode, regions));
    }

    /// <summary>
    /// Adds a <b>dynamic</b> orthogonal-regions composite: at entry <paramref name="selector"/>
    /// reads the machine-bound blackboard context and returns the <see cref="RegionMask"/> of
    /// regions to run this execution (see <see cref="AsyncDynamicParallelState"/>). An empty
    /// mask succeeds immediately as a vacuous join.
    /// </summary>
    public static StateToken Parallel(this StateToken prev, Func<BlackboardContext, RegionMask> selector,
        params Graph[] regions)
    {
        return prev.ToAsync(new AsyncDynamicParallelState(selector, regions));
    }

    /// <summary>
    /// Starts the graph with a dynamic orthogonal-regions composite as its first state
    /// (see <see cref="AsyncDynamicParallelState"/>).
    /// </summary>
    public static StateToken Parallel(this StartToken root, Func<BlackboardContext, RegionMask> selector,
        params Graph[] regions)
    {
        return root.ToAsync(new AsyncDynamicParallelState(selector, regions));
    }

    /// <summary>
    /// Adds a <b>sync</b> dynamic orthogonal-regions composite — the runtime-parity twin of the
    /// selector overload (see <see cref="DynamicParallelState"/> and <see cref="ParallelStepMode"/>).
    /// </summary>
    public static StateToken Parallel(this StateToken prev, ParallelStepMode mode,
        Func<BlackboardContext, RegionMask> selector, params Graph[] regions)
    {
        return prev.To(new DynamicParallelState(mode, selector, regions));
    }

    /// <summary>
    /// Starts the graph with a sync dynamic orthogonal-regions composite as its first state
    /// (see <see cref="DynamicParallelState"/>).
    /// </summary>
    public static StateToken Parallel(this StartToken root, ParallelStepMode mode,
        Func<BlackboardContext, RegionMask> selector, params Graph[] regions)
    {
        return root.To(new DynamicParallelState(mode, selector, regions));
    }

    /// <summary>
    /// Routes this state's <c>Failure</c> outcome to a new sync handler state defined by a lambda.
    /// </summary>
    public static StateToken OnError(this StateToken prev, Func<Result> handler)
    {
        Guard.NotNull(handler, nameof(handler));
        return prev.OnError(new RelayState(handler));
    }

    /// <summary>
    /// Routes this state's <c>Failure</c> outcome to a new async handler state defined by a lambda.
    /// </summary>
    public static StateToken OnErrorAsync(this StateToken prev, Func<CancellationToken, ValueTask<Result>> handler)
    {
        Guard.NotNull(handler, nameof(handler));
        return prev.OnErrorAsync(new AsyncRelayState(handler));
    }

    public static StateToken SetName(this StateToken prev, string name)
    {
        Guard.NotNull(name, nameof(name));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State name cannot be null or whitespace.", nameof(name));
        }

        prev.Builder.SetName(prev.Id, name);
        return new StateToken(prev.Id.WithName(name), prev.Builder);
    }

    public static Graph SetName(this Graph graph, string name)
    {
        Guard.NotNull(graph, nameof(graph));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Node name cannot be null or whitespace.", nameof(name));
        }

        graph.Id = graph.Id.WithName(name);
        return graph;
    }


    /// <summary>
    /// Creates a conditional branch in the FSM graph.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="predicate">A function that returns <c>true</c> for the "then" branch and <c>false</c> for the "else" branch.</param>
    /// <returns></returns>
    public static IfBuilder If(this StateToken prev, Func<bool> predicate)
    {
        return new IfBuilder(prev, predicate);
    }

    /// <summary>
    /// Creates a conditional branch in the FSM graph.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="selector">A function that returns <c>true</c> for the "then" branch and <c>false</c> for the "else" branch.</param>
    /// <typeparam name="TKey">The type of the key used to select the branch.</typeparam>
    /// <returns></returns>
    public static SwitchBuilder<TKey> Switch<TKey>(this StateToken prev,
        Func<TKey> selector)
        where TKey : notnull
    {
        return new SwitchBuilder<TKey>(prev, selector);
    }

    /// <summary>
    /// Creates an async switch branch in the FSM graph with an async key selector.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="selector">An async function that returns the key used to select the branch.</param>
    /// <typeparam name="TKey">The type of the key used to select the branch.</typeparam>
    /// <returns>An AsyncSwitchBuilder that allows chaining cases.</returns>
    public static AsyncSwitchBuilder<TKey> SwitchAsync<TKey>(this StateToken prev,
        Func<ValueTask<TKey>> selector)
        where TKey : notnull
    {
        return new AsyncSwitchBuilder<TKey>(prev, selector);
    }


    /// <summary>
    /// Creates a "then" branch in the FSM graph that executes the specified async logic.
    /// </summary>
    /// <param name="ifBuilder">The IfBuilder that represents the conditional branch.</param>
    /// <param name="run">A function that defines the async logic to be executed in the "then" branch.</param>
    /// <returns>A ThenElseBuilder that allows chaining further actions.</returns>
    public static BranchBuilder ThenAsync(this IfBuilder ifBuilder, Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        AsyncRelayState asyncRelay = new(run);
        return ifBuilder.ThenAsync(asyncRelay);
    }

    /// <summary>
    /// Creates an "else" branch in the FSM graph that executes the specified async logic.
    /// </summary>
    /// <param name="ifBuilder">The BranchBuilder that represents the conditional branch.</param>
    /// <param name="run">A function that defines the async logic to be executed in the "else" branch.</param>
    /// <returns>A <see cref="BranchEnd"/> that allows building the graph or continuing to chain states.</returns>
    public static BranchEnd ElseAsync(this BranchBuilder ifBuilder, Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        AsyncRelayState asyncRelay = new(run);
        return ifBuilder.ElseAsync(asyncRelay);
    }


    /// <summary>
    ///  Creates a case in the switch statement of the FSM graph with async logic.
    /// </summary>
    /// <param name="switchBuilder">The SwitchBuilder that represents the switch statement.</param>
    /// <param name="key">The key that identifies the case in the switch statement.</param>
    /// <param name="run">A function that defines the async logic to be executed for this case.</param>
    /// <typeparam name="TKey">The type of the key used to identify the case.</typeparam>
    /// <returns>A SwitchBuilder that allows chaining further cases or a default case.</returns>
    public static SwitchBuilder<TKey> CaseAsync<TKey>(this SwitchBuilder<TKey> switchBuilder, TKey key,
        Func<CancellationToken, ValueTask<Result>> run)
        where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        AsyncRelayState asyncRelay = new(run);
        return switchBuilder.CaseAsync(key, asyncRelay);
    }

    /// <summary>
    /// Creates a default case in the switch statement of the FSM graph with async logic.
    /// </summary>
    /// <param name="switchBuilder">The SwitchBuilder that represents the switch statement.</param>
    /// <param name="run">A function that defines the async logic to be executed for the default case.</param>
    /// <typeparam name="TKey">The type of the key used to identify the case.</typeparam>
    /// <returns>A SwitchBuilder that allows chaining further cases or finalizing the switch statement.</returns>
    public static SwitchBuilder<TKey> DefaultAsync<TKey>(this SwitchBuilder<TKey> switchBuilder,
        Func<CancellationToken, ValueTask<Result>> run)
        where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        AsyncRelayState asyncRelay = new(run);
        return switchBuilder.DefaultAsync(asyncRelay);
    }

    // ── AsyncSwitchBuilder convenience overloads (lambda → AsyncRelayState) ──

    /// <summary>
    /// Adds an async case with a lambda to the async switch statement.
    /// </summary>
    public static AsyncSwitchBuilder<TKey> CaseAsync<TKey>(this AsyncSwitchBuilder<TKey> switchBuilder, TKey key,
        Func<CancellationToken, ValueTask<Result>> run)
        where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        AsyncRelayState asyncRelay = new(run);
        return switchBuilder.CaseAsync(key, asyncRelay);
    }

    /// <summary>
    /// Adds an async default case with a lambda to the async switch statement.
    /// </summary>
    public static AsyncSwitchBuilder<TKey> DefaultAsync<TKey>(this AsyncSwitchBuilder<TKey> switchBuilder,
        Func<CancellationToken, ValueTask<Result>> run)
        where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        AsyncRelayState asyncRelay = new(run);
        return switchBuilder.DefaultAsync(asyncRelay);
    }

    public static Graph Build(this BranchBuilder branch)
    {
        return branch.Builder.Build();
    }

    // ── BranchBuilder convenience overloads ─────────────────────────────

    /// <summary>
    /// Chains a new async state onto the "then" branch using a lambda.
    /// </summary>
    public static BranchBuilder ToAsync(this BranchBuilder branch, Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.ToAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Sets the display name for the current tip node of the "then" branch.
    /// </summary>
    public static BranchBuilder SetName(this BranchBuilder branch, string name)
    {
        Guard.NotNull(name, nameof(name));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State name cannot be null or whitespace.", nameof(name));
        }

        branch.Builder.SetName(branch.Tip, name);
        return branch;
    }

    // ── BranchEnd convenience overloads ─────────────────────────────────

    /// <summary>
    /// Chains a new async state after the "else" branch using a lambda.
    /// </summary>
    public static StateToken ToAsync(this BranchEnd branchEnd, Func<CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return branchEnd.ToAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Sets the display name for the current tip node of the "else" branch.
    /// </summary>
    public static BranchEnd SetName(this BranchEnd branchEnd, string name)
    {
        Guard.NotNull(name, nameof(name));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State name cannot be null or whitespace.", nameof(name));
        }

        branchEnd.Builder.SetName(branchEnd.Tip, name);
        return branchEnd;
    }
}