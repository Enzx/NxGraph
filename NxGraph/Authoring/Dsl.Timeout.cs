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
    public static StateToken StartWithTimeoutAsync(TimeSpan timeout, Func<CancellationToken, ValueTask<Result>> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(run, nameof(run));
        return GraphBuilder.StartWithAsync(Timeout.For(timeout, run, behavior));
    }

    /// <inheritdoc cref="StartWithTimeoutAsync(TimeSpan, Func{CancellationToken, ValueTask{Result}}, TimeoutBehavior)"/>
    [Obsolete("Use the timeout-first overload — the timeout DSL overloads disagreed on parameter order; " +
              "they now uniformly take the timeout first. This alias forwards and will not change behavior.")]
    public static StateToken StartWithTimeoutAsync(Func<CancellationToken, ValueTask<Result>> run, TimeSpan timeout,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return StartWithTimeoutAsync(timeout, run, behavior);
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
    public static StateToken ToWithTimeoutAsync(this StateToken prev, TimeSpan timeout,
        IAsyncLogic nextStateAsyncLogic, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return prev.ToAsync(Timeout.Wrap(nextStateAsyncLogic, timeout, behavior));
    }

    /// <inheritdoc cref="ToWithTimeoutAsync(StateToken, TimeSpan, IAsyncLogic, TimeoutBehavior)"/>
    [Obsolete("Use the timeout-first overload — the timeout DSL overloads disagreed on parameter order; " +
              "they now uniformly take the timeout first. This alias forwards and will not change behavior.")]
    public static StateToken ToWithTimeoutAsync(this StateToken prev, IAsyncLogic nextStateAsyncLogic, TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        return prev.ToWithTimeoutAsync(timeout, nextStateAsyncLogic, behavior);
    }

    /// <summary>
    /// Wraps the next async state logic in a timeout and starts the graph with it.
    /// </summary>
    /// <param name="prev">The start token, which is the entry point of the FSM graph.</param>
    /// <param name="timeout">The duration of the timeout for the next state logic.</param>
    /// <param name="nextStateAsyncLogic">The logic for the next state, which will be wrapped in a timeout.</param>
    /// <param name="behavior">The behavior to apply when the timeout occurs.</param>
    /// <returns>A new state token that represents the next state logic wrapped in a timeout.</returns>
    public static StateToken ToWithTimeoutAsync(this StartToken prev, TimeSpan timeout,
        IAsyncLogic nextStateAsyncLogic, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(nextStateAsyncLogic, nameof(nextStateAsyncLogic));
        IAsyncLogic wrapped = Timeout.Wrap(nextStateAsyncLogic, timeout, behavior);
        NodeId id = prev.Builder.AddNode(wrapped, true);
        return new StateToken(id, prev.Builder);
    }

    /// <inheritdoc cref="ToWithTimeoutAsync(StartToken, TimeSpan, IAsyncLogic, TimeoutBehavior)"/>
    [Obsolete("Use the timeout-first overload — the timeout DSL overloads disagreed on parameter order; " +
              "they now uniformly take the timeout first. This alias forwards and will not change behavior.")]
    public static StateToken ToWithTimeoutAsync(this StartToken prev, IAsyncLogic nextStateAsyncLogic, TimeSpan timeout,
        TimeoutBehavior behavior)
    {
        return prev.ToWithTimeoutAsync(timeout, nextStateAsyncLogic, behavior);
    }

    /// <summary>
    /// Wraps the next async state logic in a timeout and starts the graph with it.
    /// </summary>
    /// <param name="prev">The start token, which is the entry point of the FSM graph.</param>
    /// <param name="timeout">The duration of the timeout for the next state logic.</param>
    /// <param name="run">The logic for the next state, which will be executed with a timeout.</param>
    /// <param name="behavior">The behavior to apply when the timeout occurs.</param>
    /// <returns>A new state token that represents the next state logic wrapped in a timeout.</returns>
    public static StateToken ToWithTimeoutAsync(this StartToken prev, TimeSpan timeout,
        Func<CancellationToken, ValueTask<Result>> run, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(run, nameof(run));
        IAsyncLogic nextStateAsyncLogic = new AsyncRelayState(run);
        IAsyncLogic wrapped = Timeout.Wrap(nextStateAsyncLogic, timeout, behavior);
        NodeId id = prev.Builder.AddNode(wrapped, true);
        return new StateToken(id, prev.Builder);
    }

    /// <inheritdoc cref="ToWithTimeoutAsync(StartToken, TimeSpan, Func{CancellationToken, ValueTask{Result}}, TimeoutBehavior)"/>
    [Obsolete("Use the timeout-first overload — the timeout DSL overloads disagreed on parameter order; " +
              "they now uniformly take the timeout first. This alias forwards and will not change behavior.")]
    public static StateToken ToWithTimeoutAsync(this StartToken prev, Func<CancellationToken, ValueTask<Result>> run,
        TimeSpan timeout, TimeoutBehavior behavior)
    {
        return prev.ToWithTimeoutAsync(timeout, run, behavior);
    }

    // ── Sync twins (see TimeoutState) ─────────────────────────────────────

    /// <summary>
    /// Adds a brand-new timed <b>sync</b> node and wires a transition to it — the
    /// runtime-parity twin of <see cref="ToWithTimeoutAsync(StateToken, TimeSpan, Func{CancellationToken, ValueTask{Result}}, TimeoutBehavior)"/>.
    /// The deadline is checked between ticks (see <see cref="TimeoutState"/>): a node that
    /// returns <see cref="Result.InProgress"/> past the timeout produces the timeout outcome
    /// per <paramref name="behavior"/>.
    /// </summary>
    public static StateToken ToWithTimeout(this StateToken prev, TimeSpan timeout, Func<Result> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(run, nameof(run));
        return prev.To(new TimeoutState(new RelayState(run), timeout, behavior));
    }

    /// <summary>
    /// Wraps an existing sync node logic with a timeout and wires a transition to it
    /// (see <see cref="TimeoutState"/>).
    /// </summary>
    public static StateToken ToWithTimeout(this StateToken prev, TimeSpan timeout, ILogic nextStateLogic,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(nextStateLogic, nameof(nextStateLogic));
        return prev.To(new TimeoutState(nextStateLogic, timeout, behavior));
    }

    /// <summary>
    /// Starts the graph with a timed sync node (see <see cref="TimeoutState"/>).
    /// </summary>
    public static StateToken ToWithTimeout(this StartToken prev, TimeSpan timeout, Func<Result> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(run, nameof(run));
        return prev.To(new TimeoutState(new RelayState(run), timeout, behavior));
    }

    /// <summary>
    /// Starts the graph with an existing sync node logic wrapped in a timeout
    /// (see <see cref="TimeoutState"/>).
    /// </summary>
    public static StateToken ToWithTimeout(this StartToken prev, TimeSpan timeout, ILogic nextStateLogic,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        Guard.NotNull(nextStateLogic, nameof(nextStateLogic));
        return prev.To(new TimeoutState(nextStateLogic, timeout, behavior));
    }
}