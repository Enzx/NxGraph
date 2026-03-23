using System.Runtime.CompilerServices;
using NxGraph.Diagnostics.Replay;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="AsyncState"/>.
/// All lifecycle methods are plain, non-virtual, zero-allocation calls.
/// No <c>CancellationToken</c>, no <c>ValueTask</c>, no threading primitives.
/// <para>
/// Also implements <see cref="IAsyncLogic"/> so the same <see cref="Graph"/> can be used by
/// both <see cref="AsyncStateMachine"/> (async) and <see cref="StateMachine"/> (sync).
/// The <see cref="IAsyncLogic.ExecuteAsync"/> path wraps the synchronous result in a
/// completed <see cref="ValueTask{Result}"/> (zero-allocation on .NET 8+).
/// </para>
/// </summary>
public abstract class State : ILogic, IAsyncLogic, ILogReporter
{
    /// <summary>
    /// Callback set by the sync runtime so the state can emit log messages.
    /// Uses <see cref="Action{String}"/> instead of an async delegate.
    /// </summary>
    public Action<string>? SyncLogReport { get; set; }

    /// <summary>
    /// Async log report callback (used when the state is executed via the async <see cref="AsyncStateMachine"/>).
    /// </summary>
    Func<string, CancellationToken, ValueTask>? ILogReporter.LogReport { get; set; }

    /// <summary>
    /// Executes the full enter → run → exit lifecycle synchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    /// <inheritdoc />
    /// <remarks>
    /// Bridges the sync execution into the async <see cref="IAsyncLogic"/> contract.
    /// Returns a completed <see cref="ValueTask{Result}"/> — zero-allocation.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Result> IAsyncLogic.ExecuteAsync(CancellationToken ct)
    {
        return new ValueTask<Result>(Execute());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void Log(string message)
    {
        SyncLogReport?.Invoke(message);
    }

    protected virtual void OnEnter() { }

    protected abstract Result OnRun();

    protected virtual void OnExit() { }
}

/// <summary>
/// Synchronous counterpart of <see cref="AsyncState{TAgent}"/>.
/// </summary>
/// <typeparam name="TAgent">The type of agent available during execution.</typeparam>
public abstract class State<TAgent> : State, IAgentSettable<TAgent>
{
    // ReSharper disable once NullableWarningSuppressionIsUsed
    protected TAgent Agent = default!;

    public void SetAgent(TAgent agent)
    {
        Agent = agent;
    }
}
