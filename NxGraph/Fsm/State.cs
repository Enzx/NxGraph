using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Base class for all states in a finite state machine.
/// </summary>
public abstract class State : INode
{
    public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        await OnEnterAsync(ct).ConfigureAwait(false);
        Result result = await OnRunAsync(ct).ConfigureAwait(false);
        await OnExitAsync(ct).ConfigureAwait(false);
        return result;
    }

    protected virtual ValueTask OnEnterAsync(CancellationToken ct) => default;
    protected abstract ValueTask<Result> OnRunAsync(CancellationToken ct);
    protected virtual ValueTask OnExitAsync (CancellationToken ct) => default;
}

/// <summary>
/// Base class for states that can be set with an agent.
/// </summary>
/// <typeparam name="TAgent">The type of the agent to be used in the state.</typeparam>
public abstract class State<TAgent> : State, IAgentSettable<TAgent>
{
    // ReSharper disable once NullableWarningSuppressionIsUsed
    protected TAgent Agent = default!;
    public void SetAgent(TAgent agent) => Agent = agent;
}