namespace NxGraph.Fsm;

/// <summary>
/// How the sync <see cref="ParallelState"/> spreads its region rounds across
/// <see cref="Graphs.ILogic.Execute"/> calls (ticks).
/// </summary>
public enum ParallelStepMode
{
    /// <summary>
    /// Loop rounds inside a single <c>Execute()</c> call until every region reaches a
    /// terminal result — the whole join costs one tick (the caller opts into the work).
    /// Mirrors <see cref="Async.AsyncParallelState"/>, which always runs to the join.
    /// </summary>
    RunToJoin = 0,

    /// <summary>
    /// Advance every still-running region by exactly one node, then return
    /// <see cref="Result.InProgress"/> ("re-run me next tick"). Rounds align 1:1 with
    /// game-loop frames; the join result is returned on the tick the last region
    /// terminates. Only meaningful under the sync <see cref="StateMachine"/> — the async
    /// runtime rejects node-level <see cref="Result.InProgress"/>.
    /// </summary>
    RoundPerTick = 1,
}
