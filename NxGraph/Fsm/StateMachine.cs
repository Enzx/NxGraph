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
    protected readonly Graph Graph;
    private readonly NodeId _initialState;
    private readonly IAsyncStateObserver? _observer;
    private NodeId _currentState;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateMachine"/> class.
    /// </summary>
    /// <param name="graph">The graph <see cref="Graph"/> containing
    /// the nodes <see cref="Node"/>
    /// and transitions <see cref="Transition"/>.</param>
    /// <param name="observer">An optional observer to monitor state transitions and executions.</param>
    public StateMachine(Graph graph, IAsyncStateObserver? observer = null)
    {
        Graph = graph;
        _initialState = graph.StartNode.Key;
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
        while (!ct.IsCancellationRequested)
        {
            if (!Graph.Nodes.TryGetValue(_currentState, out Node? node))
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
                            return Result.Success;
                    }
                    else
                    {
                        Transition edge = Graph.GetTransition(_currentState);
                        if (edge.IsEmpty) return Result.Success;

                        next = edge.Destination;
                    }

                    if (_observer is not null)
                        await _observer.OnTransition(_currentState, next, ct).ConfigureAwait(false);

                    _currentState = next;

                    if (_observer is not null)
                        await _observer.OnStateEntered(_currentState, ct).ConfigureAwait(false);
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