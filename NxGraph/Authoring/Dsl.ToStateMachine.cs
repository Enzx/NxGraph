using NxGraph.Compatibility;
using NxGraph.Fsm;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Converts the previous state token into a state machine.
    /// </summary>
    public static StateMachine ToStateMachine(this StateToken prev,
        IStateMachineObserver? observer = null)
    {
        return prev.Build().ToStateMachine(observer);
    }

    /// <summary>
    /// Converts the previous state token into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StateToken prev,
        IStateMachineObserver? observer = null)
    {
        return prev.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the start token into a state machine.
    /// </summary>
    public static StateMachine ToStateMachine(this StartToken startToken,
        IStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToStateMachine(observer);
    }

    /// <summary>
    /// Converts the start token into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this StartToken startToken,
        IStateMachineObserver? observer = null)
    {
        return startToken.Builder.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a state machine.
    /// </summary>
    public static StateMachine ToStateMachine(this BranchBuilder branch,
        IStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToStateMachine(observer);
    }

    /// <summary>
    /// Converts the "then" branch builder into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this BranchBuilder branch,
        IStateMachineObserver? observer = null)
    {
        return branch.Builder.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Converts the "else" branch end into a state machine.
    /// </summary>
    public static StateMachine ToStateMachine(this BranchEnd branchEnd,
        IStateMachineObserver? observer = null)
    {
        return branchEnd.Builder.Build().ToStateMachine(observer);
    }

    /// <summary>
    /// Converts the "else" branch end into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> ToStateMachine<TAgent>(this BranchEnd branchEnd,
        IStateMachineObserver? observer = null)
    {
        return branchEnd.Builder.Build().ToStateMachine<TAgent>(observer);
    }

    /// <summary>
    /// Fluently injects an agent into a typed state machine.
    /// </summary>
    public static StateMachine<TAgent> WithAgent<TAgent>(this StateMachine<TAgent> fsm,
        TAgent agent)
    {
        Guard.NotNull(agent, nameof(agent));
        fsm.SetAgent(agent);
        return fsm;
    }
}

