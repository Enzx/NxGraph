using NxGraph.Graphs;

namespace NxGraph.Serialization.Abstraction;

public interface ILogicCodec;

public interface ILogicCodec<TWire> : ILogicCodec
{
     TWire Serialize(IAsyncLogic asyncLogic);
     IAsyncLogic Deserialize(TWire data);
}