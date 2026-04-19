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
}

public interface IAsyncDirector
{
    /// <summary>
    /// Asynchronously selects the next node to run based on some logic.
    /// </summary>
    /// <param name="ct">The cancellation token to observe while selecting the next node.</param>
    /// <returns>A <see cref="ValueTask{NodeId}"/> representing the selected next node.</returns>
    ValueTask<NodeId> SelectNextAsync(CancellationToken ct = default);
}