using System.Text;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Diagnostics.Export;

/// <summary>
/// Exports a <see cref="Graph"/> (single static transition per node) to Mermaid flowchart syntax.
/// Dynamic branches chosen at runtime by <c>IDirector</c> are not emitted (no static edges),
/// but statically-known director targets appear as dashed edges. Token fork/join nodes render
/// first-class: subroutine-bar shapes, solid <c>fork</c>-labeled branch edges (every branch
/// runs — an AND-split, not a choice), the join's <see cref="JoinPolicy"/> in its label, and
/// no terminal edge from forks (a fork never ends a token).
/// </summary>
public sealed class MermaidGraphExporter : IGraphExporter
{
    public string Format => "Mermaid";
    public string FileExtension => ".mmd";
    public string ContentType => "text/markdown";

    public string Export(Graph graph, ExportOptions? options = null)
    {
        MermaidExportOptions opts = options as MermaidExportOptions ?? new MermaidExportOptions();

        string dir = opts.Direction switch
        {
            FlowDirection.TopToBottom => "TB",
            FlowDirection.LeftToRight => "LR",
            FlowDirection.RightToLeft => "RL",
            FlowDirection.BottomToTop => "BT",
            _ => "LR"
        };

        StringBuilder sb = new(256 + graph.NodeCount * 32);

        // Header
        string title = opts.Title ?? graph.Id.ToString();
        sb.AppendLine($"%% NxGraph → Mermaid export: {EscapeComment(title)}");
        sb.AppendLine($"flowchart {dir}");

        // Nodes
        for (int i = 0; i < graph.NodeCount; i++)
        {
            INode node = graph.GetNodeByIndex(i);
            string nodeId = NodeVar(i);
            string label = BuildNodeLabel(node.Id, i, opts);

            // Shape: Start node gets a "stadium" shape, director nodes — sync IDirector or
            // async IAsyncDirector — get a rhombus, others are rectangles. Previously only
            // the sync IDirector path was checked, so async If/Switch nodes silently
            // rendered as plain rectangles.
            if (node is LogicNode ln)
            {
                // Token fork/join first: ForkState implements IDirector purely so its branches
                // reach reachability/exports — rendering it as a decision rhombus would read
                // as "choose one" when every branch runs. Both get the subroutine-bar shape;
                // the join carries its firing policy so All/Any/Quorum are distinguishable.
                if (ForkOf(ln) is not null)
                {
                    sb.Append("  ").Append(nodeId).Append("[[\"").Append(label).AppendLine("\"]]");
                    continue;
                }

                if (JoinOf(ln) is { } join)
                {
                    sb.Append("  ").Append(nodeId).Append("[[\"").Append(label)
                        .Append(" : ").Append(join.Policy).AppendLine("\"]]");
                    continue;
                }

                if (ln.AsyncLogic is IDirector || ln.Logic is IDirector
                    || ln.AsyncLogic is IAsyncDirector || ln.Logic is IAsyncDirector)
                {
                    // BuildNodeLabel already escaped the label — escaping again here mangled
                    // quotes/backslashes in director names (rendered \\\" instead of \").
                    label = $"{{\"{label}\"}}";
                    sb.Append("  ").Append(nodeId).Append(label).AppendLine();
                    continue;
                }
            }

            string shape = (i == NodeId.Start.Index) ? $"([\"{label}\"])" : $"[\"{label}\"]";
            sb.Append("  ").Append(nodeId).Append(shape).AppendLine();
        }

        // Optional global terminal node (only emitted if needed). A fork's transition slot is
        // always empty by design (branches replace the success edge), but a fork never ends a
        // token — it must not count as terminal.
        bool needsTerminal = false;
        for (int i = 0; i < graph.TransitionCount; i++)
        {
            Transition edge = graph.GetTransitionByIndex(i);
            if (!edge.IsEmpty || IsFork(graph, i))
            {
                continue;
            }

            needsTerminal = true;
            break;
        }

        if (opts.AddTerminalNode && needsTerminal)
        {
            string endLabel = string.IsNullOrWhiteSpace(opts.TerminalLabel) ? "End" : opts.TerminalLabel.Trim();
            sb.Append("  ").Append(TerminalVar).Append("((\"").Append(EscapeLabel(endLabel)).AppendLine("\"))");
        }

        // Edges
        for (int i = 0; i < graph.TransitionCount; i++)
        {
            Transition edge = graph.GetTransitionByIndex(i);
            string from = NodeVar(i);

            if (edge.IsEmpty)
            {
                if (opts.AddTerminalNode && !IsFork(graph, i))
                {
                    sb.Append("  ").Append(from).Append(" --> ").Append(TerminalVar).AppendLine();
                }

                // else: omit terminal arrow; the node appears unconnected (terminal).
                // Forks are skipped either way — their fan-out renders below.
                continue;
            }

            int dstIdx = edge.Destination.Index;
            // Defensive: only link if destination exists in this graph
            if ((uint)dstIdx < (uint)graph.NodeCount)
            {
                string to = NodeVar(dstIdx);
                sb.Append("  ").Append(from).Append(" --> ").Append(to).AppendLine();
            }
            else
            {
                // Dangling destination (shouldn't happen with well-formed graphs) – link to synthetic terminal
                if (opts.AddTerminalNode)
                {
                    sb.Append("  ").Append(from).Append(" -. invalid → .- ").Append(TerminalVar).AppendLine();
                }
            }
        }

        // Failure edges: emitted as labeled dashed arrows so fault routing is visible
        // alongside the solid success flow.
        for (int i = 0; i < graph.TransitionCount; i++)
        {
            Transition edge = graph.GetTransitionByIndex(i);
            if (!edge.HasFailureDestination)
            {
                continue;
            }

            int dstIdx = edge.FailureDestination.Index;
            if ((uint)dstIdx < (uint)graph.NodeCount)
            {
                sb.Append("  ").Append(NodeVar(i)).Append(" -. fail .-> ").Append(NodeVar(dstIdx)).AppendLine();
            }
        }

        // Director edges: directors don't carry a static transition (the runtime calls
        // SelectNext at execution time), so they're missed by the static-edge loop above.
        // Emit dashed edges to every statically-known target so the rendered diagram
        // reflects the actual reachability. Fork branches are the exception: they are
        // static AND-splits (every branch runs), so they render as solid labeled edges
        // instead of the dashed "chosen at runtime" style.
        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (graph.GetNodeByIndex(i) is not LogicNode dn) continue;

            if (ForkOf(dn) is { } fork)
            {
                string forkVar = NodeVar(i);
                foreach (NodeId branch in fork.Branches)
                {
                    int branchIdx = branch.Index;
                    if ((uint)branchIdx >= (uint)graph.NodeCount) continue;
                    sb.Append("  ").Append(forkVar).Append(" -- fork --> ").Append(NodeVar(branchIdx))
                        .AppendLine();
                }

                continue;
            }

            // Event entries (spec 013) render as an event surface, not an anonymous switch:
            // each entry edge carries the event type's short name, and the Otherwise edge is
            // labeled "otherwise". Dashed like other director edges — the branch taken is a
            // runtime (raise-time) decision.
            if (EventEntryOf(dn) is { } eventEntry)
            {
                string entryVar = NodeVar(i);
                foreach (EventRegistration registration in eventEntry.Registrations)
                {
                    int dstIdx = registration.Target.Index;
                    if ((uint)dstIdx >= (uint)graph.NodeCount) continue;
                    sb.Append("  ").Append(entryVar).Append(" -. ")
                        .Append(EscapeLabel(registration.EventTypeShortName)).Append(" .-> ")
                        .Append(NodeVar(dstIdx)).AppendLine();
                }

                int defaultIdx = eventEntry.DefaultTarget.Index;
                if (!eventEntry.DefaultTarget.Equals(NodeId.Default) && (uint)defaultIdx < (uint)graph.NodeCount)
                {
                    sb.Append("  ").Append(entryVar).Append(" -. otherwise .-> ")
                        .Append(NodeVar(defaultIdx)).AppendLine();
                }

                continue;
            }

            IEnumerable<NodeId>? targets =
                (dn.AsyncLogic as IDirector)?.EnumerateStaticTargets()
                ?? (dn.Logic as IDirector)?.EnumerateStaticTargets()
                ?? (dn.AsyncLogic as IAsyncDirector)?.EnumerateStaticTargets()
                ?? (dn.Logic as IAsyncDirector)?.EnumerateStaticTargets();
            if (targets is null) continue;

            string from = NodeVar(i);
            foreach (NodeId target in targets)
            {
                int dstIdx = target.Index;
                // Skip the NodeId.Default sentinel (terminal exit from a director) — emitting
                // an edge to a non-existent index would just look like a broken link.
                if (target.Equals(NodeId.Default)) continue;
                if ((uint)dstIdx >= (uint)graph.NodeCount) continue;
                sb.Append("  ").Append(from).Append(" -.-> ").Append(NodeVar(dstIdx)).AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string NodeVar(int index) => $"n{index}";
    private const string TerminalVar = "End";

    private static ForkState? ForkOf(LogicNode node) =>
        node.Logic as ForkState ?? node.AsyncLogic as ForkState;

    private static JoinState? JoinOf(LogicNode node) =>
        node.Logic as JoinState ?? node.AsyncLogic as JoinState;

    private static EventEntryState? EventEntryOf(LogicNode node) =>
        node.AsyncLogic as EventEntryState ?? node.Logic as EventEntryState;

    private static bool IsFork(Graph graph, int index) =>
        graph.GetNodeByIndex(index) is LogicNode ln && ForkOf(ln) is not null;

    private static string BuildNodeLabel(NodeId id, int index, MermaidExportOptions opts)
    {
        string baseName = (opts.UseNodeNames && !string.IsNullOrEmpty(id.Name))
            ? id.Name
            : (index == NodeId.Start.Index ? "Start" : "Node");

        if (opts.ShowNodeIndices)
        {
            baseName = $"{baseName} [{index}]";
        }

        return EscapeLabel(baseName);
    }

    private static string EscapeLabel(string s)
    {
        return s.Replace("\\", @"\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
    }

    private static string EscapeComment(string s)
    {
        return s.Replace('\r', ' ').Replace('\n', ' ');
    }
}