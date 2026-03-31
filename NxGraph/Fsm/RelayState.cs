namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="AsyncRelayState"/>.
/// Wraps plain delegates (<see cref="Func{Result}"/>) for zero-allocation inline execution.
/// </summary>
public sealed class RelayState(
    Func<Result> run,
    Action? onEnter = null,
    Action? onExit = null)
    : State
{
    private readonly Func<Result> _run = run ?? throw new ArgumentNullException(nameof(run));

    protected override void OnEnter() => onEnter?.Invoke();

    protected override Result OnRun() => _run();

    protected override void OnExit() => onExit?.Invoke();
}

/// <summary>
/// Synchronous counterpart of <see cref="AsyncRelayState{TAgent}"/>.
/// </summary>
/// <typeparam name="TAgent">The type of agent available during execution.</typeparam>
public sealed class RelayState<TAgent>(
    Func<TAgent, Result> run,
    Action<TAgent>? onEnter = null,
    Action<TAgent>? onExit = null)
    : State<TAgent>
{
    private readonly Func<TAgent, Result> _run = run ?? throw new ArgumentNullException(nameof(run));

    protected override void OnEnter() => onEnter?.Invoke(Agent);

    protected override Result OnRun() => _run(Agent);

    protected override void OnExit() => onExit?.Invoke(Agent);
}

