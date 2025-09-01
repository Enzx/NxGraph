using NxGraph.Authoring;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

[TestFixture]
[Category("serialization")]
public class GraphSerializerTestsTextCodec
{
    [SetUp]
    public void SetUp() => GraphSerializer.SetLogicCodec(new DummyLogicTextCodec());

    private static Graph BuildChain(params string[] labels)
    {
        if (labels is null || labels.Length == 0)
            throw new ArgumentException("Need at least one node label.");

        StateToken builder = GraphBuilder.StartWith(new DummyState { Data = labels[0] });
        for (int i = 1; i < labels.Length; i++)
            builder = builder.To(new DummyState { Data = labels[i] });
        return builder.Build();
    }

    [Test]
    public async Task Json_roundtrip_preserves_structure_and_logic()
    {
        Graph graph = BuildChain("start", "mid", "end");

        await using MemoryStream stream = new();
        await GraphSerializer.ToJsonAsync(graph, stream);
        stream.Position = 0;

        Graph roundTripped = await GraphSerializer.FromJsonAsync(stream);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.NodeCount, Is.EqualTo(3));
            Assert.That(roundTripped.TransitionCount, Is.EqualTo(3));
            Assert.That(((DummyState)roundTripped.StartNode.Logic).Data, Is.EqualTo("start"));
            Assert.That(((DummyState)roundTripped.GetNodeByIndex(1).Logic).Data, Is.EqualTo("mid"));
            Assert.That(((DummyState)roundTripped.GetNodeByIndex(2).Logic).Data, Is.EqualTo("end"));
        });

        // Transitions: 0->1, 1->2, 2->Empty (builder usually ends last edge empty)
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
    public void SetLogicCodec_throws_on_null()
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed
        Assert.Throws<ArgumentNullException>(() => GraphSerializer.SetLogicCodec<string>(null!));
    }

    [Test]
    public void ArgNull_checks_are_enforced_on_stream_helpers()
    {
        // ReSharper disable NullableWarningSuppressionIsUsed
        Graph graph = BuildChain("one");
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await GraphSerializer.ToJsonAsync(graph, destination: null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await GraphSerializer.ToJsonAsync(graph: null!, new MemoryStream()));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await GraphSerializer.FromJsonAsync(source: null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await GraphSerializer.ToBinaryAsync(graph, destination: null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await GraphSerializer.ToBinaryAsync(graph: null!, new MemoryStream()));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await GraphSerializer.FromBinaryAsync(source: null!));
        // ReSharper restore NullableWarningSuppressionIsUsed
    }

    [Test]
    public async Task Streams_are_left_open_after_json_helpers()
    {
        Graph graph = BuildChain("a", "b");

        // ToJsonAsync leaves the stream open
        await using MemoryStream s1 = new();
        await GraphSerializer.ToJsonAsync(graph, s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x20)); // still open

        // FromJsonAsync leaves the stream open
        s1.Position = 0;
        await GraphSerializer.FromJsonAsync(s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x21)); // still open
    }
}