namespace NxGraph.Blackboards;

/// <summary>
/// Which binding slot a schema's keys route to. Scope is a property of the
/// <see cref="BlackboardSchema"/>; every key registered on a schema inherits it.
/// </summary>
public enum BlackboardScope
{
    /// <summary>
    /// One user-owned board shared across graphs/machines. There is no global registry —
    /// the caller owns the instance and binds it to each machine that needs it.
    /// </summary>
    Global = 0,

    /// <summary>
    /// The default: one board per machine/entity, validated against the graph's declared schema.
    /// </summary>
    Graph = 1,

    /// <summary>
    /// Reserved for transient per-node keys reset at transition boundaries.
    /// Not yet implemented: <see cref="BlackboardSchema"/> rejects it at construction.
    /// </summary>
    Node = 2,
}
