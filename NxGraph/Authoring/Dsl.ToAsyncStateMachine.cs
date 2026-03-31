using NxGraph.Compatibility;
using NxGraph.Fsm;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into an async state machine.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="observer">An optional observer that can be used to monitor the state machine's execution.</param>
    /// <returns>An async state machine built from the previous state token.</returns>
    public static AsyncStateMachine ToAsyncStateMachine(this StateToken prev, IAsyncStateMachineObserver? observer = null)
    {
        return prev.Build().ToAsyncStateMachine(observer);
    }

    public static AsyncStateMachine<TAgent> ToAsyncStateMachine<TAgent>(this StateToken prev,
        IAsyncStateMachineObserver? observer = null)
    {
        return prev.Build().ToAsyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the start token into an async state machine.
    /// </summary>
    /// <param name="startToken">The start token, which is the entry point of the FSM graph.</param>
    /// <param name="observer">An optional observer that can be used to monitor the state machine's execution.</param>
    /// <returns>An async state machine built from the start token.</returns>
    public static AsyncStateMachine ToAsyncStateMachine(this StartToken startToken,
        IAsyncStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToAsyncStateMachine(observer);
    }

    /// <summary>
    /// Converts the start token into a typed async state machine.
    /// </summary>
    public static AsyncStateMachine<TAgent> ToAsyncStateMachine<TAgent>(this StartToken startToken,
        IAsyncStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToAsyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into an async state machine.
    /// </summary>
    public static AsyncStateMachine ToAsyncStateMachine(this BranchBuilder branch, IAsyncStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToAsyncStateMachine(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a typed async state machine.
    /// </summary>
    public static AsyncStateMachine<TAgent> ToAsyncStateMachine<TAgent>(this BranchBuilder branch,
        IAsyncStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToAsyncStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Fluently injects an agent into a typed async state machine.
    /// </summary>
    public static AsyncStateMachine<TAgent> WithAgent<TAgent>(this AsyncStateMachine<TAgent> fsm,
        TAgent agent)
    {
        Guard.NotNull(agent, nameof(agent));
        fsm.SetAgent(agent);
        return fsm;
    }
}