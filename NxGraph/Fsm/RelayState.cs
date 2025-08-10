namespace NxGraph.Fsm;

/// <summary>
/// RelayState is a state that can be used to encapsulate a function that returns a Result.
/// </summary>
/// <param name="run">The function to run when the state is executed. It should return a <see cref="Result"/>.</param>
/// <param name="onEnter">Optional function to run when the state is entered. It can be used for initialization or setup.</param>
/// <param name="onExit">Optional function to run when the state is exited. It can be used for cleanup or finalization.</param>
public sealed class RelayState(
    Func<CancellationToken, ValueTask<Result>> run,
    Func<CancellationToken, ValueTask>? onEnter = null,
    Func<CancellationToken, ValueTask>? onExit = null)
    : State
{
    private readonly Func<CancellationToken, ValueTask<Result>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    protected override ValueTask OnEnterAsync(CancellationToken ct)
    {
        return onEnter?.Invoke(ct) ?? default;
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return _run(ct);
    }

    protected override ValueTask OnExitAsync(CancellationToken ct)
    {
        return onExit?.Invoke(ct) ?? default;
    }
}

/// <summary>
/// RelayState is a state that can be used to encapsulate a function that returns a Result.
/// </summary>
/// <param name="run">The function to run when the state is executed. It should return a <see cref="Result"/>.</param>
/// <param name="onEnter">Optional function to run when the state is entered. It can be used for initialization or setup.</param>
/// <param name="onExit">Optional function to run when the state is exited. It can be used for cleanup or finalization.</param>
/// <typeparam name="TAgent">The type of the agent that this state operates on.</typeparam>
public sealed class RelayState<TAgent>(
    Func<TAgent, CancellationToken, ValueTask<Result>> run,
    Func<TAgent, CancellationToken, ValueTask>? onEnter = null,
    Func<TAgent, CancellationToken, ValueTask>? onExit = null)
    : State<TAgent>
{
    private readonly Func<TAgent, CancellationToken, ValueTask<Result>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    protected override ValueTask OnEnterAsync(CancellationToken ct)
    {
        return onEnter?.Invoke(Agent, ct) ?? default;
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return _run(Agent, ct);
    }

    protected override ValueTask OnExitAsync(CancellationToken ct)
    {
        return onExit?.Invoke(Agent, ct) ?? default;
    }
}