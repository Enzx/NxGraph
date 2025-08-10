using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// A state machine that executes a graph of nodes.
/// </summary>
/// <param name="graph">The graph containing the nodes and transitions.</param>
/// <param name="observer">An optional observer to monitor state transitions and executions.</param>
/// <typeparam name="TAgent">The type of the agent to be used in the state machine.</typeparam>
public class StateMachine<TAgent>(Graph graph, IAsyncStateObserver? observer = null)
    : StateMachine(graph, observer), IAgentSettable<TAgent>
{
    public void SetAgent(TAgent agent)
    {
        Graph.SetAgent(agent);
    }
}

/// <summary>
/// A state machine that executes a graph of nodes.
/// </summary>
public class StateMachine : State
{
    protected readonly IGraph Graph;
    private readonly NodeId _initialState;
    private readonly IAsyncStateObserver? _observer;
    private NodeId _currentState;
    private volatile ExecutionStatus _status = ExecutionStatus.Created;
    private readonly object _lifecycleLock = new();

    /// <summary>
    /// Public execution status of this state machine.
    /// </summary>
    public ExecutionStatus Status => _status;


    /// <summary>
    /// Try to transition to Running. Throws if already Running.
    /// </summary>
    private void EnterRunningOrThrow()
    {
        lock (_lifecycleLock)
        {
            if (_status == ExecutionStatus.Running)
            {
                throw new InvalidOperationException("StateMachine is already running.");
            }

            _status = ExecutionStatus.Running;
        }
    }

    private void TransitionTo(ExecutionStatus next)
    {
        lock (_lifecycleLock)
        {
            _status = next;
        }
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="StateMachine"/> class.
    /// </summary>
    /// <param name="graph">The graph <see cref="Graph"/> containing
    /// the nodes <see cref="Node"/>
    /// and transitions <see cref="Transition"/>.</param>
    /// <param name="observer">An optional observer to monitor state transitions and executions.</param>
    public StateMachine(IGraph graph, IAsyncStateObserver? observer = null)
    {
        Graph = graph;
        _initialState = graph.StartNode.Id;
        _currentState = _initialState;
        _observer = observer;
    }

    protected override async ValueTask OnEnterAsync(CancellationToken ct)
    {
        _currentState = _initialState;
        if (_observer != null)
        {
            await _observer.OnStateEntered(_currentState, ct).ConfigureAwait(false);
        }
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        EnterRunningOrThrow();
        try
        {
            Result result = await InternalRunAsync(ct).ConfigureAwait(false);
            TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed);
            return result;
        }
        catch (OperationCanceledException ex)
        {
            if (_observer is not null)
            {
                await _observer.OnStateFailed(_currentState, ex, ct).ConfigureAwait(false);
            }

            TransitionTo(ExecutionStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            if (_observer is not null)
            {
                await _observer.OnStateFailed(_currentState, ex, ct).ConfigureAwait(false);
            }

            TransitionTo(ExecutionStatus.Failed);
            throw;
        }
    }

    private async ValueTask<Result> InternalRunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Graph.TryGetNode(_currentState, out Node? node))
            {
                throw new InvalidOperationException($"Node {_currentState} not found");
            }

            Result result = await node.Logic.ExecuteAsync(ct).ConfigureAwait(false);

            switch (result)
            {
                case Result.Success:
                {
                    if (_observer is not null)
                    {
                        await _observer.OnStateExited(_currentState, ct).ConfigureAwait(false);
                    }

                    NodeId next;

                    if (node.Logic is IDirector director)
                    {
                        next = director.SelectNext();

                        if (next.Equals(NodeId.Default))
                        {
                            return Result.Success;
                        }
                    }
                    else
                    {
                        if (!Graph.TryGetTransition(_currentState, out Transition edge))
                        {
                            if (_observer is not null)
                            {
                                await _observer.OnStateFailed(_currentState,
                                        new InvalidOperationException($"No transition found for state {_currentState}"),
                                        ct)
                                    .ConfigureAwait(false);
                            }

                            return Result.Failure;
                        }

                        if (edge.IsEmpty)
                        {
                            return Result.Success;
                        }

                        next = edge.Destination;
                    }

                    if (_observer is not null)
                    {
                        await _observer.OnTransition(_currentState, next, ct).ConfigureAwait(false);
                    }

                    _currentState = next;

                    if (_observer is not null)
                    {
                        await _observer.OnStateEntered(_currentState, ct).ConfigureAwait(false);
                    }

                    continue;
                }
                case Result.Failure:
                    return Result.Failure;
                default:
                    throw new InvalidOperationException("Unknown result");
            }
        }

        ct.ThrowIfCancellationRequested();
        return Result.Failure;
    }
}