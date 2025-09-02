using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Serialization;

namespace NxFSM.Examples;

public class ExampleState : ILogic
{
    public string Data { get; set; } = string.Empty;

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }
}

public class ExampleLogicSerializer : ILogicTextCodec
{
    public ILogic Deserialize(string s)
    {
        return System.Text.Json.JsonSerializer.Deserialize<ExampleState>(s) ?? throw new InvalidOperationException();
    }

    public string Serialize(ILogic data)
    {
        return System.Text.Json.JsonSerializer.Serialize((ExampleState)data);
    }
}

public static class SerializationExample
{
    public static async ValueTask Run()
    {
        GraphSerializer.SetLogicCodec(new ExampleLogicSerializer());
        ExampleState start = new() { Data = "start" };
        ExampleState end = new() { Data = "end" };
        Graph graph = GraphBuilder.StartWith(start).SetName("Start").To(end).SetName("End").Build().SetName("FSM");
        MemoryStream stream = new();
        await GraphSerializer.ToJsonAsync(graph, stream);
        byte[] bytes = stream.ToArray();
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        Console.WriteLine(json);
        stream.Position = 0;
        Graph deserializedGraph = await GraphSerializer.FromJsonAsync(stream);

        StateMachine fsm = deserializedGraph.ToStateMachine();
        await fsm.ExecuteAsync();


    }
}