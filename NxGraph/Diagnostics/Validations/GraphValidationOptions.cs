using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Validations;

public sealed class GraphValidationOptions(IReadOnlyList<NodeId>? allNodes = null)
{
    /// <summary>
    /// Optional: supply the full set of nodes known to the graph (e.g., from GraphBuilder) to enable unreachable/duplicate-name checks.
    /// </summary>
    public IReadOnlyList<NodeId>? AllNodes { get; init; } = allNodes;

    /// <summary>
    /// Treat self-loop transitions as warnings (true) or ignore (false).
    /// </summary>
    public bool WarnOnSelfLoop { get; init; } = true;

    /// <summary>
    /// If true, emit an Error when no terminal path exists from Start; otherwise emit a Warning.
    /// </summary>
    public bool StrictNoTerminalPath { get; init; }
}