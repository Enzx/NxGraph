namespace NxGraph.Serialization.Abstraction;

public interface IGraphSerializer
{
    /// <summary>
    /// Set the codec used to serialize and deserialize node logic.
    /// </summary>
    /// <param name="codec">The codec to use.</param>
    /// <remarks>
    /// This method must be called before any serialization or deserialization is performed.
    /// Implement <see cref="ILogicCodec{Twire}"/> for your logic types and provide an instance here.
    /// </remarks>
    static abstract void SetLogicCodec<TWire>(ILogicCodec<TWire> codec);
    
}