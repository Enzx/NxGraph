using NxGraph.Blackboards;

namespace NxGraph.Fsm.Async;

/// <summary>
/// AsyncRelayState is a state that can be used to encapsulate a function that returns a Result.
/// The blackboard-context overload receives the machine-bound routed context (see
/// <see cref="BlackboardContext"/>) instead of relying on closure capture, so one graph
/// template can serve N machines with distinct boards.
/// </summary>
public sealed class AsyncRelayState : AsyncState
{
    private readonly Func<CancellationToken, ValueTask<Result>>? _run;
    private readonly Func<CancellationToken, ValueTask<Result>>? _onEnter;
    private readonly Func<CancellationToken, ValueTask<Result>>? _onExit;

    private readonly Func<BlackboardContext, CancellationToken, ValueTask<Result>>? _bbRun;
    private readonly Func<BlackboardContext, CancellationToken, ValueTask<Result>>? _bbOnEnter;
    private readonly Func<BlackboardContext, CancellationToken, ValueTask<Result>>? _bbOnExit;

    /// <param name="run">The function to run when the state is executed. It should return a <see cref="Result"/>.</param>
    /// <param name="onEnter">Optional function to run when the state is entered. It can be used for initialization or setup.</param>
    /// <param name="onExit">Optional function to run when the state is exited. It can be used for cleanup or finalization.</param>
    public AsyncRelayState(
        Func<CancellationToken, ValueTask<Result>> run,
        Func<CancellationToken, ValueTask<Result>>? onEnter = null,
        Func<CancellationToken, ValueTask<Result>>? onExit = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    /// <param name="run">The function to run when the state is executed, receiving the routed blackboard context.</param>
    /// <param name="onEnter">Optional enter hook receiving the routed blackboard context.</param>
    /// <param name="onExit">Optional exit hook receiving the routed blackboard context.</param>
    public AsyncRelayState(
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>>? onEnter = null,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>>? onExit = null)
    {
        _bbRun = run ?? throw new ArgumentNullException(nameof(run));
        _bbOnEnter = onEnter;
        _bbOnExit = onExit;
    }

    protected override ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        return _bbRun is not null
            ? _bbOnEnter?.Invoke(Bb, ct) ?? ResultHelpers.InProgress
            : _onEnter?.Invoke(ct) ?? ResultHelpers.InProgress;
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return _bbRun is not null ? _bbRun(Bb, ct) : _run!(ct);
    }

    protected override ValueTask<Result> OnExitAsync(CancellationToken ct)
    {
        return _bbRun is not null
            ? _bbOnExit?.Invoke(Bb, ct) ?? ResultHelpers.Success
            : _onExit?.Invoke(ct) ?? ResultHelpers.Success;
    }
}

/// <summary>
/// RelayState is a state that can be used to encapsulate a function that returns a Result.
/// The combined overload hands the delegate both the stamped agent and the routed
/// blackboard context.
/// </summary>
/// <typeparam name="TAgent">The type of the agent that this state operates on.</typeparam>
public sealed class AsyncRelayState<TAgent> : AsyncState<TAgent>
{
    private readonly Func<TAgent, CancellationToken, ValueTask<Result>>? _run;
    private readonly Func<TAgent, CancellationToken, ValueTask<Result>>? _onEnter;
    private readonly Func<TAgent, CancellationToken, ValueTask<Result>>? _onExit;

    private readonly Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>>? _bbRun;
    private readonly Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>>? _bbOnEnter;
    private readonly Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>>? _bbOnExit;

    /// <param name="run">The function to run when the state is executed. It should return a <see cref="Result"/>.</param>
    /// <param name="onEnter">Optional function to run when the state is entered. It can be used for initialization or setup.</param>
    /// <param name="onExit">Optional function to run when the state is exited. It can be used for cleanup or finalization.</param>
    public AsyncRelayState(
        Func<TAgent, CancellationToken, ValueTask<Result>> run,
        Func<TAgent, CancellationToken, ValueTask<Result>>? onEnter = null,
        Func<TAgent, CancellationToken, ValueTask<Result>>? onExit = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    /// <param name="run">The function to run when the state is executed, receiving the agent and the routed blackboard context.</param>
    /// <param name="onEnter">Optional enter hook receiving the agent and the routed blackboard context.</param>
    /// <param name="onExit">Optional exit hook receiving the agent and the routed blackboard context.</param>
    public AsyncRelayState(
        Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>> run,
        Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>>? onEnter = null,
        Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>>? onExit = null)
    {
        _bbRun = run ?? throw new ArgumentNullException(nameof(run));
        _bbOnEnter = onEnter;
        _bbOnExit = onExit;
    }

    protected override ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        return _bbRun is not null
            ? _bbOnEnter?.Invoke(Agent, Bb, ct) ?? ResultHelpers.InProgress
            : _onEnter?.Invoke(Agent, ct) ?? ResultHelpers.InProgress;
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return _bbRun is not null ? _bbRun(Agent, Bb, ct) : _run!(Agent, ct);
    }

    protected override ValueTask<Result> OnExitAsync(CancellationToken ct)
    {
        return _bbRun is not null
            ? _bbOnExit?.Invoke(Agent, Bb, ct) ?? ResultHelpers.Success
            : _onExit?.Invoke(Agent, ct) ?? ResultHelpers.Success;
    }
}
