// NxGraph/Fsm/ExecutionStatus.cs
namespace NxGraph.Fsm
{
    /// <summary>
    /// Execution status for a running entity (state or state machine).
    /// </summary>
    public enum ExecutionStatus
    {
        Created = 0, 
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4,
        Transitioning = 5
    }
}
