using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// One owned board reached by the owned-board enumeration walk: the live
/// <see cref="Blackboard"/> a ports-declaring composite created for its child, identified by
/// its node-index path — <c>"3"</c> for the board owned by the composite at node 3,
/// <c>"3/1"</c> for region 1 of a parallel at node 3 (or a nested composite at node 1 inside
/// a child graph), and so on, one segment per nesting hop. The path is stable for a given
/// graph structure, so hosts persist boards keyed by path and restore into the matching
/// entry of a fresh machine's enumeration. The empty path is the enumerating machine's own
/// board (a top-level machine constructed with ports).
/// </summary>
public readonly record struct OwnedBoardEntry(string Path, Blackboard Board);

/// <summary>
/// Durable-artifact surface of the composites that own a child board (sub-graph ports): the
/// machines' recursive owned-board walk collects entries from every graph node whose logic
/// implements this interface — the <see cref="Graphs.ISubGraphProvider"/> pattern, with the
/// same hard requirement of deterministic enumeration order, because hosts pair persisted
/// payloads with fresh boards by walking the same sequence. An owned board is a real durable
/// artifact: beside the graph payload, the machine snapshot, and the machine-bound boards,
/// hosts persist each owned board (e.g. with <c>BlackboardSerializer</c>) and restore into
/// the fresh machine's enumerated boards before <c>ResumeDeep</c>. Enumeration is a cold
/// path — allocation is expected.
/// </summary>
public interface IOwnedBoardProvider
{
    /// <summary>
    /// Yields every board owned by this composite (and, recursively, by composites inside
    /// its child graphs), with paths rooted at <paramref name="nodeIndex"/> — the owner
    /// node's index in the enumerating machine's graph.
    /// </summary>
    IEnumerable<OwnedBoardEntry> EnumerateOwnedBoards(int nodeIndex);
}

/// <summary>
/// Shared plumbing for the owned-board walk: the linear probe over one graph's nodes (the
/// <see cref="DeepSnapshots"/> shape — async-then-sync logic slots, no recursion of its own;
/// composites recurse by walking their child machines) and the path composition helpers.
/// </summary>
internal static class OwnedBoards
{
    /// <summary>Mirrors <c>Graph.MaxStampingDepth</c> and the deep-snapshot cap.</summary>
    private const int MaxDepth = 64;

    [ThreadStatic] private static int _depth;

    /// <summary>
    /// One linear walk over <paramref name="graph"/> yielding the entries of every node
    /// whose logic implements <see cref="IOwnedBoardProvider"/>, in node-index order.
    /// </summary>
    internal static IEnumerable<OwnedBoardEntry> Enumerate(Graph graph)
    {
        if (_depth >= MaxDepth)
        {
            throw new InvalidOperationException(
                $"Composite nesting exceeds {MaxDepth} levels while enumerating owned boards — " +
                "check for a cycle between graphs nested as composites.");
        }

        _depth++;
        try
        {
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (!graph.TryGetNodeByIndex(i, out INode? node) || node is not LogicNode logicNode)
                {
                    continue;
                }

                // Async-then-sync probing, the stamping-walk order; sync composites sit
                // behind SyncLogicAdapter, which LogicNode.Logic already unwraps.
                IOwnedBoardProvider? provider = logicNode.AsyncLogic as IOwnedBoardProvider
                                                ?? logicNode.Logic as IOwnedBoardProvider;
                if (provider is null)
                {
                    continue;
                }

                foreach (OwnedBoardEntry entry in provider.EnumerateOwnedBoards(i))
                {
                    yield return entry;
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Roots a machine-relative path at the owning node's index.</summary>
    internal static string ChildPath(int nodeIndex, string subPath) =>
        subPath.Length == 0 ? $"{nodeIndex}" : $"{nodeIndex}/{subPath}";

    /// <summary>Roots a region-machine-relative path at the owning node's index and region index.</summary>
    internal static string RegionPath(int nodeIndex, int regionIndex, string subPath) =>
        subPath.Length == 0 ? $"{nodeIndex}/{regionIndex}" : $"{nodeIndex}/{regionIndex}/{subPath}";
}
