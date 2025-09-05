using NxGraph.Graphs;

namespace NxGraph.Serialization.Abstraction;

public interface IGraphJsonSerializer : IGraphSerializer
{
    /// <summary>
    /// Serialize a graph to the stream as JSON.
    /// </summary>
    /// <param name="graph">The graph to serialize.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask ToJsonAsync(Graph graph, Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Deserialize a graph from a JSON stream.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized graph.</returns>
    ValueTask<Graph> FromJsonAsync(Stream source, CancellationToken ct = default);
}