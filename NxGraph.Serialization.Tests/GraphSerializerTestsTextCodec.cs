using NxGraph.Authoring;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

[TestFixture]
[Category("serialization")]
public class GraphSerializerTestsTextCodec
{
    private readonly GraphSerializer _serializer = new(new DummyLogicTextCodec());

    private static Graph BuildChain(params string[] labels)
    {
        if (labels is null || labels.Length == 0)
            throw new ArgumentException("Need at least one node label.");

        StateToken builder = GraphBuilder.StartWithAsync(new DummyState { Data = labels[0] });
        for (int i = 1; i < labels.Length; i++)
            builder = builder.ToAsync(new DummyState { Data = labels[i] });
        return builder.Build();
    }

    [Test]
    public async Task Json_roundtrip_preserves_structure_and_logic()
    {
        Graph graph = BuildChain("start", "mid", "end");

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;

        Graph roundTripped = await _serializer.FromJsonAsync(stream);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.NodeCount, Is.EqualTo(3));
            Assert.That(roundTripped.TransitionCount, Is.EqualTo(3));
            Assert.That(((DummyState)((LogicNode)roundTripped.StartNode).AsyncLogic).Data, Is.EqualTo("start"));
            Assert.That(((DummyState)((LogicNode)roundTripped.GetNodeByIndex(1)).AsyncLogic).Data, Is.EqualTo("mid"));
            Assert.That(((DummyState)((LogicNode)roundTripped.GetNodeByIndex(2)).AsyncLogic).Data, Is.EqualTo("end"));
        });

        // Transitions: 0->1, 1->2, 2->Empty (builder ends last edge empty)
        Transition t0 = roundTripped.GetTransitionByIndex(0);
        Transition t1 = roundTripped.GetTransitionByIndex(1);
        Transition t2 = roundTripped.GetTransitionByIndex(2);
        Assert.Multiple(() =>
        {
            Assert.That(t0.IsEmpty, Is.False);
            Assert.That(t0.Destination.Index, Is.EqualTo(1));
            Assert.That(t1.IsEmpty, Is.False);
            Assert.That(t1.Destination.Index, Is.EqualTo(2));
            Assert.That(t2.IsEmpty, Is.True);
        });
    }

    [Test]
    public void Constructor_throws_on_null_codec()
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed
        Assert.Throws<ArgumentNullException>(() => _ = new GraphSerializer(null!));
    }

    [Test]
    public void ArgNull_checks_are_enforced_on_stream_helpers()
    {
        // ReSharper disable NullableWarningSuppressionIsUsed
        Graph graph = BuildChain("one");
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _serializer.ToJsonAsync(graph, destination: null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _serializer.ToJsonAsync(graph: null!, new MemoryStream()));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _serializer.FromJsonAsync(source: null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _serializer.ToBinaryAsync(graph, destination: null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _serializer.ToBinaryAsync(graph: null!, new MemoryStream()));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _serializer.FromBinaryAsync(source: null!));
        // ReSharper restore NullableWarningSuppressionIsUsed
    }

    [Test]
    public async Task Streams_are_left_open_after_json_helpers()
    {
        Graph graph = BuildChain("a", "b");

        await using MemoryStream s1 = new();
        await _serializer.ToJsonAsync(graph, s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x20)); // still open

        s1.Position = 0;
        await _serializer.FromJsonAsync(s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x21)); // still open
    }
}
