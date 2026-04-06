using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// A synchronous state machine with an agent, mirroring <see cref="AsyncStateMachine{TAgent}"/>.
/// </summary>
public class StateMachine<TAgent>(Graph graph, IStateMachineObserver? observer = null)
    : StateMachine(graph, observer), IAgentSettable<TAgent>
{
    public void SetAgent(TAgent agent) => Graph.SetAgent(agent);
}

/// <summary>
/// Synchronous, zero-allocation state machine that inherits <see cref="State"/> so it
/// can be nested as a node inside a parent graph — mirroring the
/// <see cref="AsyncStateMachine"/> → <see cref="AsyncState"/> relationship.
/// <para>
/// All threading overhead has been removed:
/// <list type="bullet">
///   <item><c>_status</c> is a plain field (no <see cref="Volatile"/>/<see cref="Interlocked"/>).</item>
///   <item><c>_executeGate</c> is a plain <see langword="bool"/>.</item>
///   <item><c>_autoReset</c> is a plain <see langword="bool"/>.</item>
///   <item>No <see cref="CancellationToken"/>, no <c>async</c>/<c>await</c>, no <c>ValueTask</c>.</item>
///   <item>Observer callbacks are <c>void</c> (see <see cref="IStateMachineObserver"/>).</item>
/// </list>
/// </para>
/// <para>
/// <b>Full-run (blocking)</b> — use the inherited <see cref="State.Execute"/>:
/// <code>Result r = fsm.Execute();</code>
/// This is also the entry point used automatically when the machine is nested
/// as a node in a parent <see cref="StateMachine"/>.
/// </para>
/// <para>
/// Node logic is executed via <see cref="ILogic.Execute"/> — every node in the graph
/// must have its <see cref="INode.Logic"/> populated (i.e. implement <see cref="ILogic"/>).
/// </para>
/// </summary>
public class StateMachine : State
{
    public readonly Graph Graph;
    private readonly IStateMachineObserver? _observer;

    private ExecutionStatus _status = ExecutionStatus.Created;

    private NodeId _current;
    private readonly NodeId _initial;

    private RestartPolicy _restartPolicy = RestartPolicy.Auto;
    private Result _terminalResult = Result.Success;
    private bool _executeGate;
    private bool _reentranceGuard;
    private readonly Action<string> _cachedLogReportCallback;

    /// <summary>Public execution status.</summary>
    public ExecutionStatus Status
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _status;
    }

    public StateMachine(Graph graph, IStateMachineObserver? observer = null)
    {
        Graph = graph;
        _observer = observer;
        _initial = graph.StartNode.Id;
        _current = _initial;
        _cachedLogReportCallback = LogReportCallback;
    }

    /// <summary>
    /// Controls how the machine behaves after reaching a terminal status.
    /// <list type="bullet">
    /// <item><see cref="RestartPolicy.Auto"/>: automatically resets to Ready after completion/failure.</item>
    /// <item><see cref="RestartPolicy.Manual"/>: requires explicit <see cref="Reset"/>; re-execution throws.</item>
    /// <item><see cref="RestartPolicy.Ignore"/>: ignores further execution attempts and returns the terminal result until <see cref="Reset"/> is called.</item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResetPolicy(RestartPolicy policy) => _restartPolicy = policy;

    /// <summary>
    /// Backwards-compatible switch for the legacy auto-reset behaviour.
    /// <para>Maps to <see cref="RestartPolicy.Auto"/> (true) and <see cref="RestartPolicy.Manual"/> (false).</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAutoReset(bool enabled) => _restartPolicy = enabled ? RestartPolicy.Auto : RestartPolicy.Manual;

    /// <summary>
    /// Reset to the initial node. Disallowed while Running/Transitioning.
    /// Status moves: (any non-running) → Resetting → Ready.
    /// </summary>
    public Result Reset()
    {
        ExecutionStatus status = _status;
        switch (status)
        {
            case ExecutionStatus.Running:
            case ExecutionStatus.Transitioning:
            case ExecutionStatus.Starting:
                throw new InvalidOperationException("Cannot reset while starting, running, or transitioning.");
            case ExecutionStatus.Created:
            case ExecutionStatus.Ready:
                return Result.Success;
            case ExecutionStatus.Completed:
            case ExecutionStatus.Failed:
            case ExecutionStatus.Cancelled:
            case ExecutionStatus.Resetting:
                break;
            default:
                throw new IndexOutOfRangeException(nameof(status));
        }

        TransitionTo(ExecutionStatus.Resetting);
        _current = _initial;
        TransitionTo(ExecutionStatus.Ready);
        return Result.Success;
    }

    // ── State overrides (full-run via inherited Execute()) ──────────────

    protected override void OnEnter()
    {
        if (_executeGate)
        {
            throw new InvalidOperationException("StateMachine is already executing.");
        }

        // If we're terminal, apply reset policy.
        ExecutionStatus status = _status;
        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            if (_restartPolicy == RestartPolicy.Ignore)
            {
                // Ignore further execution attempts until a manual Reset() occurs.
                return;
            }

            if (_restartPolicy == RestartPolicy.Manual)
            {
                throw new InvalidOperationException(
                    $"StateMachine is in terminal state '{status}'. Call Reset() before executing again.");
            }

            // For Auto, we should never normally reach terminal (we reset in Finalise),
            // but tolerate it by resetting now.
            Reset();
        }

        _executeGate = true;

        TransitionTo(ExecutionStatus.Starting);
        _current = _initial;

        _observer?.OnStateEntered(_current);

        TransitionTo(ExecutionStatus.Running);
    }

    protected override Result OnRun()
    {
        // Ignore policy: once terminal, do not re-run.
        ExecutionStatus status = _status;
        if (_restartPolicy == RestartPolicy.Ignore &&
            status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            return _terminalResult;
        }

        // ── Re-entrance guard ───────────────────────────────────────────
        if (_reentranceGuard)
        {
            throw new InvalidOperationException("StateMachine is already executing.");
        }

        _reentranceGuard = true;
        try
        {
            // Full-run path (State.Execute): loop until terminal.
            // OnRun must never return Result.Continue.
            while (true)
            {
                try
                {
                    Result result = TickInternal();
                    if (result == Result.Continue)
                    {
                        continue;
                    }

                    Finalise(result);
                    return result;
                }
                catch (Exception ex)
                {
                    _observer?.OnStateFailed(_current, ex);
                    Finalise(Result.Failure);
                    throw;
                }
            }
        }
        finally
        {
            _reentranceGuard = false;
        }
    }

    protected override void OnExit()
    {
        // Gate is released in Finalise for the normal path.
        // For Ignore policy we also don't want OnExit to toggle it erroneously.
        if (_restartPolicy != RestartPolicy.Ignore)
        {
            _executeGate = false;
        }
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Finalise(Result result)
    {
        _terminalResult = result;
        TransitionTo(result.IsSuccess ? ExecutionStatus.Completed : ExecutionStatus.Failed);

        if (_restartPolicy == RestartPolicy.Auto)
        {
            Reset();
        }

        _executeGate = false;
    }

    /// <summary>
    /// Executes the current node. Returns <see cref="Result.Continue"/> when the machine
    /// transitions to a next node, or a terminal result (<see cref="Result.Success"/> /
    /// <see cref="Result.Failure"/>) when the run is finished.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Result TickInternal()
    {
        if (!Graph.TryGetNode(_current, out INode? node))
        {
            throw new InvalidOperationException($"Node '{_current}' not found.");
        }

        LogicNode logicNode = (LogicNode)node;

        // Wire log-report callback for nodes that support it.
        if (logicNode.Logic is State stateForLog)
        {
            stateForLog.SyncLogReport = _cachedLogReportCallback;
        }

        // Execute the node synchronously.
        ILogic syncLogic = logicNode.Logic
            ?? throw new InvalidOperationException(
                $"Node '{_current}' logic ({logicNode.AsyncLogic.GetType().Name}) does not implement ILogic. " +
                "All nodes in a StateMachine must implement ILogic.");

        Result result = syncLogic.Execute();

        switch (result.Code)
        {
            case Result.StatusCode.Success:
            {
                _observer?.OnStateExited(_current);

                NodeId next;

                if (logicNode.Logic is IDirector director)
                {
                    next = director.SelectNext();
                    if (next.Equals(NodeId.Default))
                    {
                        return Result.Success;
                    }
                }
                else
                {
                    if (!Graph.TryGetTransition(_current, out Transition edge))
                    {
                        _observer?.OnStateFailed(
                            _current,
                            new InvalidOperationException($"No transition found for state '{_current}'."));

                        return Result.Failure;
                    }

                    if (edge.IsEmpty)
                    {
                        return Result.Success; // terminal
                    }

                    next = edge.Destination;
                }

                TransitionTo(ExecutionStatus.Transitioning);

                _observer?.OnTransition(_current, next);

                _current = next;

                _observer?.OnStateEntered(_current);

                TransitionTo(ExecutionStatus.Running);

                return Result.Continue;
            }
            case Result.StatusCode.Failure:
                return Result.Failure;
            case Result.StatusCode.Continue:
                throw new InvalidOperationException(
                    $"Node '{_current}' returned Result.Continue, which is reserved for the " +
                    "stepped-execution runtime. Node logic must return Success or Failure.");
            default:
                throw new InvalidOperationException("Unknown node result.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogReportCallback(string message)
    {
        _observer?.OnLogReport(_current, message);
    }

    /// <summary>
    /// Transitions the machine to a new execution status, invoking observer callbacks if the status changed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransitionTo(ExecutionStatus next)
    {
        ExecutionStatus prev = _status;
        Debug.Assert(prev != next, $"Redundant status transition: {prev} -> {next}");
        _status = next;
        if (prev != next)
        {
            NotifyMachineStatusChange(prev, next);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifyMachineStatusChange(ExecutionStatus prev, ExecutionStatus next)
    {
        if (_observer is null)
        {
            return;
        }

        NodeId graphId = Graph.Id;
        _observer.StateMachineStatusChanged(graphId, prev, next);

        switch (next)
        {
            case ExecutionStatus.Starting:
                _observer.OnStateMachineStarted(graphId);
                break;

            case ExecutionStatus.Completed:
                _observer.OnStateMachineCompleted(graphId, Result.Success);
                break;

            case ExecutionStatus.Failed:
                _observer.OnStateMachineCompleted(graphId, Result.Failure);
                break;

            case ExecutionStatus.Resetting:
                _observer.OnStateMachineReset(graphId);
                break;

            case ExecutionStatus.Cancelled:
                // Synchronous StateMachine currently has no cancellation surface,
                // but tolerate the status for parity with the async machine.
                break;

            case ExecutionStatus.Ready:
            case ExecutionStatus.Created:
            case ExecutionStatus.Running:
            case ExecutionStatus.Transitioning:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(next), next, null);
        }
    }
}

