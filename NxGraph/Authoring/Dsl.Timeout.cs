using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using Timeout = NxGraph.Fsm.Async.Timeout;
namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Starts a new graph with a timed async node as the start state.
    /// </summary>
    public static StateToken StartWithTimeoutAsync(Func<CancellationToken, ValueTask<Result>> run, TimeSpan timeout,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(run, nameof(run));
        return GraphBuilder.StartWithAsync(Timeout.For(timeout, run, behavior));
    }

    /// <summary>
    /// Adds a brand-new timed async node and wires a transition to it.
    /// Example:
    /// <code>
    /// GraphBuilder.Start()
    ///     .ToWithTimeoutAsync(2.Seconds(), _ => ResultHelpers.Success)
    ///     .ToAsync(_ => ResultHelpers.Success)
    ///     .ToAsyncStateMachine();
    /// </code>
    /// </summary>
    public static StateToken ToWithTimeoutAsync(this StateToken prev,
        TimeSpan timeout,
        Func<CancellationToken, ValueTask<Result>> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(run, nameof(run));
        IAsyncLogic wrapped = Timeout.Wrap(new AsyncRelayState(run), timeout, behavior);
        return prev.ToAsync(wrapped);
    }

    /// <summary>
    /// Utility to wrap an existing async node logic in-place with a timeout (when you already have a node instance).
    /// </summary>
    public static StateToken ToWithTimeoutAsync(this StateToken prev, IAsyncLogic nextStateAsyncLogic, TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        return prev.ToAsync(Timeout.Wrap(nextStateAsyncLogic, timeout, behavior));
    }

    /// <summary>
    /// Wraps the next async state logic in a timeout and returns a new state token.
    /// </summary>
    /// <param name="prev"> The previous state token, which is the source of the transition.</param>
    /// <param name="nextStateAsyncLogic">  The logic for the next state, which will be wrapped in a timeout.</param>
    /// <param name="timeout">The duration of the timeout for the next state logic.</param>
    /// <param name="behavior">The behavior to apply when the timeout occurs, such as failing or skipping.</param>
    /// <returns>A new state token that represents the next state logic wrapped in a timeout.</returns>
    public static StateToken ToWithTimeoutAsync(this StartToken prev, IAsyncLogic nextStateAsyncLogic, TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        Guard.NotNull(nextStateAsyncLogic, nameof(nextStateAsyncLogic));
        IAsyncLogic wrapped = Timeout.Wrap(nextStateAsyncLogic, timeout, behavior);
        NodeId id = prev.Builder.AddNode(wrapped, true);
        return new StateToken(id, prev.Builder);
    }


    /// <summary>
    /// Wraps the next async state logic in a timeout and returns a new state token.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="run">The logic for the next state, which will be executed with a timeout.</param>
    /// <param name="timeout">The duration of the timeout for the next state logic.</param>
    /// <param name="behavior">The behavior to apply when the timeout occurs, such as failing or skipping.</param>
    /// <returns>A new state token that represents the next state logic wrapped in a timeout.</returns>
    public static StateToken ToWithTimeoutAsync(this StartToken prev, Func<CancellationToken, ValueTask<Result>> run,
        TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        Guard.NotNull(run, nameof(run));
        IAsyncLogic nextStateAsyncLogic = new AsyncRelayState(run);
        IAsyncLogic wrapped = Timeout.Wrap(nextStateAsyncLogic, timeout, behavior);
        NodeId id = prev.Builder.AddNode(wrapped, true);
        return new StateToken(id, prev.Builder);
    }
}