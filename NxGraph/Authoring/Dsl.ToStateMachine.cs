using System.Diagnostics.CodeAnalysis;
using NxGraph.Fsm;

namespace NxGraph.Authoring;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into a state machine.
    /// </summary>
    /// <param name="prev">The previous state token, which is the source of the transition.</param>
    /// <param name="observer">An optional observer that can be used to monitor the state machine's execution.</param>
    /// <returns>A state machine built from the previous state token.</returns>
    public static StateMachine ToStateMachine(this StateToken prev, IAsyncStateObserver? observer = null)
    {
        return prev.Build().ToStateMachine(observer);
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StateToken prev,
        IAsyncStateObserver? observer = null)
    {
        return prev.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    ///  Converts the start token into a state machine.
    /// </summary>
    /// <param name="startToken">The start token, which is the entry point of the FSM graph.</param>
    /// <returns>A state machine built from the start token.</returns>
    public static StateMachine ToStateMachine(this StartToken startToken)
    {
        ArgumentNullException.ThrowIfNull(startToken);
        return startToken.Builder.Build().ToStateMachine();
    }

    /// <summary>
    /// Converts the terminal builder into a state machine.
    /// </summary>
    /// <param name="ifBuilder">The terminal builder, which is the final state of the FSM graph.</param>
    /// <returns>A state machine built from the terminal builder.</returns>
    public static StateMachine ToStateMachine(this TerminalBuilder ifBuilder)
    {
        ArgumentNullException.ThrowIfNull(ifBuilder);
        return ifBuilder.Builder.Build().ToStateMachine();
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this TerminalBuilder ifBuilder)
    {
        ArgumentNullException.ThrowIfNull(ifBuilder);
        return ifBuilder.Builder.Build().ToStateMachine<TAgent>();
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StartToken startToken,
        IAsyncStateObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(startToken);
        return startToken.Builder.Build().ToStateMachine<TAgent>(observer);
    }
    
    public static StateMachine ToStateMachine(this BranchBuilder branch, IAsyncStateObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.Builder.Build().ToStateMachine(observer);
    }

    public static StateMachine<TAgent> ToStateMachine<TAgent>(this BranchBuilder branch,
        IAsyncStateObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.Builder.Build().ToStateMachine<TAgent>(observer);
    }
    public static StateMachine<TAgent> WithAgent<TAgent>(this StateMachine<TAgent> fsm,
        TAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        fsm.SetAgent(agent);
        return fsm;
    }
}