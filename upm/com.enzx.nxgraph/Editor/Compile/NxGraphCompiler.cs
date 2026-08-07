using System;
using System.Collections.Generic;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Unity.Editor.Compile
{
    /// <summary>
    /// Turns an <see cref="NxGraphAsset"/> into a runtime <see cref="Graph"/>.
    /// <para>
    /// The fluent DSL (<c>.To(...)</c>, <c>.If(...)</c>, …) is built for authoring a graph as a
    /// chain you write top to bottom. An editor has the opposite shape: an unordered bag of nodes
    /// and edges where any node may already exist when an edge referencing it is read. So this
    /// uses <see cref="GraphBuilder"/>'s node-level API instead — <c>AddNode</c>, then
    /// <c>AddTransition</c> / <c>AddFailureTransition</c> — which is exactly what the DSL lowers
    /// to.
    /// </para>
    /// </summary>
    public static class NxGraphCompiler
    {
        /// <summary>
        /// Compiles <paramref name="asset"/>. Returns null and fills <paramref name="errors"/>
        /// when the document is not well-formed enough to build — a half-drawn graph is the
        /// normal state of an editor, so this reports rather than throws.
        /// </summary>
        /// <param name="uidToNode">
        /// Maps each authored node's UID to the <see cref="NodeId"/> it compiled to. The built
        /// graph also carries the UIDs (<c>Graph.TryGetNodeByUid</c>); this is the reverse
        /// direction, for lighting up the canvas from a running machine.
        /// </param>
        public static Graph Compile(NxGraphAsset asset, List<string> errors,
            out Dictionary<string, NodeId> uidToNode)
        {
            if (asset is null) throw new ArgumentNullException(nameof(asset));
            if (errors is null) throw new ArgumentNullException(nameof(errors));

            uidToNode = new Dictionary<string, NodeId>(StringComparer.Ordinal);

            if (asset.Nodes.Count == 0)
            {
                errors.Add("The graph has no nodes.");
                return null;
            }

            NxNodeData start = asset.FindNode(asset.StartNodeUid);
            if (start is null)
            {
                errors.Add(string.IsNullOrEmpty(asset.StartNodeUid)
                    ? "No start node is set. Right-click a node and choose 'Set as Start'."
                    : $"The start node '{asset.StartNodeUid}' is not in the graph.");
                return null;
            }

            GraphBuilder builder = new();

            // nodes[0] must be the start node: the core treats index 0 as the entry point, and
            // that invariant is what every runtime and validator relies on.
            AddNode(builder, start, isStart: true, uidToNode, errors);

            foreach (NxNodeData node in asset.Nodes)
            {
                if (node.Uid == start.Uid)
                {
                    continue;
                }

                AddNode(builder, node, isStart: false, uidToNode, errors);
            }

            if (errors.Count > 0)
            {
                return null;
            }

            foreach (NxEdgeData edge in asset.Edges)
            {
                if (!uidToNode.TryGetValue(edge.FromUid, out NodeId from))
                {
                    errors.Add($"Edge starts at unknown node '{edge.FromUid}'.");
                    continue;
                }

                if (!uidToNode.TryGetValue(edge.ToUid, out NodeId to))
                {
                    errors.Add($"Edge ends at unknown node '{edge.ToUid}'.");
                    continue;
                }

                if (edge.IsFailure)
                {
                    builder.AddFailureTransition(from, to);
                }
                else
                {
                    builder.AddTransition(from, to);
                }
            }

            if (errors.Count > 0)
            {
                return null;
            }

            try
            {
                // throwOnError: false — validation is surfaced in the window's diagnostics panel,
                // where the author can see which node is at fault, not thrown at them.
                return builder.Build(throwOnError: false);
            }
            catch (Exception e)
            {
                // A structural rejection GraphBuilder makes at build time (a duplicate success
                // edge, an unresolvable Goto) rather than a validation warning.
                errors.Add(e.Message);
                return null;
            }
        }

        private static void AddNode(GraphBuilder builder, NxNodeData node, bool isStart,
            Dictionary<string, NodeId> uidToNode, List<string> errors)
        {
            if (uidToNode.ContainsKey(node.Uid))
            {
                errors.Add($"Duplicate node UID '{node.Uid}' ({node.DisplayName}).");
                return;
            }

            // A relay is the smallest thing that is a real node: it carries the whole fault
            // model (retry policy, failure edge, outcome) without needing a State subclass.
            Result result = node.Outcome == NxNodeOutcome.Failure ? Result.Failure : Result.Success;
            NodeId id = builder.AddNode(new RelayState(() => result), isStart);

            builder.SetName(id, string.IsNullOrWhiteSpace(node.DisplayName) ? "Step" : node.DisplayName);

            if (Guid.TryParseExact(node.Uid, "N", out Guid uid) && uid != Guid.Empty)
            {
                // Stable per-node identity for editor tooling. Never read by any runtime; it is
                // what lets a built graph point back at the asset node it came from.
                builder.SetUid(id, uid);
            }

            uidToNode[node.Uid] = id;
        }
    }
}
