namespace NxGraph.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Graphs;

public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record GraphDiagnostic(Severity Severity, string Message, NodeId Node)
{
    public override string ToString() => $"[{Severity}] {Node}: {Message}";
}

public sealed class GraphValidationResult
{
    private readonly List<GraphDiagnostic> _list = [];
    public IReadOnlyList<GraphDiagnostic> Diagnostics => _list;
    public bool HasErrors => _list.Any(d => d.Severity == Severity.Error);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(Severity s, string msg, NodeId node) => _list.Add(new GraphDiagnostic(s, msg, node));

    public override string ToString() => string.Join(Environment.NewLine, _list.Select(x => x.ToString()));
}

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
    public bool StrictNoTerminalPath { get; init; } = false;
}

public static class GraphValidator
{
    
    /// <summary>
    /// Validate a graph: reachability (if AllNodes provided), broken transitions, self-loops, and terminal-path existence.
    /// Runs in O(N) over reachable nodes and does not allocate on the steady-state hot path.
    /// </summary>
    public static GraphValidationResult Validate(this Graph graph, GraphValidationOptions? options = null)
    {
        //TODO: add separate validation class for validation rules and allow custom rules to be registered

        ArgumentNullException.ThrowIfNull(graph);
        options ??= new GraphValidationOptions();

        GraphValidationResult result = new();

        // 1) Start node must exist
        NodeId start = graph.StartNode.Id;
        if (!graph.TryGetNode(start, out _))
        {
            result.Add(Severity.Error, "Start node does not exist in graph.", start);
            return result;
        }

        // 2) Traverse reachable subgraph from Start; collect stats
        HashSet<int> visited = new(capacity: 64);
        Queue<NodeId> queue = new(capacity: 64);
        queue.Enqueue(start);
        bool sawTerminal = false;

        while (queue.Count > 0)
        {
            NodeId current = queue.Dequeue();
            if (!visited.Add(current.Index))
                continue;

            if (!graph.TryGetNode(current, out _))
            {
                result.Add(Severity.Error, "Node referenced during traversal does not exist.", current);
                continue;
            }

            if (!graph.TryGetTransition(current, out Transition edge) || edge.IsEmpty)
            {
                // Terminal node: no outgoing edge
                sawTerminal = true;
                continue;
            }

            NodeId dest = edge.Destination;

            if (dest.Equals(NodeId.Default))
            {
                result.Add(Severity.Error, "Transition destination is NodeId.Default (invalid).", current);
                continue;
            }

            if (!graph.TryGetNode(dest, out _))
            {
                result.Add(Severity.Error, $"Transition points to non-existent node #{dest.Index}.", current);
            }

            if (options.WarnOnSelfLoop && dest.Index == current.Index)
            {
                result.Add(Severity.Warning, "Self-loop transition detected.", current);
            }

            queue.Enqueue(dest);
        }

        // 3) Detect lack of terminal path
        if (!sawTerminal)
        {
            Severity sev = options.StrictNoTerminalPath ? Severity.Error : Severity.Warning;
            result.Add(sev, "No terminal path reachable from Start (all paths have outgoing transitions).", start);
        }

        // 4) If AllNodes are supplied, check for unreachable nodes and duplicate names
        if (options.AllNodes is { Count: > 0 } all)
        {
            // Unreachable detection
            foreach (NodeId node in all)
            {
                if (!visited.Contains(node.Index))
                    result.Add(Severity.Warning, "Node is unreachable from Start.", node);
            }

            // Duplicate names (case-sensitive by default)
            Dictionary<string, List<NodeId>> byName = new();
            foreach (NodeId node in all)
            {
                string name = node.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!byName.TryGetValue(name, out List<NodeId>? list))
                    byName[name] = list = new List<NodeId>(2);
                list.Add(node);
            }

            foreach (KeyValuePair<string, List<NodeId>> kvp in byName)
            {
                if (kvp.Value.Count <= 1)
                {
                    continue;
                }

                foreach (NodeId dup in kvp.Value)
                    result.Add(Severity.Warning, $"Duplicate node name '{kvp.Key}'.", dup);
            }
        }
        else
        {
            // Cannot check unreachable/duplicates without AllNodes
            result.Add(Severity.Info, "Skipped unreachable/duplicate-name checks (AllNodes not provided).",
                NodeId.Default);
        }

        return result;
    }
}

public static class GraphValidationExtensions
{
    /// <summary>
    /// Validate and throw in DEBUG builds when errors exist. Returns the result regardless.
    /// </summary>
    public static GraphValidationResult ValidateAndThrowIfErrorsDebug(this Graph graph,
        IReadOnlyList<NodeId>? all = null)
    {
        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = all });
#if DEBUG
            if (res.HasErrors) throw new GraphValidationException(res);
#endif
        return res;
    }
}