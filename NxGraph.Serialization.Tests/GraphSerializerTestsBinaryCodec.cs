using NxGraph.Authoring;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

[TestFixture]
[Category("serialization")]
public class GraphSerializerTestsBinaryCodec
{
    private readonly GraphSerializer _serializer = new(new DummyLogicBinaryCodec());


    private static Graph BuildPair(string a, string b)
        => GraphBuilder.StartWithAsync(new DummyState { Data = a }).ToAsync(new DummyState { Data = b }).Build();

    [Test]
    public async Task Binary_roundtrip_preserves_structure_and_logic()
    {
        Graph graph = BuildPair("start", "end");
        await using MemoryStream ms = new();
        await _serializer.ToBinaryAsync(graph, ms);
        ms.Position = 0;

        Graph roundTripped = await _serializer.FromBinaryAsync(ms);
        INode startNode = roundTripped.StartNode;
        LogicNode start = (LogicNode)startNode;
        INode endNode = roundTripped.GetNodeByIndex(1);
        LogicNode end = (LogicNode)endNode;
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.NodeCount, Is.EqualTo(2));
            Assert.That(((DummyState)start.AsyncLogic).Data, Is.EqualTo("start"));
            Assert.That(((DummyState)end.AsyncLogic).Data, Is.EqualTo("end"));
        });
    }


    [Test]
    public async Task Streams_are_left_open_after_binary_helpers()
    {
        Graph graph = BuildPair("x", "y");

        await using MemoryStream s1 = new();
        await _serializer.ToBinaryAsync(graph, s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x7F)); // still open

        s1.Position = 0;
        await _serializer.FromBinaryAsync(s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x01)); // still open
    }

    [Test]
    public async Task Binary_codec_roundtrips_a_graph_with_a_subgraph()
    {
        // Regression: the nested-machine marker is written as a text node regardless of the
        // configured codec, but the read side used to throw "no ILogicCodec<string>" before
        // reaching the marker check — so a binary-codec serializer could write subgraph
        // payloads it could not read back.
        Graph child = GraphBuilder.StartWithAsync(new DummyState { Data = "child" }).Build();
        Graph parent = GraphBuilder
            .StartWithAsync(new DummyState { Data = "parent" })
            .SubGraph(child)
            .Build();

        await using MemoryStream ms = new();
        await _serializer.ToBinaryAsync(parent, ms);
        ms.Position = 0;

        Graph roundTripped = await _serializer.FromBinaryAsync(ms);
        LogicNode owner = (LogicNode)roundTripped.GetNodeByIndex(1);
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.NodeCount, Is.EqualTo(2));
            Assert.That(owner.AsyncLogic, Is.InstanceOf<AsyncStateMachine>());
            Assert.That(((AsyncStateMachine)owner.AsyncLogic).Graph.NodeCount, Is.EqualTo(1));
        });
    }
}