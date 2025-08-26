namespace NxGraph.Graphs;

/// <summary>
/// Interface representing the logic of a node in a graph.
/// </summary>
public interface ILogic
{
    /// <summary>
    /// Executes the logic of the node asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{Result}"/> representing the asynchronous operation.</returns>
    ValueTask<Result> ExecuteAsync(CancellationToken ct = default);
}