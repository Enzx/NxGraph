using System.Text;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Export;

/// <summary>
/// Exports a <see cref="Graph"/> (single static transition per node) to Mermaid flowchart syntax.
/// Dynamic branches chosen at runtime by <c>IDirector</c> are not emitted (no static edges).
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
            Node node = graph.GetNodeByIndex(i);
            string nodeId = NodeVar(i);
            string label = BuildNodeLabel(node.Id, i, opts);

            // Shape: Start node gets a "stadium" shape, if IDirector rhombus {}, others are rectangles
            if (node.Logic is IDirector)
            {
                label = $"{{\"{EscapeLabel( label)}\"}}";
                sb.Append("  ").Append(nodeId).Append(label).AppendLine();
                continue;
            }

            string shape = (i == NodeId.Start.Index) ? $"([\"{label}\"])" : $"[\"{label}\"]";
            sb.Append("  ").Append(nodeId).Append(shape).AppendLine();
        }

        // Optional global terminal node (only emitted if needed)
        bool needsTerminal = false;
        for (int i = 0; i < graph.TransitionCount; i++)
        {
            Transition edge = graph.GetTransitionByIndex(i);
            if (!edge.IsEmpty)
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
                if (opts.AddTerminalNode)
                {
                    sb.Append("  ").Append(from).Append(" --> ").Append(TerminalVar).AppendLine();
                }

                // else: omit terminal arrow; the node appears unconnected (terminal)
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

        return sb.ToString();
    }

    private static string NodeVar(int index) => $"n{index}";
    private const string TerminalVar = "End";

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