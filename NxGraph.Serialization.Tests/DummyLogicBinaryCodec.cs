using System.Text;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

public class DummyLogicBinaryCodec : ILogicBinaryCodec
{
    public ILogic Deserialize(ReadOnlyMemory<byte> payload)
    {
        string json = Encoding.UTF8.GetString(payload.Span);
        return System.Text.Json.JsonSerializer.Deserialize<DummyState>(json)
               ?? throw new InvalidOperationException("Failed to deserialize DummyState from bytes.");
    }

    public ReadOnlyMemory<byte> Serialize(ILogic data)
    {
        string json = System.Text.Json.JsonSerializer.Serialize((DummyState)data);
        return Encoding.UTF8.GetBytes(json);
    }
}