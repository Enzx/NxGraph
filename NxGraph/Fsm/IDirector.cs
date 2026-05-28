using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// A director is a node that selects the next node to run based on some logic.
/// </summary>
public interface IDirector
{
    /// <summary>
    /// Selects the next node to run based on some logic.
    /// </summary>
    /// <returns></returns>
    NodeId SelectNext();

    /// <summary>
    /// Enumerates every <see cref="NodeId"/> this director can statically be routed to
    /// (i.e. nodes whose existence is known at authoring time, not selected dynamically
    /// at runtime). Used by diagnostics (Mermaid export, reachability validation) so
    /// director branches are visible to tooling.
    /// </summary>
    /// <remarks>
    /// The default returns an empty sequence so existing user implementations compile
    /// unchanged — but those custom directors will be opaque to the validator and the
    /// exporter. Built-in <see cref="ChoiceState"/> and <see cref="SwitchState"/>
    /// override this to surface their known targets.
    /// </remarks>
    IEnumerable<NodeId> EnumerateStaticTargets() => System.Array.Empty<NodeId>();
}

public interface IAsyncDirector
{
    /// <summary>
    /// Asynchronously selects the next node to run based on some logic.
    /// </summary>
    /// <param name="ct">The cancellation token to observe while selecting the next node.</param>
    /// <returns>A <see cref="ValueTask{NodeId}"/> representing the selected next node.</returns>
    ValueTask<NodeId> SelectNextAsync(CancellationToken ct = default);

    /// <inheritdoc cref="IDirector.EnumerateStaticTargets"/>
    IEnumerable<NodeId> EnumerateStaticTargets() => System.Array.Empty<NodeId>();
}