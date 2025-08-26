using NxGraph.Graphs;

namespace NxGraph.Serialization.Abstraction;

public interface ILogicCodec;

public interface ILogicCodec<TWire> : ILogicCodec
{
     TWire Serialize(ILogic logic);
     ILogic Deserialize(TWire data);
}