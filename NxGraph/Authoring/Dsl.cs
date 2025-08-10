using NxGraph.Fsm;
using NxGraph.Graphs;

// ReSharper disable UnusedMember.Global

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into a new state token with a relay state.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="run">A function that defines the logic to be executed in the relay state.</param>
    /// <returns>A new state token that includes the relay state logic.</returns>
    public static StateToken To(this StateToken prev, Func<CancellationToken, ValueTask<Result>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        INode node = new RelayState(run);
        return prev.To(node);
    }

    public static StateToken SetName(this StateToken prev, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State name cannot be null or whitespace.", nameof(name));
        }

        prev.Builder.SetName(prev.Id, name);
        return prev;
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
    /// Creates a "then" branch in the FSM graph that executes the specified logic.
    /// </summary>
    /// <param name="ifBuilder">The IfBuilder that represents the conditional branch.</param>
    /// <param name="run">A function that defines the logic to be executed in the "then" branch.</param>
    /// <returns>A ThenElseBuilder that allows chaining further actions.</returns>
    public static BranchBuilder Then(this IfBuilder ifBuilder, Func<CancellationToken, ValueTask<Result>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        RelayState relay = new(run);
        return ifBuilder.Then(relay);
    }

    /// <summary>
    /// Creates an "else" branch in the FSM graph that executes the specified logic.
    /// </summary>
    /// <param name="ifBuilder">The ThenElseBuilder that represents the conditional branch.</param>
    /// <param name="run">A function that defines the logic to be executed in the "else" branch.</param>
    /// <returns>A TerminalBuilder that allows chaining further actions.
    /// (i.e. Building the final state of the FSM graph)</returns>
    public static BranchEnd Else(this BranchBuilder ifBuilder, Func<CancellationToken, ValueTask<Result>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        RelayState relay = new(run);
        return ifBuilder.Else(relay);
    }


    /// <summary>
    ///  Creates a case in the switch statement of the FSM graph.
    /// </summary>
    /// <param name="switchBuilder">The SwitchBuilder that represents the switch statement.</param>
    /// <param name="key">The key that identifies the case in the switch statement.</param>
    /// <param name="run">A function that defines the logic to be executed for this case.</param>
    /// <typeparam name="TKey">The type of the key used to identify the case.</typeparam>
    /// <returns>A SwitchBuilder that allows chaining further cases or a default case.</returns>
    public static SwitchBuilder<TKey> Case<TKey>(this SwitchBuilder<TKey> switchBuilder, TKey key,
        Func<CancellationToken, ValueTask<Result>> run)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(run);
        RelayState relay = new(run);
        return switchBuilder.Case(key, relay);
    }

    /// <summary>
    /// Creates a default case in the switch statement of the FSM graph.
    /// </summary>
    /// <param name="switchBuilder">The SwitchBuilder that represents the switch statement.</param>
    /// <param name="run">A function that defines the logic to be executed for the default case.</param>
    /// <typeparam name="TKey">The type of the key used to identify the case.</typeparam>
    /// <returns>A SwitchBuilder that allows chaining further cases or finalizing the switch statement.</returns>
    public static SwitchBuilder<TKey> Default<TKey>(this SwitchBuilder<TKey> switchBuilder,
        Func<CancellationToken, ValueTask<Result>> run)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(run);
        RelayState relay = new(run);
        return switchBuilder.Default(relay);
    }

    public static Graph Build(this BranchBuilder branch)
    {
        return branch.Builder.Build();
    }
}