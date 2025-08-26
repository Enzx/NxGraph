using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization.Tests;

public class DummyLogicIntCodec : ILogicCodec<int>
{
    public ILogic Deserialize(int _) => new DummyState { Data = "X" };
    public int Serialize(ILogic _) => 42;
}