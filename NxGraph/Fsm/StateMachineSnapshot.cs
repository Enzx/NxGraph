namespace NxGraph.Fsm;

/// <summary>
/// A serialization-friendly capture of a state machine's runtime position — current node,
/// status, retry progress, and terminal outcome. Produced by <c>Suspend()</c> and consumed
/// by <c>Resume(...)</c> on <see cref="Async.AsyncStateMachine"/> or <see cref="StateMachine"/>
/// built over an equivalent graph (same node indices — e.g. one round-tripped through
/// NxGraph.Serialization). Snapshots are interchangeable between the two runtimes as long as
/// the graph's nodes are executable by the target runtime.
/// <para>
/// The snapshot deliberately contains only primitives, so it serializes cleanly with any
/// serializer. The user context (agent/blackboard) is owned by the caller and must be
/// re-attached via <c>SetAgent</c> after resuming.
/// </para>
/// </summary>
public sealed record StateMachineSnapshot(
    int CurrentNodeIndex,
    ExecutionStatus Status,
    int Attempts,
    bool NodeEntered,
    bool MidRun,
    int LastOutcome);
