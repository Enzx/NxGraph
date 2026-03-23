namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="RelayState"/>.
/// Wraps plain delegates (<see cref="Func{Result}"/>) for zero-allocation inline execution.
/// </summary>
public sealed class SyncRelayState : SyncState
{
    private readonly Func<Result> _run;
    private readonly Action? _onEnter;
    private readonly Action? _onExit;

    public SyncRelayState(
        Func<Result> run,
        Action? onEnter = null,
        Action? onExit = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    protected override void OnEnter() => _onEnter?.Invoke();

    protected override Result OnRun() => _run();

    protected override void OnExit() => _onExit?.Invoke();
}

/// <summary>
/// Synchronous counterpart of <see cref="RelayState{TAgent}"/>.
/// </summary>
/// <typeparam name="TAgent">The type of agent available during execution.</typeparam>
public sealed class SyncRelayState<TAgent> : SyncState<TAgent>
{
    private readonly Func<TAgent, Result> _run;
    private readonly Action<TAgent>? _onEnter;
    private readonly Action<TAgent>? _onExit;

    public SyncRelayState(
        Func<TAgent, Result> run,
        Action<TAgent>? onEnter = null,
        Action<TAgent>? onExit = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    protected override void OnEnter() => _onEnter?.Invoke(Agent);

    protected override Result OnRun() => _run(Agent);

    protected override void OnExit() => _onExit?.Invoke(Agent);
}

