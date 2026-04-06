namespace NxGraph.Fsm;

/// <summary>
/// Controls what a state machine should do after it reaches a terminal status
/// (<see cref="ExecutionStatus.Completed"/>, <see cref="ExecutionStatus.Failed"/>, or
/// <see cref="ExecutionStatus.Cancelled"/>).
/// </summary>
/// <remarks>
/// This policy governs behaviour on subsequent <c>Execute()</c>/<c>ExecuteAsync()</c> calls
/// once the machine is terminal:
/// <list type="bullet">
/// <item><see cref="Auto"/>: automatically resets the machine to <see cref="ExecutionStatus.Ready"/>.</item>
/// <item><see cref="Manual"/>: requires an explicit reset; re-execution throws until reset.</item>
/// <item><see cref="Ignore"/>: re-execution is silently ignored and the last terminal result is returned until reset.</item>
/// </list>
/// </remarks>
public enum RestartPolicy
{
    /// <summary>
    /// Automatically reset to <see cref="ExecutionStatus.Ready"/> after the machine completes/fails/cancels.
    /// </summary>
    Auto,

    /// <summary>
    /// Stay terminal after completion/failure/cancellation. The caller must explicitly reset the machine.
    /// Re-execution without reset is considered an error.
    /// </summary>
    Manual,

    /// <summary>
    /// Stay terminal after completion/failure/cancellation. Subsequent execution attempts are no-ops:
    /// the machine does not run any nodes and simply returns the last terminal result until reset.
    /// </summary>
    Ignore
}