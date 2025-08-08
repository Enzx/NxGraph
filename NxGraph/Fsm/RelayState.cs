namespace NxGraph.Fsm;

public sealed class RelayState(
    Func<CancellationToken, ValueTask<Result>> run,
    Func<CancellationToken, ValueTask>? onEnter = null,
    Func<CancellationToken, ValueTask>? onExit = null)
    : State
{
    private readonly Func<CancellationToken, ValueTask<Result>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    protected override ValueTask OnEnterAsync(CancellationToken ct) => onEnter?.Invoke(ct) ?? default;
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => _run(ct);
    protected override ValueTask OnExitAsync(CancellationToken ct) => onExit?.Invoke(ct) ?? default;
}

public sealed class RelayState<TAgent>(
    Func<TAgent, CancellationToken, ValueTask<Result>> run,
    Func<TAgent, CancellationToken, ValueTask>? onEnter = null,
    Func<TAgent, CancellationToken, ValueTask>? onExit = null)
    : State<TAgent>
{
    private readonly Func<TAgent, CancellationToken, ValueTask<Result>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    protected override ValueTask OnEnterAsync(CancellationToken ct) => onEnter?.Invoke(Agent, ct) ?? default;
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => _run(Agent, ct);
    protected override ValueTask OnExitAsync(CancellationToken ct) => onExit?.Invoke(Agent, ct) ?? default;
}