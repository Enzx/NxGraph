using System.Runtime.CompilerServices;
using NxGraph.Diagnostics.Replay;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="AsyncState"/>.
/// All lifecycle methods are plain, non-virtual, zero-allocation calls.
/// No <c>CancellationToken</c>, no <c>ValueTask</c>, no threading primitives.
/// <para>
/// When placed in a <see cref="Graph"/>, the authoring layer wraps this in a
/// <see cref="SyncLogicAdapter"/> so that async runtimes can execute it via
/// <see cref="IAsyncLogic.ExecuteAsync"/> (zero-allocation on .NET 8+).
/// </para>
/// </summary>
public abstract class State : ILogic, ILogReporter
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

    private bool _hasEntered;

    /// <summary>
    /// Executes the state lifecycle: OnEnter once on first call, OnRun every call,
    /// OnExit once when a terminal result (non-Continue) is returned or an exception is thrown.
    /// </summary>
    public Result Execute()
    {
        if (!_hasEntered)
        {
            _hasEntered = true;
            try { OnEnter(); }
            catch { _hasEntered = false; throw; }
        }

        try
        {
            Result result = OnRun();
            if (result.IsCompleted)
            {
                _hasEntered = false;
                OnExit();
            }
            return result;
        }
        catch
        {
            _hasEntered = false;
            OnExit();
            throw;
        }
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
