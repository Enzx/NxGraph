using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
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

        Guard.NotNull(graph, nameof(graph));
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

            if (!graph.TryGetNode(current, out INode? currentNode))
            {
                result.Add(Severity.Error, "Node referenced during traversal does not exist.", current);
                continue;
            }

            // Failure edges apply to every node kind, directors included, so walk them
            // before the director-specific handling below.
            if (graph.TryGetTransition(current, out Transition outgoing) && outgoing.HasFailureDestination)
            {
                NodeId failureDest = outgoing.FailureDestination;
                if (!graph.TryGetNode(failureDest, out _))
                {
                    result.Add(Severity.Error,
                        $"Failure transition points to non-existent node #{failureDest.Index}.", current);
                }
                else
                {
                    if (options.WarnOnSelfLoop && failureDest.Index == current.Index)
                    {
                        result.Add(Severity.Warning, "Self-loop transition detected.", current);
                    }

                    queue.Enqueue(failureDest);
                }
            }

            // Director nodes route at runtime; their statically-known targets must be walked
            // explicitly because TryGetTransition returns Empty for them. EnumerateStaticTargets
            // returns the empty sequence for opaque custom directors — those remain invisible
            // to reachability but the built-in Choice/Switch states now participate.
            IEnumerable<NodeId>? directorTargets = null;
            if (currentNode is LogicNode logicNode)
            {
                directorTargets =
                    (logicNode.AsyncLogic as IDirector)?.EnumerateStaticTargets()
                    ?? (logicNode.Logic as IDirector)?.EnumerateStaticTargets()
                    ?? (logicNode.AsyncLogic as IAsyncDirector)?.EnumerateStaticTargets()
                    ?? (logicNode.Logic as IAsyncDirector)?.EnumerateStaticTargets();
            }

            if (directorTargets is not null)
            {
                bool sawTerminalBranch = false;
                bool sawAnyTarget = false;
                foreach (NodeId branchDest in directorTargets)
                {
                    sawAnyTarget = true;
                    if (branchDest.Equals(NodeId.Default))
                    {
                        // NodeId.Default is the runtime sentinel for "exit successfully" from
                        // a director — counts as a terminal path, not an error.
                        sawTerminalBranch = true;
                        continue;
                    }

                    if (!graph.TryGetNode(branchDest, out _))
                    {
                        result.Add(Severity.Error,
                            $"Director branch points to non-existent node #{branchDest.Index}.", current);
                        continue;
                    }

                    if (options.WarnOnSelfLoop && branchDest.Index == current.Index)
                    {
                        result.Add(Severity.Warning, "Self-loop transition detected.", current);
                    }

                    queue.Enqueue(branchDest);
                }

                if (sawTerminalBranch)
                {
                    sawTerminal = true;
                }

                if (!sawAnyTarget)
                {
                    result.Add(Severity.Warning,
                        "Director exposes no static targets — its branches are invisible to reachability " +
                        "validation and Mermaid export. Override EnumerateStaticTargets() to surface them.",
                        current);
                }

                // Director nodes do not have a static fall-through transition; skip the
                // edge check below.
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

        // 4) Strict sync validation: all nodes must implement ILogic
        if (options.StrictSyncOnly)
        {
            foreach (int index in visited)
            {
                if (graph.TryGetNodeByIndex(index, out INode? node) && node!.Logic is null)
                {
                    result.Add(Severity.Error,
                        "Node does not implement ILogic and cannot be executed by sync StateMachine.",
                        node.Id);
                }
            }
        }

        // 5) If AllNodes are supplied, check for unreachable nodes and duplicate names
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