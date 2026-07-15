using System.Text;
using NxGraph.Authoring;
using NxGraph.Fsm.Async;
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
    public async Task Json_payload_includes_serialization_version()
    {
        Graph graph = BuildChain("only");

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
    }

    [Test]
    public void FromJsonAsync_rejects_payload_with_newer_version()
    {
        // Hand-rolled JSON with a version one ahead of what the serializer supports.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version + 1}},
              "nodes": [],
              "transitions": [],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _serializer.FromJsonAsync(source));
    }

    [Test]
    public void FromJsonAsync_rejects_payload_with_duplicate_node_indices()
    {
        // Two nodes both claiming index 0 — would otherwise overwrite a slot and lose routing.
        // The logic field carries a JSON-encoded DummyState so the codec doesn't throw before
        // the duplicate-index check fires on the second iteration.
        const string logic = "\"{\\\"Data\\\":\\\"x\\\"}\"";
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": {{logic}} },
                { "$type": "txt", "index": 0, "name": "b", "logic": {{logic}} }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _serializer.FromJsonAsync(source));
        Assert.That(ex!.Message, Does.Contain("duplicated"));
    }

    [Test]
    public void FromJsonAsync_rejects_payload_with_out_of_range_node_index()
    {
        // Index 5 in a 1-node payload — would have corrupted routing under the old code.
        const string logic = "\"{\\\"Data\\\":\\\"x\\\"}\"";
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 5, "name": "a", "logic": {{logic}} }
              ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _serializer.FromJsonAsync(source));
    }

    [Test]
    public void Serializer_rejects_payloads_exceeding_subgraph_depth_cap()
    {
        // Build a chain of nested AsyncStateMachines deeper than MaxSubGraphDepth (64).
        // The cap exists to prevent stack-overflow on untrusted/deeply nested input.
        AsyncStateMachine current = GraphBuilder
            .StartWithAsync(new DummyState { Data = "leaf" })
            .ToAsyncStateMachine();
        for (int i = 0; i < 100; i++)
        {
            current = GraphBuilder
                .StartWithAsync(current)
                .ToAsyncStateMachine();
        }

        using MemoryStream stream = new();
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _serializer.ToBinaryAsync(current.Graph, stream));
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

    [Test]
    public async Task Sync_nested_state_machine_roundtrips_and_stays_sync_runnable()
    {
        // The wire marker "SyncStateMachine" discriminates sync-nested machines from async
        // ones, so a sync-authored graph deserializes back into a sync-runnable graph.
        Graph child = GraphBuilder
            .StartWith(new DummyState { Data = "c0" })
            .To(new DummyState { Data = "c1" })
            .Build();

        Graph parent = GraphBuilder
            .StartWith(new DummyState { Data = "p0" })
            .SubGraph(NxGraph.Fsm.ParallelStepMode.RunToJoin, child)
            .Build();

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(parent, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());
        stream.Position = 0;

        Graph roundTripped = await _serializer.FromJsonAsync(stream);
        LogicNode composite = (LogicNode)roundTripped.GetNodeByIndex(1);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("SyncStateMachine"));
            Assert.That(composite.Logic, Is.InstanceOf<NxGraph.Fsm.StateMachine>(),
                "The sync marker must rebuild a sync StateMachine node, not an async one.");
        });

        // And the round-tripped graph runs on the sync runtime end to end.
        NxGraph.Fsm.StateMachine machine = new(roundTripped);
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void Dynamic_parallel_composites_still_throw_a_targeted_error()
    {
        // Since v4 the history/parallel composites ride the payload; the dynamic variants
        // cannot (their region selector is a delegate) and must keep the targeted error,
        // which now names the supported set.
        Graph region = GraphBuilder
            .StartWith(new DummyState { Data = "r0" })
            .Build();

        Graph withDynamicParallel = GraphBuilder
            .StartWith(new DummyState { Data = "p0" })
            .Parallel(NxGraph.Fsm.ParallelStepMode.RunToJoin, _ => NxGraph.Fsm.RegionMask.Bit(0), region)
            .Build();

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(
            async () => await _serializer.ToJsonAsync(withDynamicParallel, new MemoryStream()));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("DynamicParallelState"));
            Assert.That(ex.Message, Does.Contain("history composites"),
                "The targeted error names what is supported post-v4.");
        });
    }

    private sealed class RawStringCodec : ILogicTextCodec
    {
        public IAsyncLogic Deserialize(string s) => new DummyState { Data = s };
        public string Serialize(IAsyncLogic data) => ((DummyState)data).Data;
    }

    [Test]
    public async Task Logic_payload_equal_to_the_marker_string_is_not_misread_as_a_subgraph()
    {
        // Regression: subgraph owners were detected purely by comparing the node's logic
        // string against the marker, so a codec legitimately emitting that string for
        // ordinary logic produced an unreadable payload ("marked as a StateMachine but has
        // no associated subgraph"). The marker is now honored only when a subgraph payload
        // actually claims the node index.
        GraphSerializer serializer = new(new RawStringCodec());
        Graph graph = GraphBuilder
            .StartWithAsync(new DummyState { Data = "Default" })
            .ToAsync(new DummyState { Data = "StateMachine" })
            .Build();

        await using MemoryStream stream = new();
        await serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;

        Graph roundTripped = await serializer.FromJsonAsync(stream);
        Assert.Multiple(() =>
        {
            Assert.That(((DummyState)((LogicNode)roundTripped.StartNode).AsyncLogic).Data, Is.EqualTo("Default"));
            Assert.That(((DummyState)((LogicNode)roundTripped.GetNodeByIndex(1)).AsyncLogic).Data,
                Is.EqualTo("StateMachine"));
        });
    }
}
