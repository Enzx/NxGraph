using NxGraph.Graphs;
#if NETSTANDARD2_1
using ArgumentNullException = System.ArgumentNullExceptionShim;
#endif
namespace NxGraph.Diagnostics.Validations;

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