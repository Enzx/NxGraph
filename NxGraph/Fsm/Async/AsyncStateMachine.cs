using System.Diagnostics;
using NxGraph.Diagnostics.Replay;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// An async state machine that executes a graph of nodes with full lifecycle/observer support.
/// </summary>
public class AsyncStateMachine<TAgent>(Graph graph, IAsyncStateMachineObserver? observer = null)
    : AsyncStateMachine(graph, observer), IAgentSettable<TAgent>
{
    public void SetAgent(TAgent agent) => Graph.SetAgent(agent);
}

/// <summary>
/// Non-generic AsyncStateMachine.
/// </summary>
public class AsyncStateMachine : AsyncState
{
    public readonly Graph Graph;
    private readonly IAsyncStateMachineObserver? _observer;

    private int _statusInt = (int)ExecutionStatus.Created; // atomic state

    private NodeId _current;
    private readonly NodeId _initial;

    // Reset behaviour after reaching a terminal status.
    private RestartPolicy _restartPolicy = RestartPolicy.Auto;
    private Result _terminalResult = Result.Success;
    private int _executeGate;
    private readonly Func<string, CancellationToken, ValueTask> _cachedLogReportCallback;

    /// <summary>Public execution status (volatile for visibility).</summary>
    public ExecutionStatus Status => (ExecutionStatus)Volatile.Read(ref _statusInt);

    public AsyncStateMachine(Graph graph, IAsyncStateMachineObserver? observer = null)
    {
        Graph = graph;
        _observer = observer;
        _initial = graph.StartNode.Id;
        _current = _initial;
        _cachedLogReportCallback = LogReportCallback;
        // _statusInt already initialized to Created at field declaration; no transition needed.
    }

    /// <summary>
    /// Controls how the machine behaves after reaching a terminal status.
    /// <list type="bullet">
    /// <item><see cref="RestartPolicy.Auto"/>: automatically resets to Ready after completion/failure/cancellation.</item>
    /// <item><see cref="RestartPolicy.Manual"/>: requires explicit <see cref="Reset"/>; re-execution throws.</item>
    /// <item><see cref="RestartPolicy.Ignore"/>: ignores further execution attempts and returns the terminal result until <see cref="Reset"/> is called.</item>
    /// </list>
    /// </summary>
    public void SetRestartPolicy(RestartPolicy policy) => _restartPolicy = policy;

    /// <summary>
    /// Backwards-compatible switch for the legacy auto-reset behaviour.
    /// <para>Maps to <see cref="RestartPolicy.Auto"/> (true) and <see cref="RestartPolicy.Manual"/> (false).</para>
    /// </summary>
    public void SetAutoReset(bool enabled) => _restartPolicy = enabled ? RestartPolicy.Auto : RestartPolicy.Manual;

    /// <summary>
    /// Reset to the initial node. Disallowed while Running/Transitioning.
    /// Status moves: (any non-running) -> Resetting -> Ready.
    /// </summary>
    public async ValueTask<Result> Reset(CancellationToken ct = default)
    {
        while (true)
        {
            ExecutionStatus status = Status;
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

            // Own the transition to Resetting from whatever terminal state we observed.
            if (!TryTransition(status, ExecutionStatus.Resetting, out ExecutionStatus previous))
            {
                continue;
            }

            await NotifyMachineStatusChangeAsync(previous, ExecutionStatus.Resetting).ConfigureAwait(false);
            break; // we won; proceed
            // else: raced with another transition; retry
        }

        _current = _initial;

        await TransitionTo(ExecutionStatus.Ready).ConfigureAwait(false);
        return Result.Success;
    }

    protected override async ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1) // Prevent re-entrance
        {
            throw new InvalidOperationException("AsyncStateMachine is already executing.");
        }

        // If we're terminal, apply reset policy.
        ExecutionStatus status = Status;
        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            switch (_restartPolicy)
            {
                case RestartPolicy.Ignore:
                    // Ignore further execution attempts until a manual Reset() occurs.
                    // We must release the gate so the current ExecuteAsync call can run OnRunAsync,
                    // which will return the cached terminal result.
                    Volatile.Write(ref _executeGate, 0);
                    return Result.Continue;
                case RestartPolicy.Manual:
                    Volatile.Write(ref _executeGate, 0); // Release the gate before throwing.
                    throw new InvalidOperationException(
                        $"AsyncStateMachine is in terminal state '{status}'. Call Reset() before executing again.");
                default:
                    // Auto: tolerate unexpected terminal state by resetting on entry.
                    await Reset(ct).ConfigureAwait(false);
                    break;
            }
        }

        // First entry or re-entry after Ready: Starting -> (enter first node) -> Running
        await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
        _current = _initial;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
        }

        await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);
        
        return Result.Continue;
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        // Ignore policy: once terminal, do not re-run.
        ExecutionStatus status = Status;
        if (_restartPolicy == RestartPolicy.Ignore &&
            status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            return _terminalResult;
        }

        try
        {
            Result result = await InternalRunAsync(ct).ConfigureAwait(false);
            _terminalResult = result;

            // If we get here, loop has returned a terminal result already.
            // Machine completion notification + optional auto-reset-to-Ready.
            await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed)
                .ConfigureAwait(false);

            if (_restartPolicy == RestartPolicy.Auto)
            {
                await Reset(ct).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // TransitionTo(Cancelled) already fires OnStateMachineCancelled via NotifyMachineStatusChangeAsync.
            await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);

            _terminalResult = Result.Failure;

            if (_restartPolicy == RestartPolicy.Auto)
            {
                // Use default token — ct is already cancelled at this point.
                await Reset(ct).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex)
        {
            // Unexpected error outside node execution (node failures are returned as Failure below)
            if (_observer is not null)
                await _observer.OnStateFailed(_current, ex, ct).ConfigureAwait(false);

            // TransitionTo(Failed) already fires OnStateMachineCompleted(Failure) via NotifyMachineStatusChangeAsync.
            await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);

            _terminalResult = Result.Failure;

            if (_restartPolicy == RestartPolicy.Auto)
            {
                // Use default token — ct may be cancelled or faulted at this point.
                await Reset(ct).ConfigureAwait(false);
            }

            throw;
        }
    }

    protected override ValueTask<Result> OnExitAsync(CancellationToken ct)
    {
        // For Ignore policy, OnEnterAsync returns early with the gate held.
        // We want OnRunAsync to run and return the terminal result, and then
        // OnExitAsync will release the gate in the normal way.
        Volatile.Write(ref _executeGate, 0);
        return ResultHelpers.Success;
    }

    private async ValueTask<Result> InternalRunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Graph.TryGetNode(_current, out INode? node))
            {
                throw new InvalidOperationException($"Node '{_current}' not found.");
            }

            if (node is LogicNode logicNode)
            {
                ILogReporter? reporter = logicNode.AsyncLogic as ILogReporter ?? logicNode.Logic as ILogReporter;
                if (reporter is not null)
                {
                    //We need to capture the current node in the closure for correct attribution
                    reporter.LogReport = _cachedLogReportCallback;
                }
            }

            LogicNode logic = (LogicNode)node;
            Result result = await logic.AsyncLogic.ExecuteAsync(ct).ConfigureAwait(false);
            switch (result.Code)
            {
                case Result.StatusCode.Success:
                {
                    if (_observer is not null)
                    {
                        await _observer.OnStateExited(_current, ct).ConfigureAwait(false);
                    }

                    NodeId next;

                    if (logic.AsyncLogic is IAsyncDirector director)
                    {
                        next = await director.SelectNextAsync(ct).ConfigureAwait(false);
                        if (next.Equals(NodeId.Default))
                        {
                            return Result.Success;
                        }
                    }
                    else
                    {
                        if (!Graph.TryGetTransition(_current, out Transition edge))
                        {
                            if (_observer is not null)
                            {
                                await _observer.OnStateFailed(
                                    _current,
                                    new InvalidOperationException($"No transition found for state '{_current}'."),
                                    ct
                                ).ConfigureAwait(false);
                            }

                            return Result.Failure;
                        }

                        if (edge.IsEmpty)
                        {
                            return Result.Success; // terminal
                        }

                        next = edge.Destination;
                    }

                    await TransitionTo(ExecutionStatus.Transitioning).ConfigureAwait(false);

                    if (_observer is not null)
                    {
                        await _observer.OnTransition(_current, next, ct).ConfigureAwait(false);
                    }

                    _current = next;

                    if (_observer is not null)
                    {
                        await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
                    }

                    await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);

                    continue;
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

        ct.ThrowIfCancellationRequested();
        return Result.Failure;
    }

    private async ValueTask LogReportCallback(string message, CancellationToken ct)
    {
        if (_observer is not null)
        {
            await _observer.OnLogReport(_current, message, ct).ConfigureAwait(false);
        }
    }

    private bool TryTransition(ExecutionStatus from, ExecutionStatus to, out ExecutionStatus prev)
    {
        int f = (int)from, t = (int)to;
        int seen = Interlocked.CompareExchange(ref _statusInt, t, f);
        prev = (ExecutionStatus)seen;

        return seen == f;
    }

    /// <summary>
    /// Centralized status transition. Sets the status unconditionally and then
    /// kicks machine-lifecycle notifications outside the lock.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Interlocked.Exchange(ref int, int)"/> (unconditional) rather than CAS.
    /// Only call this when the caller owns the execution context (i.e. from the
    /// run-loop or enter/exit methods that are guarded by <see cref="_executeGate"/>).
    /// For external transitions that must validate the predecessor state, use
    /// <see cref="TryTransition"/> instead.
    /// </remarks>
    private async ValueTask TransitionTo(ExecutionStatus next)
    {
        ExecutionStatus prev = (ExecutionStatus)Interlocked.Exchange(ref _statusInt, (int)next);
        Debug.Assert(prev != next, $"Redundant status transition: {prev} -> {next}");
        if (prev != next && _observer is not null)
        {
            await NotifyMachineStatusChangeAsync(prev, next).ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyMachineStatusChangeAsync(ExecutionStatus prev, ExecutionStatus next)
    {
        if (_observer is null)
        {
            return;
        }

        NodeId graphId = Graph.Id;
        await _observer.StateMachineStatusChanged(graphId, prev, next).ConfigureAwait(false);
        switch (next)
        {
            case ExecutionStatus.Starting:
                await _observer.OnStateMachineStarted(graphId).ConfigureAwait(false);
                break;

            case ExecutionStatus.Completed:
                await _observer.OnStateMachineCompleted(graphId, Result.Success).ConfigureAwait(false);
                break;

            case ExecutionStatus.Failed:
                await _observer.OnStateMachineCompleted(graphId, Result.Failure).ConfigureAwait(false);
                break;

            case ExecutionStatus.Cancelled:
                await _observer.OnStateMachineCancelled(graphId).ConfigureAwait(false);
                break;

            case ExecutionStatus.Resetting:
                await _observer.OnStateMachineReset(graphId).ConfigureAwait(false);
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