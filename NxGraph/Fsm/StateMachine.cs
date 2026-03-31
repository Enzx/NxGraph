using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// A synchronous state machine with an agent, mirroring <see cref="AStateMachine{TAgent}"/>.
/// </summary>
public class StateMachine<TAgent>(Graph graph, IStateMachineObserver? observer = null)
    : StateMachine(graph, observer), IAgentSettable<TAgent>
{
    public void SetAgent(TAgent agent) => Graph.SetAgent(agent);
}

/// <summary>
/// Synchronous, zero-allocation counterpart of <see cref="AStateMachine"/>.
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
/// Node logic is executed via <see cref="ILogic.Execute"/> — every node in the graph
/// must have its <see cref="INode.Logic"/> populated (i.e. implement <see cref="ILogic"/>).
/// </summary>
public class StateMachine
{
    public readonly Graph Graph;
    private readonly IStateMachineObserver? _observer;

    private ExecutionStatus _status = ExecutionStatus.Created;

    private NodeId _current;
    private readonly NodeId _initial;

    private bool _autoReset = true;
    private bool _executeGate;
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

    /// <summary>Enable/disable auto-reset to Ready after a terminal state.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAutoReset(bool enabled)
    {
        _autoReset = enabled;
    }

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

    /// <summary>
    /// Executes the full state machine synchronously: enter → run loop → exit.
    /// </summary>
    public Result Execute()
    {
        OnEnter();
        try
        {
            return OnRun();
        }
        finally
        {
            OnExit();
        }
    }

    private void OnEnter()
    {
        if (_executeGate)
        {
            throw new InvalidOperationException("StateMachine is already executing.");
        }

        _executeGate = true;

        TransitionTo(ExecutionStatus.Starting);
        _current = _initial;

        _observer?.OnStateEntered(_current);

        TransitionTo(ExecutionStatus.Running);
    }

    private Result OnRun()
    {
        try
        {
            Result result = InternalRun();

            TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed);

            if (_autoReset)
            {
                Reset();
            }

            return result;
        }
        catch (Exception ex)
        {
            _observer?.OnStateFailed(_current, ex);

            TransitionTo(ExecutionStatus.Failed);

            if (_autoReset)
            {
                Reset();
            }

            throw;
        }
    }

    private void OnExit()
    {
        _executeGate = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Result InternalRun()
    {
        while (true)
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

            switch (result)
            {
                case Result.Success:
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

                    continue;
                }
                case Result.Failure:
                    return Result.Failure;
                default:
                    throw new InvalidOperationException("Unknown node result.");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogReportCallback(string message)
    {
        _observer?.OnLogReport(_current, message);
    }

    /// <summary>
    /// Plain field write + observer notification. No Interlocked, no Volatile.
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

