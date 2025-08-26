using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

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
    public void ToDto_emits_binary_nodes_when_binary_codec_is_set()
    {
        Graph graph = BuildPair("p", "q");
        GraphDto dto = GraphSerializer.ToDto(graph);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Nodes, Has.Length.EqualTo(2));
            Assert.That(dto.Nodes[0], Is.TypeOf<NodeBinaryDto>());
            Assert.That(dto.Nodes[1], Is.TypeOf<NodeBinaryDto>());
        });
    }

    [Test]
    public void FromDto_with_binary_nodes_requires_binary_codec()
    {
        // Build a DTO with binary nodes but then install a text codec -> should throw (invalid cast)
        INodeDto[] nodes =
        [
            new NodeBinaryDto("A", """{"Data":"one"}"""u8.ToArray()),
            new NodeBinaryDto("B", """{"Data":"two"}"""u8.ToArray())
        ];
        GraphDto dto = new(nodes, [new TransitionDto(1), new TransitionDto(-1)], "Bin");

        // Flip to text codec to force mismatch
        GraphSerializer.SetLogicCodec(new DummyLogicTextCodec());

        Assert.Throws<InvalidCastException>(() => GraphSerializer.FromDto(dto));
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