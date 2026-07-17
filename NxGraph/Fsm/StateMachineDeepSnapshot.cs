using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// The deep counterpart of <see cref="StateMachineSnapshot"/>: the machine's own position
/// (<see cref="Self"/>) plus the internal state of every composite node in its graph that
/// holds durable state (<see cref="Composites"/>). Produced by <c>SuspendDeep()</c> and
/// consumed by <c>ResumeDeep(...)</c> on <see cref="Async.AsyncStateMachine"/> or
/// <see cref="StateMachine"/> built over an equivalent graph (same node indices, per nesting
/// level). Deep snapshots are interchangeable between the two runtimes as long as the graph's
/// nodes are executable by the target runtime.
/// <para>
/// Like the shallow snapshot, the records contain only primitives and arrays — no
/// polymorphism — so any serializer (e.g. <c>System.Text.Json</c>) handles them with zero
/// configuration; the library never serializes them itself. Capture is <b>sparse</b>: only
/// composites that persist visit or cross-visit state (<see cref="ISuspendableComposite"/>
/// implementors) emit entries, and a composite node absent from <see cref="Composites"/>
/// simply re-enters fresh on resume. The user context (agent/blackboards) is owned by the
/// caller and must be re-attached after resuming; Node-scoped scratch is transient by
/// definition and resumes as defaults at every nesting level.
/// </para>
/// </summary>
public sealed record StateMachineDeepSnapshot(
    StateMachineSnapshot Self,
    CompositeSnapshot[] Composites);

/// <summary>
/// One composite node's captured internals inside a <see cref="StateMachineDeepSnapshot"/>:
/// the owner node's index in the capturing machine's graph, the sync RoundPerTick mid-visit
/// flag (<see cref="InFlight"/>, always <c>false</c> from async composites), the parallel
/// per-region done bits (<see cref="Done"/> — captured, not derived, because a dynamic
/// parallel deselection is done-without-terminal-status; empty for single-child composites),
/// and the child/region machines' own deep snapshots in region order
/// (<see cref="Children"/> — length 1 for subgraph/history composites). Join bookkeeping that
/// is derivable (<c>_remaining</c>, <c>_anyFailed</c>) is deliberately not captured — it is
/// recomputed at resume so the wire shape cannot self-contradict.
/// </summary>
public sealed record CompositeSnapshot(
    int NodeIndex,
    bool InFlight,
    bool[] Done,
    StateMachineDeepSnapshot[] Children);

/// <summary>
/// Shared deep-suspend plumbing for both FSM machines: the linear capture walk over one
/// graph's nodes, the resume-side validation/dispatch, and the composite-side shape checks.
/// The walk itself never recurses — composites recurse by calling <c>SuspendDeep()</c> /
/// <c>ResumeDeep(...)</c> on their own child machines, mirroring the agent/blackboard
/// stamping walks — so a thread-local depth counter guards the machine-hop recursion with
/// the same limit and error style as <c>Graph.MaxStampingDepth</c>.
/// </summary>
internal static class DeepSnapshots
{
    /// <summary>Mirrors <c>Graph.MaxStampingDepth</c>.</summary>
    private const int MaxDepth = 64;

    [ThreadStatic] private static int _captureDepth;
    [ThreadStatic] private static int _resumeDepth;

    /// <summary>
    /// One linear O(nodes) walk over <paramref name="graph"/> collecting
    /// <see cref="ISuspendableComposite.SuspendComposite"/> from every logic node whose
    /// async-then-sync logic slot implements the interface (the same probing order as the
    /// stamping walks). Cold path by contract — allocates the entry list and arrays.
    /// </summary>
    internal static CompositeSnapshot[] Capture(Graph graph)
    {
        if (_captureDepth >= MaxDepth)
        {
            throw new InvalidOperationException(
                $"Composite nesting exceeds {MaxDepth} levels while capturing a deep snapshot — " +
                "check for a cycle between graphs nested as composites.");
        }

        _captureDepth++;
        try
        {
            List<CompositeSnapshot>? entries = null;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                ISuspendableComposite? composite = SuspendableAt(graph, i);
                if (composite is null)
                {
                    continue;
                }

                (entries ??= []).Add(composite.SuspendComposite(i));
            }

            return entries?.ToArray() ?? [];
        }
        finally
        {
            _captureDepth--;
        }
    }

    /// <summary>
    /// Validates every composite entry against <paramref name="graph"/> (index in range, the
    /// claimed node implements <see cref="ISuspendableComposite"/>) before dispatching any
    /// restore, then hands each entry to its composite. Composite-side shape checks and the
    /// recursive child validation run inside <see cref="ISuspendableComposite.ResumeComposite"/>.
    /// </summary>
    internal static void ResumeComposites(Graph graph, CompositeSnapshot[]? composites)
    {
        if (composites is null || composites.Length == 0)
        {
            return;
        }

        if (_resumeDepth >= MaxDepth)
        {
            throw new InvalidOperationException(
                $"Composite nesting exceeds {MaxDepth} levels while resuming a deep snapshot — " +
                "check for a cycle between graphs nested as composites.");
        }

        _resumeDepth++;
        try
        {
            for (int i = 0; i < composites.Length; i++)
            {
                CompositeSnapshot entry = composites[i]
                    ?? throw new InvalidOperationException(
                        $"Deep snapshot composite entry at position {i} is null.");

                if ((uint)entry.NodeIndex >= (uint)graph.NodeCount)
                {
                    throw new InvalidOperationException(
                        $"Deep snapshot composite entry claims node index {entry.NodeIndex}, which is out of " +
                        $"range for this graph (0..{graph.NodeCount - 1}).");
                }

                if (SuspendableAt(graph, entry.NodeIndex) is null)
                {
                    throw new InvalidOperationException(
                        $"Deep snapshot composite entry claims node index {entry.NodeIndex}, but that node's " +
                        $"logic does not implement {nameof(ISuspendableComposite)} — the graph is not " +
                        "equivalent to the one the snapshot was taken from.");
                }
            }

            for (int i = 0; i < composites.Length; i++)
            {
                SuspendableAt(graph, composites[i].NodeIndex)!.ResumeComposite(composites[i]);
            }
        }
        finally
        {
            _resumeDepth--;
        }
    }

    private static ISuspendableComposite? SuspendableAt(Graph graph, int index)
    {
        if (!graph.TryGetNodeByIndex(index, out INode? node) || node is not LogicNode logicNode)
        {
            return null;
        }

        // Check the async logic first, then the sync logic (for States wrapped in
        // SyncLogicAdapter) — the same probing order as the agent/blackboard stamping walks.
        return logicNode.AsyncLogic as ISuspendableComposite ?? logicNode.Logic as ISuspendableComposite;
    }

    /// <summary>
    /// Composite-side shape validation: the entry must carry exactly the child snapshots and
    /// done bits this composite expects, else the graph is not equivalent to the snapshot's.
    /// Errors name the composite's node index.
    /// </summary>
    internal static void ValidateShape(CompositeSnapshot snapshot, int expectedChildren, int expectedDoneBits)
    {
        if (snapshot.Children is null || snapshot.Children.Length != expectedChildren)
        {
            throw new InvalidOperationException(
                $"Deep snapshot for composite node {snapshot.NodeIndex} carries " +
                $"{snapshot.Children?.Length ?? 0} child snapshot(s), but this composite expects " +
                $"{expectedChildren}.");
        }

        if (snapshot.Done is null || snapshot.Done.Length != expectedDoneBits)
        {
            throw new InvalidOperationException(
                $"Deep snapshot for composite node {snapshot.NodeIndex} carries " +
                $"{snapshot.Done?.Length ?? 0} done bit(s), but this composite expects {expectedDoneBits}.");
        }
    }

    /// <summary>
    /// Deep-resumes one child machine, contextualizing any validation error with the owning
    /// composite's node index (the child machine's own messages have no parent-graph index).
    /// </summary>
    internal static void ResumeChild(Async.AsyncStateMachine child, CompositeSnapshot owner, int childIndex)
    {
        try
        {
            child.ResumeDeep(owner.Children[childIndex]);
        }
        catch (InvalidOperationException ex)
        {
            throw ChildResumeError(owner, childIndex, ex);
        }
    }

    /// <inheritdoc cref="ResumeChild(Async.AsyncStateMachine, CompositeSnapshot, int)"/>
    internal static void ResumeChild(StateMachine child, CompositeSnapshot owner, int childIndex)
    {
        try
        {
            child.ResumeDeep(owner.Children[childIndex]);
        }
        catch (InvalidOperationException ex)
        {
            throw ChildResumeError(owner, childIndex, ex);
        }
    }

    private static InvalidOperationException ChildResumeError(
        CompositeSnapshot owner, int childIndex, InvalidOperationException inner)
    {
        return new InvalidOperationException(
            $"Deep snapshot for composite node {owner.NodeIndex}, child {childIndex}: {inner.Message}", inner);
    }
}
