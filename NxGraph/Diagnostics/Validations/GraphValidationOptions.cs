using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Validations;

public sealed class GraphValidationOptions(IReadOnlyList<NodeId>? allNodes = null)
{
    /// <summary>
    /// Optional override for the node set used by the unreachable/duplicate-name checks.
    /// When absent (the default), the validator derives the full set from the graph itself,
    /// so a standalone <c>Validate()</c> is complete. Supply a list only to validate against
    /// a different set — e.g. a pre-build ID list from <c>GraphBuilder.GetAllNodeIds()</c>.
    /// </summary>
    public IReadOnlyList<NodeId>? AllNodes { get; set; } = allNodes;

    /// <summary>
    /// Treat self-loop transitions as warnings (true) or ignore (false).
    /// </summary>
    public bool WarnOnSelfLoop { get; set; } = true;

    /// <summary>
    /// If true, emit an Error when no terminal path exists from Start; otherwise emit a Warning.
    /// </summary>
    public bool StrictNoTerminalPath { get; set; }

    /// <summary>
    /// If true, emit an Error when any node does not implement <see cref="ILogic"/> (sync-only graphs).
    /// Use this to validate that a graph can be executed by the synchronous <c>StateMachine</c> runtime.
    /// </summary>
    public bool StrictSyncOnly { get; set; }

    /// <summary>
    /// If true, emit an Error when a reachable node holds a sync composite configured with
    /// <c>ParallelStepMode.RoundPerTick</c> (including a nested sync <c>StateMachine</c> in its
    /// default RoundPerTick step mode). Such nodes return node-level <c>Result.InProgress</c>,
    /// which the async runtime rejects mid-run — use this to validate a graph destined for
    /// the <c>AsyncStateMachine</c>.
    /// </summary>
    public bool StrictAsyncCompatible { get; set; }
}