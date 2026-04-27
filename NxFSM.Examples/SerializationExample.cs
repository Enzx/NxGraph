using System.Text.Json;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Serialization;
using NxGraph.Serialization.Abstraction;

namespace NxFSM.Examples;

// ── Logic type that the codec serializes ─────────────────────────────────
public sealed class ExampleState : IAsyncLogic
{
    public string Data { get; set; } = string.Empty;

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
        => ResultHelpers.Success;
}

// ── Text codec: serializes / deserializes ExampleState via System.Text.Json ──
public sealed class ExampleLogicCodec : ILogicTextCodec
{
    public string Serialize(IAsyncLogic asyncLogic)
        => JsonSerializer.Serialize((ExampleState)asyncLogic);

    public IAsyncLogic Deserialize(string payload)
        => JsonSerializer.Deserialize<ExampleState>(payload)
           ?? throw new InvalidOperationException("Failed to deserialize ExampleState.");
}

public static class SerializationExample
{
    public static async ValueTask Run()
    {
        Console.WriteLine("=== Serialization Example ===");

        Graph graph = GraphBuilder
            .StartWithAsync(new ExampleState { Data = "start" }).SetName("Start")
            .ToAsync(new ExampleState { Data = "end" }).SetName("End")
            .Build()
            .SetName("ExampleGraph");

        GraphSerializer serializer = new(new ExampleLogicCodec());

        await using MemoryStream stream = new();
        await serializer.ToJsonAsync(graph, stream);

        string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Console.WriteLine($"Serialized ({json.Length} bytes):");
        Console.WriteLine(json);

        stream.Position = 0;
        Graph roundTripped = await serializer.FromJsonAsync(stream);

        Result result = await roundTripped.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Round-tripped FSM result: {result}");
    }
}
