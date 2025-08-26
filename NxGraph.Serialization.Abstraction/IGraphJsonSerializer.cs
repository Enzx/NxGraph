using NxGraph.Graphs;

namespace NxGraph.Serialization.Abstraction;

public interface IGraphSerializer
{
    /// <summary>
    /// Set the codec used to serialize and deserialize node logic.
    /// </summary>
    /// <param name="codec">The codec to use.</param>
    /// <remarks>
    /// This method must be called before any serialization or deserialization is performed.
    /// Implement <see cref="ILogicTextCodec"/>,<see cref="ILogicBinaryCodec"/> or <see cref="ILogicCodec{Twire}"/> for more details.
    /// </remarks>
    static abstract void SetLogicCodec<TWire>(ILogicCodec<TWire> codec);

    /// <summary>
    /// Convert a Graph into an intermediate DTO for serialization.
    /// </summary>
    /// <param name="graph">The graph to convert.</param>
    /// <returns>The converted DTO.</returns>
    static abstract GraphDto ToDto(Graph graph);

    /// <summary>
    ///  Convert an intermediate DTO back into a Graph.
    /// </summary>
    /// <param name="dto">The DTO to convert.</param>
    /// <returns>The converted graph.</returns>
    static abstract Graph FromDto(GraphDto dto);
}

public interface IGraphJsonSerializer : IGraphSerializer
{
    /// <summary>
    /// Serialize a graph to the stream as JSON.
    /// </summary>
    /// <param name="graph">The graph to serialize.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    static abstract ValueTask ToJsonAsync(Graph graph, Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Deserialize a graph from a JSON stream.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized graph.</returns>
    static abstract ValueTask<Graph> FromJsonAsync(Stream source, CancellationToken ct = default);
}

public interface IGraphBinarySerializer : IGraphSerializer
{
    /// <summary>
    /// Serialize a graph to the stream as binary.
    /// </summary>
    /// <param name="graph">The graph to serialize.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    static abstract ValueTask ToBinaryAsync(Graph graph, Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Deserialize a graph from a binary stream.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized graph.</returns>
    static abstract ValueTask<Graph> FromBinaryAsync(Stream source, CancellationToken ct = default);
}