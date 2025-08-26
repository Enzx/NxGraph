using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

public class DummyLogicTextCodec : ILogicTextCodec
{
    public ILogic Deserialize(string s)
        => System.Text.Json.JsonSerializer.Deserialize<DummyState>(s)
           ?? throw new InvalidOperationException("Failed to deserialize DummyState from text.");

    public string Serialize(ILogic data)
        => System.Text.Json.JsonSerializer.Serialize((DummyState)data);
}