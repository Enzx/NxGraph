using System.Buffers;
using System.Text;
using MessagePack;
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
    public void Dynamic_parallel_composites_without_a_selector_registry_throw_a_targeted_error()
    {
        // Since v6 the dynamic parallel composites ride the payload, but only with a selector
        // registry configured — a plain GraphSerializer(codec) keeps a targeted error that
        // points at the option unlocking the feature.
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
            Assert.That(ex!.Message, Does.Contain("dynamic parallel composite"));
            Assert.That(ex.Message, Does.Contain("GraphSerializerOptions.SelectorRegistry"),
                "The targeted error names the option that unlocks the feature.");
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

    // ── Consistency-pass guards: duplicate subgraph claims, version floor, canonical order ──

    private const string DummyLogicJson = "\"{\\\"Data\\\":\\\"x\\\"}\"";

    [Test]
    public void FromJsonAsync_rejects_payload_with_duplicate_subgraph_owner()
    {
        // Two subgraph payloads claiming the same owner node: the first used to be silently
        // dropped (HashSet dedup on the claims pass, dictionary overwrite on the rebuild
        // pass); the SubGraphs section now has the same duplicate-claim guard as every
        // sibling section.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogicJson}} },
                { "$type": "txt", "index": 1, "name": "sub", "logic": "StateMachine" }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [
                { "ownerIndex": 1,
                  "graph": { "version": {{SerializationVersion.Version}},
                             "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": {{DummyLogicJson}} } ],
                             "transitions": [ { "destination": -1 } ],
                             "subGraphs": [], "name": null, "index": -1 } },
                { "ownerIndex": 1,
                  "graph": { "version": {{SerializationVersion.Version}},
                             "nodes": [ { "$type": "txt", "index": 0, "name": "c1", "logic": {{DummyLogicJson}} } ],
                             "transitions": [ { "destination": -1 } ],
                             "subGraphs": [], "name": null, "index": -1 } }
              ],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _serializer.FromJsonAsync(source));
        Assert.That(ex!.Message, Does.Contain("Subgraph DTO owner index 1 is duplicated"));
    }

    [Test]
    public void FromJsonAsync_rejects_payload_without_a_version()
    {
        // No "version" property at all: GraphDto.Version defaults to 0 (not the current
        // version), so a version-stripped payload cannot pass as current and sail past the
        // newer-than-supported gate.
        string json = $$"""
            {
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogicJson}} } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _serializer.FromJsonAsync(source));
        Assert.That(ex!.Message, Does.Contain("carries no version"));
    }

    [Test]
    public void FromJsonAsync_rejects_payload_with_version_zero()
    {
        string json = $$"""
            {
              "version": 0,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogicJson}} } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _serializer.FromJsonAsync(source));
        Assert.That(ex!.Message, Does.Contain("carries no version"));
    }

    [Test]
    public void FromJsonAsync_rejects_nested_subgraph_payload_without_a_version()
    {
        // The version floor is enforced per nesting level, like the newer-than gate: a child
        // graph with its "version" stripped must not read as current just because the parent
        // carries one.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogicJson}} },
                { "$type": "txt", "index": 1, "name": "sub", "logic": "StateMachine" }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [
                { "ownerIndex": 1,
                  "graph": { "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": {{DummyLogicJson}} } ],
                             "transitions": [ { "destination": -1 } ],
                             "subGraphs": [], "name": null, "index": -1 } }
              ],
              "name": null,
              "index": -1
            }
            """;

        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _serializer.FromJsonAsync(source));
        Assert.That(ex!.Message, Does.Contain("carries no version"));
    }

    [Test]
    public void FromJsonAsync_rejects_payload_with_non_canonical_node_order()
    {
        // Positions swapped: logic would still land correctly (it is installed by Index), but
        // the rebuild passes re-read node names positionally (dto.Nodes[ownerIndex]), so
        // canonical order (Nodes[i].Index == i) is required for the payload to be sound.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 1, "name": "b", "logic": {{DummyLogicJson}} },
                { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogicJson}} }
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
        Assert.That(ex!.Message, Does.Contain("canonical order"));
    }

    [Test]
    public void Serialization_version_only_moves_with_a_deliberate_format_addition()
    {
        // Read-side hardening (duplicate-claim guards, version floor, canonical order, header
        // drains) must never move the payload version — the project bumps it only for
        // structural format additions. Update this pin consciously, together with the
        // changelog comment in SerializationVersion.cs, when such an addition ships.
        Assert.That(SerializationVersion.Version, Is.EqualTo(9));
    }

    // ── Crafted MessagePack: inflated header counts must drain, not desync ──

    private static void WriteTextNode(ref MessagePackWriter writer, int index, string name, string logic)
    {
        writer.WriteArrayHeader(4); // [Type, Index, Name, Logic]
        writer.Write(0); // 0 = text node
        writer.Write(index);
        writer.Write(name);
        writer.Write(logic);
    }

    private static void WriteTransition(ref MessagePackWriter writer, int destination)
    {
        writer.WriteArrayHeader(2); // [Destination, FailureDestination]
        writer.Write(destination);
        writer.Write(-1);
    }

    /// <summary>
    /// A parent graph whose nested subgraph's GraphDto array header declares one element more
    /// than the current 16-element shape (the 16 known fields plus one trailing nil). The
    /// parent carries a retry policy <i>after</i> the subgraph section, so its reads only stay
    /// aligned if the child read drains the extra element instead of leaving it in the stream.
    /// </summary>
    private static byte[] CraftBinaryPayloadWithInflatedChildHeader()
    {
        ArrayBufferWriter<byte> buffer = new();
        MessagePackWriter writer = new(buffer);

        // Parent GraphDto: the regular 16-element v9 shape.
        writer.WriteArrayHeader(16);
        writer.Write(SerializationVersion.Version); // 0. version
        writer.Write(-1); // 1. index
        writer.WriteNil(); // 2. name
        writer.WriteArrayHeader(2); // 3. nodes
        WriteTextNode(ref writer, 0, "a", "{\"Data\":\"a\"}");
        WriteTextNode(ref writer, 1, "sub", "StateMachine");
        writer.WriteArrayHeader(2); // 4. transitions
        WriteTransition(ref writer, 1);
        WriteTransition(ref writer, -1);
        writer.WriteArrayHeader(1); // 5. subGraphs
        writer.WriteArrayHeader(2); //    SubGraphDto: [OwnerIndex, GraphDto]
        writer.Write(1);

        // Child GraphDto with an inflated header: 17 declared elements.
        writer.WriteArrayHeader(17);
        writer.Write(SerializationVersion.Version);
        writer.Write(-1);
        writer.WriteNil();
        writer.WriteArrayHeader(1); // nodes
        WriteTextNode(ref writer, 0, "c", "{\"Data\":\"c\"}");
        writer.WriteArrayHeader(1); // transitions
        WriteTransition(ref writer, -1);
        for (int section = 0; section < 11; section++)
        {
            writer.WriteArrayHeader(0); // subGraphs .. behaviors, all empty
        }

        writer.WriteNil(); // the unknown 17th element the reader must drain

        // Parent sections 6..15: a real retry policy first, then the rest empty. Without the
        // child drain, this policy would be read one slot late and misparse.
        writer.WriteArrayHeader(1); // 6. retryPolicies
        writer.WriteArrayHeader(4); //    [Index, MaxAttempts, BackoffTicks, BackoffKind]
        writer.Write(0);
        writer.Write(2);
        writer.Write(0L);
        writer.Write(0);
        for (int section = 0; section < 9; section++)
        {
            writer.WriteArrayHeader(0); // 7. outcomeCodes .. 15. behaviors, all empty
        }

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    [Test]
    public async Task Crafted_binary_payload_with_inflated_child_header_is_drained_not_desynced()
    {
        // A header count above the known shape (while the version is still current) used to
        // leave the reader mid-array after the nested graph, desyncing every subsequent read
        // of the parent. The reader now drains unknown trailing elements, so the parent's
        // sections after the subgraph still parse correctly.
        byte[] payload = CraftBinaryPayloadWithInflatedChildHeader();

        using MemoryStream source = new(payload);
        Graph rebuilt = await _serializer.FromBinaryAsync(source);

        LogicNode owner = (LogicNode)rebuilt.GetNodeByIndex(1);
        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.NodeCount, Is.EqualTo(2));
            Assert.That(owner.AsyncLogic, Is.InstanceOf<AsyncStateMachine>(),
                "The nested-machine claim must still resolve after the child read.");
            Assert.That(((AsyncStateMachine)owner.AsyncLogic).Graph.NodeCount, Is.EqualTo(1));
            Assert.That(rebuilt.RetryPolicies, Is.Not.Null,
                "The parent's post-subgraph sections must still line up — no reader desync.");
            Assert.That(rebuilt.RetryPolicies![0].MaxAttempts, Is.EqualTo(2));
        });
    }
}
