namespace NxGraph.Graphs;

/// <summary>
/// Interface representing a node in the FSM graph.
/// </summary>
public interface INode
{
    /// <summary>
    /// Executes the logic of the node asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{Result}"/> representing the asynchronous operation.</returns>
    ValueTask<Result> ExecuteAsync(CancellationToken ct = default);
}