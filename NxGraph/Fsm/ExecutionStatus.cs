namespace NxGraph.Fsm;

/// <summary>
/// Execution status for a running entity (state or state machine).
/// </summary>
public enum ExecutionStatus : byte
{
    /// <summary>
    /// Initial state when the entity is created but not yet run.
    /// </summary>
    Created, // never run; equivalent to Ready, but BEFORE the first run
    /// <summary>
    /// Entity is ready to run, having been reset or initialized.
    /// </summary>
    Starting, // OnEnterAsync is executing
    /// <summary>
    /// Entity is currently running its main logic in the OnRunAsync loop.
    /// </summary>
    Running, // executing OnRunAsync loop
    /// <summary>
    /// Entity is transitioning between nodes, typically during an atomic hop in a state machine.
    /// </summary>
    Transitioning, // between nodes (atomic hop)
    /// <summary>
    /// Entity has completed its execution successfully, reaching a terminal state.
    /// </summary>
    Completed, // terminal success
    /// <summary>
    /// Entity has encountered a failure during execution, reaching a terminal state.
    /// </summary>
    Failed, // terminal failure
    /// <summary>
    /// Entity has been externally cancelled, reaching a terminal state.
    /// </summary>
    Cancelled, // terminal external cancellation
    /// <summary>
    /// Entity is in the process of being reset, typically after a run or when explicitly reset.
    /// </summary>
    Resetting, // reset in progress (explicit)
    /// <summary>
    /// Entity has been reset and is ready to run again, equivalent to Created but after the first run.
    /// </summary>
    Ready // reset done; equivalent to Created, but AFTER the first run
}