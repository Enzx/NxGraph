using NxGraph.Authoring;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

[TestFixture]
[Category("serialization")]
public class GraphSerializerTestsBinaryCodec
{
    [SetUp]
    public void SetUp() => GraphSerializer.SetLogicCodec(new DummyLogicBinaryCodec());

    private static Graph BuildPair(string a, string b)
        => GraphBuilder.StartWith(new DummyState { Data = a }).To(new DummyState { Data = b }).Build();

    [Test]
    public async Task Binary_roundtrip_preserves_structure_and_logic()
    {
        Graph graph = BuildPair("start", "end");
        await using MemoryStream ms = new();

        await GraphSerializer.ToBinaryAsync(graph, ms);
        ms.Position = 0;

        Graph roundTripped = await GraphSerializer.FromBinaryAsync(ms);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.NodeCount, Is.EqualTo(2));
            Assert.That(((DummyState)roundTripped.StartNode.Logic).Data, Is.EqualTo("start"));
            Assert.That(((DummyState)roundTripped.GetNodeByIndex(1).Logic).Data, Is.EqualTo("end"));
        });
    }
    

    [Test]
    public async Task Streams_are_left_open_after_binary_helpers()
    {
        Graph graph = BuildPair("x", "y");

        await using MemoryStream s1 = new();
        await GraphSerializer.ToBinaryAsync(graph, s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x7F)); // still open

        s1.Position = 0;
        await GraphSerializer.FromBinaryAsync(s1);
        Assert.DoesNotThrow(() => s1.WriteByte(0x01)); // still open
    }
}