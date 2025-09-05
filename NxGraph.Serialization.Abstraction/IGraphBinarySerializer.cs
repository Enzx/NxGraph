using NxGraph.Graphs;

namespace NxGraph.Serialization.Abstraction;

public interface IGraphBinarySerializer : IGraphSerializer
{
    /// <summary>
    /// Serialize a graph to the stream as binary.
    /// </summary>
    /// <param name="graph">The graph to serialize.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask ToBinaryAsync(Graph graph, Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Deserialize a graph from a binary stream.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized graph.</returns>
    ValueTask<Graph> FromBinaryAsync(Stream source, CancellationToken ct = default);
}