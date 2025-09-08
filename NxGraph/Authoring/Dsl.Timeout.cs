using NxGraph.Fsm;
using NxGraph.Graphs;
using Timeout = NxGraph.Fsm.Timeout;
#if NETSTANDARD2_1
using ArgumentNullException = System.ArgumentNullExceptionShim;
#endif
namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Starts a new graph with a timed node as the start state.
    /// </summary>
    public static StateToken StartWithTimeout(Func<CancellationToken, ValueTask<Result>> run, TimeSpan timeout,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        ArgumentNullException.ThrowIfNull(run);
        return FsmDsl.StartWith(Timeout.For(timeout, run, behavior));
    }

    /// <summary>
    /// Adds a brand-new timed node and wires a transition to it.
    /// Example:
    /// <code>
    /// GraphBuilder.Start()
    ///     .ToWithTimeout(2.Seconds(), _ => ResultHelpers.Success)
    ///     .To(_ => ResultHelpers.Success)
    ///     .ToStateMachine();
    /// </code>
    /// </summary>
    public static StateToken ToWithTimeout(this StateToken prev,
        TimeSpan timeout,
        Func<CancellationToken, ValueTask<Result>> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        ArgumentNullException.ThrowIfNull(run);
        ILogic wrapped = Timeout.Wrap(new RelayState(run), timeout, behavior);
        return prev.To(wrapped);
    }

    /// <summary>
    /// Utility to wrap an existing node logic in-place with a timeout (when you already have a node instance).
    /// </summary>
    public static StateToken ToWithTimeout(this StateToken prev, ILogic nextStateLogic, TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        return prev.To(Timeout.Wrap(nextStateLogic, timeout, behavior));
    }

    /// <summary>
    /// Wraps the next state logic in a timeout and returns a new state token.
    /// </summary>
    /// <param name="prev"> The previous state token, which is the source of the transition.</param>
    /// <param name="nextStateLogic">  The logic for the next state, which will be wrapped in a timeout.</param>
    /// <param name="timeout">The duration of the timeout for the next state logic.</param>
    /// <param name="behavior">The behavior to apply when the timeout occurs, such as failing or skipping.</param>
    /// <returns>A new state token that represents the next state logic wrapped in a timeout.</returns>
    public static StateToken ToWithTimeout(this StartToken prev, ILogic nextStateLogic, TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(nextStateLogic);
        ILogic wrapped = Timeout.Wrap(nextStateLogic, timeout, behavior);
        NodeId id = prev.Builder.AddNode(wrapped, true);
        return new StateToken(id, prev.Builder);
    }


    /// <summary>
    /// Wraps the next state logic in a timeout and returns a new state token.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="run">The logic for the next state, which will be executed with a timeout.</param>
    /// <param name="timeout">The duration of the timeout for the next state logic.</param>
    /// <param name="behavior">The behavior to apply when the timeout occurs, such as failing or skipping.</param>
    /// <returns>A new state token that represents the next state logic wrapped in a timeout.</returns>
    public static StateToken ToWithTimeout(this StartToken prev, Func<CancellationToken, ValueTask<Result>> run,
        TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(run);
        ILogic nextStateLogic = new RelayState(run);
        ILogic wrapped = Timeout.Wrap(nextStateLogic, timeout, behavior);
        NodeId id = prev.Builder.AddNode(wrapped, true);
        return new StateToken(id, prev.Builder);
    }
}