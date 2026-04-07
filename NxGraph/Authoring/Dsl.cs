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