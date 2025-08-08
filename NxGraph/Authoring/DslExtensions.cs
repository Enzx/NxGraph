using NxGraph.Fsm;
using NxGraph.Graphs;

// ReSharper disable UnusedMember.Global

namespace NxGraph.Authoring;

public static partial class DslExtensions
{
    /// <summary>
    /// Creates a conditional branch in the FSM graph.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="predicate">A function that returns <c>true</c> for the "then" branch and <c>false</c> for the "else" branch.</param>
    /// <returns></returns>
    public static IfBuilder If(this StateToken prev, Func<bool> predicate)
        => new(prev, predicate);

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
        => new(prev, selector);

    /// <summary>
    /// Converts the previous state token into a state machine.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="observer">An optional observer that can be used to monitor the state machine's execution.</param>
    /// <returns>A state machine built from the previous state token.</returns>
    public static StateMachine ToStateMachine(this StateToken prev, IAsyncStateObserver? observer = null)
    {
        return prev.Build().ToStateMachine(observer);
    }
    
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StateToken prev, IAsyncStateObserver? observer = null)
    {
        return prev.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    ///  Converts the start token into a state machine.
    /// </summary>
    /// <param name="startToken">The start token, which is the entry point of the FSM graph.</param>
    /// <returns>A state machine built from the start token.</returns>
    public static StateMachine ToStateMachine(this StartToken startToken)
    {
        ArgumentNullException.ThrowIfNull(startToken);
        return startToken.Builder.Build().ToStateMachine();
    }

    /// <summary>
    /// Converts the terminal builder into a state machine.
    /// </summary>
    /// <param name="ifBuilder">The terminal builder, which is the final state of the FSM graph.</param>
    /// <returns>A state machine built from the terminal builder.</returns>
    public static StateMachine ToStateMachine(this TerminalBuilder ifBuilder)
    {
        ArgumentNullException.ThrowIfNull(ifBuilder);
        return ifBuilder.Builder.Build().ToStateMachine();
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this TerminalBuilder ifBuilder)
    {
        ArgumentNullException.ThrowIfNull(ifBuilder);
        return ifBuilder.Builder.Build().ToStateMachine<TAgent>();
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StartToken startToken,
        IAsyncStateObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(startToken);
        return startToken.Builder.Build().ToStateMachine<TAgent>(observer);
    }

    public static StateMachine<TAgent> WithAgent<TAgent>(this StateMachine<TAgent> fsm,
        TAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        fsm.SetAgent(agent);
        return fsm;
    }

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

    public static TimeSpan Milliseconds(this int milliseconds)
    {
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public static TimeSpan Seconds(this int seconds)
    {
        return TimeSpan.FromSeconds(seconds);
    }

    public static TimeSpan Minutes(this int minutes)
    {
        return TimeSpan.FromMinutes(minutes);
    }

    public static TimeSpan Hours(this int hours)
    {
        return TimeSpan.FromHours(hours);
    }

    public static TimeSpan Days(this int days)
    {
        return TimeSpan.FromDays(days);
    }


    public static StateToken WaitFor(this StateToken token, TimeSpan delay, CancellationToken ct = default)
    {
        return token.To(Wait.For(delay, ct));
    }

    public static StateToken WaitFor(this StartToken token, TimeSpan delay, CancellationToken ct = default)
    {
        State node = Wait.For(delay, ct);
        NodeId id = token.Builder.AddNode(node, isStart: true);
        return new StateToken(id, token.Builder);
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
    

    public static StateMachine ToStateMachine(this BranchBuilder branch, IAsyncStateObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.Builder.Build().ToStateMachine(observer);
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this BranchBuilder branch,
        IAsyncStateObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.Builder.Build().ToStateMachine<TAgent>(observer);
    }


    public static Graph Build(this BranchBuilder branch)
    {
        return branch.Builder.Build();
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
}