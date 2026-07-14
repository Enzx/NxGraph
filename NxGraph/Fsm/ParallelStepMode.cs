namespace NxGraph.Fsm;

/// <summary>
/// How a sync composite spreads its child work across <see cref="Graphs.ILogic.Execute"/>
/// calls (ticks). Shared by <see cref="ParallelState"/>/<see cref="DynamicParallelState"/>
/// (a round = one node per still-running region), <see cref="HistoryState"/> and a nested
/// <see cref="StateMachine"/>'s own <see cref="StateMachine.StepMode"/> (a round = one node
/// of the child machine).
/// </summary>
public enum ParallelStepMode
{
    /// <summary>
    /// Loop rounds inside a single <c>Execute()</c> call until the composite reaches a
    /// terminal result — the whole join costs one tick (the caller opts into the work; a
    /// child node that keeps returning <see cref="Result.InProgress"/>, e.g. a multi-tick
    /// wait, busy-spins for the duration). Mirrors the async composites, which always run
    /// to the join, so <c>RunToJoin</c> composites are executable from <b>both</b> runtimes
    /// via the sync-logic adapter.
    /// </summary>
    RunToJoin = 0,

    /// <summary>
    /// Advance one round per call, then return <see cref="Result.InProgress"/> ("re-run me
    /// next tick"). Rounds align 1:1 with game-loop frames; the terminal result is returned
    /// on the tick the composite finishes. Only meaningful under the sync
    /// <see cref="StateMachine"/> — the async runtime rejects node-level
    /// <see cref="Result.InProgress"/>.
    /// </summary>
    RoundPerTick = 1,
}
