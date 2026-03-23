using NxGraph.Fsm;
#if NETSTANDARD2_1
using ArgumentNullException = System.ArgumentNullExceptionShim;
#endif

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into a synchronous state machine.
    /// </summary>
    public static SyncStateMachine ToSyncStateMachine(this StateToken prev,
        ISyncStateMachineObserver? observer = null)
    {
        return prev.Build().ToSyncStateMachine(observer);
    }

    /// <summary>
    /// Converts the previous state token into a typed synchronous state machine.
    /// </summary>
    public static SyncStateMachine<TAgent> ToSyncStateMachine<TAgent>(this StateToken prev,
        ISyncStateMachineObserver? observer = null)
    {
        return prev.Build().ToSyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the start token into a synchronous state machine.
    /// </summary>
    public static SyncStateMachine ToSyncStateMachine(this StartToken startToken,
        ISyncStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToSyncStateMachine(observer);
    }

    /// <summary>
    /// Converts the start token into a typed synchronous state machine.
    /// </summary>
    public static SyncStateMachine<TAgent> ToSyncStateMachine<TAgent>(this StartToken startToken,
        ISyncStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToSyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a synchronous state machine.
    /// </summary>
    public static SyncStateMachine ToSyncStateMachine(this BranchBuilder branch,
        ISyncStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToSyncStateMachine(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a typed synchronous state machine.
    /// </summary>
    public static SyncStateMachine<TAgent> ToSyncStateMachine<TAgent>(this BranchBuilder branch,
        ISyncStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToSyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the "else" branch end into a synchronous state machine.
    /// </summary>
    public static SyncStateMachine ToSyncStateMachine(this BranchEnd branchEnd,
        ISyncStateMachineObserver? observer = null)
    {
        return branchEnd.Builder.Build().ToSyncStateMachine(observer);
    }

    /// <summary>
    /// Converts the "else" branch end into a typed synchronous state machine.
    /// </summary>
    public static SyncStateMachine<TAgent> ToSyncStateMachine<TAgent>(this BranchEnd branchEnd,
        ISyncStateMachineObserver? observer = null)
    {
        return branchEnd.Builder.Build().ToSyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Fluently injects an agent into a typed synchronous state machine.
    /// </summary>
    public static SyncStateMachine<TAgent> WithAgent<TAgent>(this SyncStateMachine<TAgent> fsm,
        TAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        fsm.SetAgent(agent);
        return fsm;
    }
}

