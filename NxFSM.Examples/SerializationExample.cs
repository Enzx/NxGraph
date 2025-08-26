using NxGraph;
using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Serialization;
using NxGraph.Serialization.Abstraction;

namespace Example;

public class DummyState : ILogic
{
    public string Data { get; set; } = string.Empty;

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }
}

public class DummyLogicSerializer : ILogicTextCodec
{
    public ILogic Deserialize(string s)
    {
        return System.Text.Json.JsonSerializer.Deserialize<DummyState>(s) ?? throw new InvalidOperationException();
    }

    public string Serialize(ILogic data)
    {
        return System.Text.Json.JsonSerializer.Serialize((DummyState)data);
    }
}

public static class SerializationExample
{
    public static async ValueTask Run()
    {
        GraphSerializer.SetLogicCodec(new DummyLogicSerializer());
        DummyState start = new() { Data = "start" };
        DummyState end = new() { Data = "end" };
        Graph graph = GraphBuilder.StartWith(start).To(end).Build();
        MemoryStream writeStream = new();
        MemoryStream readStream = new();
        await GraphSerializer.ToJsonAsync(graph, writeStream);
        byte[] bytes = writeStream.ToArray();
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        Console.WriteLine(json);
        Graph deserializedGraph = await GraphSerializer.FromJsonAsync(readStream);
        
        Node deserializedEndNode = deserializedGraph.GetNodeByIndex(1);
        DummyState deserializedStart = (DummyState)deserializedGraph.StartNode.Logic;
        DummyState deserializedEnd = (DummyState)deserializedEndNode.Logic;
    }
}