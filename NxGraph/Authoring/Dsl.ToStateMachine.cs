using NxGraph.Fsm;
#if NETSTANDARD2_1
using ArgumentNullException = System.ArgumentNullExceptionShim;
#endif

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into a state machine.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="observer">An optional observer that can be used to monitor the state machine's execution.</param>
    /// <returns>A state machine built from the previous state token.</returns>
    public static StateMachine ToStateMachine(this StateToken prev, IAsyncStateMachineObserver? observer = null)
    {
        return prev.Build().ToStateMachine(observer);
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StateToken prev,
        IAsyncStateMachineObserver? observer = null)
    {
        return prev.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the start token into a state machine.
    /// </summary>
    /// <param name="startToken">The start token, which is the entry point of the FSM graph.</param>
    /// <param name="observer">An optional observer that can be used to monitor the state machine's execution.</param>
    /// <returns>A state machine built from the start token.</returns>
    public static StateMachine ToStateMachine(this StartToken startToken,
        IAsyncStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToStateMachine(observer);
    }

    /// <summary>
    /// Converts the start token into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StartToken startToken,
        IAsyncStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a state machine.
    /// </summary>
    public static StateMachine ToStateMachine(this BranchBuilder branch, IAsyncStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToStateMachine(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this BranchBuilder branch,
        IAsyncStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Fluently injects an agent into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> WithAgent<TAgent>(this StateMachine<TAgent> fsm,
        TAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        fsm.SetAgent(agent);
        return fsm;
    }
}