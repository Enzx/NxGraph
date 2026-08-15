using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// One region entry of a parallel composite: the region graph plus its optional per-region
/// ports declaration (see <see cref="SubGraphPorts"/>). A plain <see cref="Graphs.Graph"/>
/// converts implicitly, so region lists mix ported and shared-board regions freely — two
/// sibling regions running the <i>same</i> child graph each get their own board, which is
/// exactly the collision the ports feature removes.
/// </summary>
public readonly struct ParallelRegion
{
    /// <summary>Creates a region entry over <paramref name="graph"/>.</summary>
    public ParallelRegion(Graph graph, SubGraphPorts? ports = null)
    {
        Graph = Guard.NotNull(graph, nameof(graph));
        Ports = ports;
    }

    /// <summary>The region graph.</summary>
    public Graph Graph { get; }

    /// <summary>The region's ports declaration, or <see langword="null"/> for the shared-board default.</summary>
    public SubGraphPorts? Ports { get; }

    /// <summary>A bare graph is a shared-board region.</summary>
    public static implicit operator ParallelRegion(Graph graph) => new(graph);
}
