using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

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
    public void ToDto_emits_text_nodes_and_correct_transitions()
    {
        Graph graph = BuildChain("a", "b", "c");
        GraphDto dto = GraphSerializer.ToDto(graph);

        Assert.That(dto.Nodes, Has.Length.EqualTo(3));
        Assert.That(dto.Transitions, Has.Length.EqualTo(3));

        // Node DTO types
        Assert.That(dto.Nodes[0], Is.TypeOf<NodeTextDto>());
        Assert.That(dto.Nodes[1], Is.TypeOf<NodeTextDto>());
        Assert.That(dto.Nodes[2], Is.TypeOf<NodeTextDto>());

        // Transition destinations: 0->1, 1->2, 2->-1
        Assert.Multiple(() =>
        {
            Assert.That(dto.Transitions[0].Destination, Is.EqualTo(1));
            Assert.That(dto.Transitions[1].Destination, Is.EqualTo(2));
            Assert.That(dto.Transitions[2].Destination, Is.EqualTo(-1));
        });
    }

    [Test]
    public void FromDto_ignores_invalid_or_negative_destinations()
    {
        // Create a mixed transition set: -1 (empty), 99 (out-of-range), 1 (valid)
        INodeDto[] nodes =
        [
            new NodeTextDto("n0", "{}"),
            new NodeTextDto("n1", "{}"),
            new NodeTextDto("n2", "{}")
        ];

        GraphDto dto = new(
            nodes,
            [
                new TransitionDto(-1), // empty
                new TransitionDto(99), // ignored -> empty
                new TransitionDto(1) // valid
            ],
            "MyGraph"
        );

        Graph graph = GraphSerializer.FromDto(dto);

        Assert.That(graph.Id.Name, Is.EqualTo("MyGraph"));

        Transition t0 = graph.GetTransitionByIndex(0);
        Transition t1 = graph.GetTransitionByIndex(1);
        Transition t2 = graph.GetTransitionByIndex(2);

        Assert.Multiple(() =>
        {
            Assert.That(t0.IsEmpty, Is.True);
            Assert.That(t1.IsEmpty, Is.True);
            Assert.That(t2.IsEmpty, Is.False);
            Assert.That(t2.Destination.Index, Is.EqualTo(1));
        });
    }

    [Test]
    public void FromDto_handles_shorter_transition_array()
    {
        // 3 nodes, only 1 transition supplied; others should default to Empty
        INodeDto[] nodes =
        [
            new NodeTextDto("n0", "{}"),
            new NodeTextDto("n1", "{}"),
            new NodeTextDto("n2", "{}")
        ];
        GraphDto dto = new(nodes, [
                new TransitionDto(1),
                new TransitionDto(NodeId.Default.Index),
                new TransitionDto(NodeId.Default.Index)
            ],
            "Shorty");

        Graph graph = GraphSerializer.FromDto(dto);

        Assert.That(graph.TransitionCount, Is.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(graph.GetTransitionByIndex(0).IsEmpty, Is.False);
            Assert.That(graph.GetTransitionByIndex(0).Destination.Index, Is.EqualTo(1));
            Assert.That(graph.GetTransitionByIndex(1).IsEmpty, Is.True);
            Assert.That(graph.GetTransitionByIndex(2).IsEmpty, Is.True);
        });
    }

    [Test]
    public void GraphDto_should_throw_exception_for_mismatch_nodes_edges_length()
    {
        INodeDto[] nodes =
        [
            new NodeTextDto("n0", "{}"),
            new NodeTextDto("n1", "{}"),
            new NodeTextDto("n2", "{}")
        ];
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new GraphDto(nodes, [
                    new TransitionDto(1),
                ],
                "ExceptionCase");
        });
    }


    [Test]
    public void FromDto_throws_when_no_nodes()
    {
        GraphDto dto = new([], [], "Empty");
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.FromDto(dto));
    }

    [Test]
    public void SetLogicCodec_throws_on_null()
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed
        Assert.Throws<ArgumentNullException>(() => GraphSerializer.SetLogicCodec<string>(null!));
    }

    [Test]
    public void ToDto_throws_with_incompatible_codec_type()
    {
        // Install an incompatible codec (int), ToDto should fail with "No ILogicCodec configured..."
        GraphSerializer.SetLogicCodec(new DummyLogicIntCodec());

        Graph graph = BuildChain("x", "y");
        InvalidOperationException?
            ex = Assert.Throws<InvalidOperationException>(() => GraphSerializer.ToDto(graph));
        StringAssert.Contains("No ILogicCodec configured", ex.Message);
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