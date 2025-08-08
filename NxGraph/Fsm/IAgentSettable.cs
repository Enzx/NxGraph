namespace NxGraph.Fsm;

/// <summary>
/// An interface for types that can set an agent.
/// The agent is typically used to provide context or state for the execution of the instance.
/// </summary>
/// <typeparam name="TAgent">The type of the agent to set.</typeparam>
public interface IAgentSettable<in TAgent>
{
    /// <summary>
    /// Sets the agent for this instance.
    /// </summary>
    /// <param name="agent">The agent to set.</param>
    void SetAgent(TAgent agent);
}