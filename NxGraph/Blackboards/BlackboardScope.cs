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
    /// Transient per-node scratch: values live for one node <i>visit</i> and reset to their
    /// registered defaults at transition boundaries (success transition, failure-edge reroute,
    /// run start, reset, resume). In-place retries keep the scratch — same visit, same values.
    /// The board is machine-owned (auto-created from the graph's declared Node schema) and can
    /// never be user-bound or shared; it is also not durable — resuming a snapshot resets it.
    /// </summary>
    Node = 2,
}
