using System.Text;
using NxGraph.Authoring;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

[TestFixture]
[Category("serialization")]
public class FailureEdgeSerializationTests
{
    private readonly GraphSerializer _serializer = new(new DummyLogicTextCodec());

    private static Graph BuildGraphWithFailureEdge()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode((IAsyncLogic)new DummyState { Data = "work" }, isStart: true);
        NodeId next = builder.AddNode((IAsyncLogic)new DummyState { Data = "done" });
        NodeId handler = builder.AddNode((IAsyncLogic)new DummyState { Data = "cleanup" });
        builder.AddTransition(start, next);
        builder.AddFailureTransition(start, handler);
        return builder.Build(throwOnError: false);
    }

    [Test]
    public async Task json_roundtrip_preserves_failure_edges()
    {
        Graph graph = BuildGraphWithFailureEdge();

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromJsonAsync(stream);

        Transition edge = roundTripped.GetTransitionByIndex(0);
        Assert.Multiple(() =>
        {
            Assert.That(edge.Destination.Index, Is.EqualTo(1));
            Assert.That(edge.HasFailureDestination, Is.True);
            Assert.That(edge.FailureDestination.Index, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task binary_roundtrip_preserves_failure_edges()
    {
        Graph graph = BuildGraphWithFailureEdge();

        await using MemoryStream stream = new();
        await _serializer.ToBinaryAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromBinaryAsync(stream);

        Transition edge = roundTripped.GetTransitionByIndex(0);
        Assert.Multiple(() =>
        {
            Assert.That(edge.HasFailureDestination, Is.True);
            Assert.That(edge.FailureDestination.Index, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task version1_json_without_failure_destination_still_loads()
    {
        const string logic = "\"{\\\"Data\\\":\\\"x\\\"}\"";
        string json = $$"""
            {
              "version": 1,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": {{logic}} },
                { "$type": "txt", "index": 1, "name": "b", "logic": {{logic}} }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        Graph graph = await _serializer.FromJsonAsync(source);

        Transition edge = graph.GetTransitionByIndex(0);
        Assert.Multiple(() =>
        {
            Assert.That(edge.Destination.Index, Is.EqualTo(1));
            Assert.That(edge.HasFailureDestination, Is.False);
        });
    }

    [Test]
    public void out_of_range_failure_destination_is_rejected()
    {
        const string logic = "\"{\\\"Data\\\":\\\"x\\\"}\"";
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": {{logic}} }
              ],
              "transitions": [ { "destination": -1, "failureDestination": 9 } ],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _serializer.FromJsonAsync(source));
    }
}
