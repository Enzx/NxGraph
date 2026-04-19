using NxGraph.Diagnostics.Replay;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Base class for all async states in a finite state machine.
/// </summary>
public abstract class AsyncState : IAsyncLogic, ILogReporter
{
    /// <summary>
    /// Executes the state asynchronously, entering and exiting the state as needed.
    /// </summary>
    /// <param name="ct">The cancellation token to observe while executing the state.</param>
    /// <returns>A <see cref="ValueTask{Result}"/> representing the<see cref="Result"/> of the state execution.</returns>
    public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        Result result = await OnEnterAsync(ct).ConfigureAwait(false);
        if (result.IsCompleted) return result;
        try
        {
            result = await OnRunAsync(ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            await OnExitAsync(ct).ConfigureAwait(false);
        }
    }

    public Func<string, CancellationToken, ValueTask>? LogReport { get; set; }

    protected async ValueTask LogAsync(string message, CancellationToken ct = default)
    {
        if (LogReport != null)
        {
            await LogReport(message, ct);
        }
    }

    protected virtual ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        return ResultHelpers.Continue;
    }

    protected virtual ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return ResultHelpers.Continue;
    }

    protected virtual ValueTask<Result> OnExitAsync(CancellationToken ct)
    {
        return ResultHelpers.Success;
    }
}

/// <summary>
/// Base class for async states that can be set with an agent.
/// </summary>
/// <typeparam name="TAgent">The type of the agent to be used in the state.</typeparam>
public abstract class AsyncState<TAgent> : AsyncState, IAgentSettable<TAgent>
{
    // ReSharper disable once NullableWarningSuppressionIsUsed
    protected TAgent Agent = default!;

    public void SetAgent(TAgent agent)
    {
        Agent = agent;
    }
}