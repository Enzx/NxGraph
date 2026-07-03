using NxGraph.Blackboards;
using NxGraph.Fsm.Async;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="AsyncRelayState"/>.
/// Wraps plain delegates (<see cref="Func{Result}"/>) for zero-allocation inline execution.
/// The blackboard-context overload receives the machine-bound routed context (see
/// <see cref="BlackboardContext"/>) instead of relying on closure capture, so one graph
/// template can serve N machines with distinct boards.
/// </summary>
public sealed class RelayState : State
{
    private readonly Func<Result>? _run;
    private readonly Action? _onEnter;
    private readonly Action? _onExit;

    private readonly Func<BlackboardContext, Result>? _bbRun;
    private readonly Action<BlackboardContext>? _bbOnEnter;
    private readonly Action<BlackboardContext>? _bbOnExit;

    public RelayState(
        Func<Result> run,
        Action? onEnter = null,
        Action? onExit = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    public RelayState(
        Func<BlackboardContext, Result> run,
        Action<BlackboardContext>? onEnter = null,
        Action<BlackboardContext>? onExit = null)
    {
        _bbRun = run ?? throw new ArgumentNullException(nameof(run));
        _bbOnEnter = onEnter;
        _bbOnExit = onExit;
    }

    protected override void OnEnter()
    {
        if (_bbRun is not null)
        {
            _bbOnEnter?.Invoke(Bb);
        }
        else
        {
            _onEnter?.Invoke();
        }
    }

    protected override Result OnRun() => _bbRun is not null ? _bbRun(Bb) : _run!();

    protected override void OnExit()
    {
        if (_bbRun is not null)
        {
            _bbOnExit?.Invoke(Bb);
        }
        else
        {
            _onExit?.Invoke();
        }
    }
}

/// <summary>
/// Synchronous counterpart of <see cref="AsyncRelayState{TAgent}"/>. The combined overload
/// hands the delegate both the stamped agent and the routed blackboard context.
/// </summary>
/// <typeparam name="TAgent">The type of agent available during execution.</typeparam>
public sealed class RelayState<TAgent> : State<TAgent>
{
    private readonly Func<TAgent, Result>? _run;
    private readonly Action<TAgent>? _onEnter;
    private readonly Action<TAgent>? _onExit;

    private readonly Func<TAgent, BlackboardContext, Result>? _bbRun;
    private readonly Action<TAgent, BlackboardContext>? _bbOnEnter;
    private readonly Action<TAgent, BlackboardContext>? _bbOnExit;

    public RelayState(
        Func<TAgent, Result> run,
        Action<TAgent>? onEnter = null,
        Action<TAgent>? onExit = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    public RelayState(
        Func<TAgent, BlackboardContext, Result> run,
        Action<TAgent, BlackboardContext>? onEnter = null,
        Action<TAgent, BlackboardContext>? onExit = null)
    {
        _bbRun = run ?? throw new ArgumentNullException(nameof(run));
        _bbOnEnter = onEnter;
        _bbOnExit = onExit;
    }

    protected override void OnEnter()
    {
        if (_bbRun is not null)
        {
            _bbOnEnter?.Invoke(Agent, Bb);
        }
        else
        {
            _onEnter?.Invoke(Agent);
        }
    }

    protected override Result OnRun() => _bbRun is not null ? _bbRun(Agent, Bb) : _run!(Agent);

    protected override void OnExit()
    {
        if (_bbRun is not null)
        {
            _bbOnExit?.Invoke(Agent, Bb);
        }
        else
        {
            _onExit?.Invoke(Agent);
        }
    }
}
