using NxGraph.Diagnostics.Replay;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// A state machine that executes a graph of nodes with full lifecycle/observer support.
/// </summary>
public class StateMachine<TAgent>(Graph graph, IAsyncStateMachineObserver? observer = null)
    : StateMachine(graph, observer), IAgentSettable<TAgent>
{
    public void SetAgent(TAgent agent) => Graph.SetAgent(agent);
}

/// <summary>
/// Non-generic StateMachine.
/// </summary>
public class StateMachine : State
{
    public readonly Graph Graph;
    private readonly IAsyncStateMachineObserver? _observer;

    private int _statusInt = (int)ExecutionStatus.Created; // atomic state

    private NodeId _current;
    private readonly NodeId _initial;

    private bool _autoReset = true;
    private int _executeGate;
    private readonly Func<string, CancellationToken, ValueTask> _cachedLogReportCallback;

    /// <summary>Public execution status (volatile for visibility).</summary>
    public ExecutionStatus Status => (ExecutionStatus)Volatile.Read(ref _statusInt);

    public StateMachine(Graph graph, IAsyncStateMachineObserver? observer = null)
    {
        Graph = graph;
        _observer = observer;
        _initial = graph.StartNode.Id;
        _current = _initial;
        _cachedLogReportCallback = LogReportCallback;
        ValueTask task = TransitionTo(ExecutionStatus.Created);
        if (!task.IsCompletedSuccessfully)
        {
            task.AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Enable/disable auto-reset to Ready after a terminal state.</summary>
    public void SetAutoReset(bool enabled)
    {
        Volatile.Write(ref _autoReset, enabled);
    }

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

    protected override async ValueTask OnEnterAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1) // Prevent re-entrance
        {
            throw new InvalidOperationException("StateMachine is already executing.");
        }

        // First entry or re-entry after Ready: Starting -> (enter first node) -> Running
        await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
        _current = _initial;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
        }

        await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        try
        {
            Result result = await InternalRunAsync(ct).ConfigureAwait(false);

            // If we get here, loop has returned a terminal result already.
            // Machine completion notification + optional auto-reset-to-Ready.
            await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed)
                .ConfigureAwait(false);

            if (Volatile.Read(ref _autoReset))
            {
                await Reset(ct).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);

            if (_observer is not null)
                await _observer.OnStateMachineCancelled(Graph.Id, ct).ConfigureAwait(false);

            if (Volatile.Read(ref _autoReset))
            {
                await Reset(ct).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex)
        {
            // Unexpected error outside node execution (node failures are returned as Failure below)
            if (_observer is not null)
                await _observer.OnStateFailed(_current, ex, ct).ConfigureAwait(false);

            await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);

            if (_observer is not null)
                await _observer.OnStateMachineCompleted(Graph.Id, Result.Failure, ct).ConfigureAwait(false);

            if (Volatile.Read(ref _autoReset))
            {
                await Reset(ct).ConfigureAwait(false);
            }

            throw;
        }
    }

    protected override ValueTask OnExitAsync(CancellationToken ct)
    {
        Volatile.Write(ref _executeGate, 0);
        return ValueTask.CompletedTask;
    }

    private async ValueTask<Result> InternalRunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Graph.TryGetNode(_current, out INode? node))
            {
                throw new InvalidOperationException($"Node '{_current}' not found.");
            }

            if (node is LogicNode { Logic: ILogReporter reporter })
            {
                //We need to capture the current node in the closure for correct attribution
                reporter.LogReport = _cachedLogReportCallback;
            }

            LogicNode logicLogicNode = (LogicNode)node;
            Result result = await logicLogicNode.Logic.ExecuteAsync(ct).ConfigureAwait(false);
            switch (result)
            {
                case Result.Success:
                {
                    if (_observer is not null)
                    {
                        await _observer.OnStateExited(_current, ct).ConfigureAwait(false);
                    }

                    NodeId next;

                    if (logicLogicNode.Logic is IDirector director)
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
                case Result.Failure:
                    return Result.Failure;
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
    /// Centralized status transition. Sets the status under lock and then
    /// kicks machine-lifecycle notifications outside the lock.
    /// </summary>
    private async ValueTask TransitionTo(ExecutionStatus next)
    {
        ExecutionStatus prev = (ExecutionStatus)Interlocked.Exchange(ref _statusInt, (int)next);
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