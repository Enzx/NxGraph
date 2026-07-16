using System.Text;
using NxGraph.Authoring;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Per-node stable UIDs on the wire: a sparse <c>uids</c> section shipped with payload
/// version 5. Absence encodes "no uid" — pre-v5 payloads read back as uid-free graphs.
/// </summary>
[TestFixture]
[Category("serialization")]
public class NodeUidSerializationTests
{
    private readonly GraphSerializer _serializer = new(new DummyLogicTextCodec());

    // A JSON-encoded DummyState payload for hand-written node literals.
    private const string DummyLogic = "\"{\\\"Data\\\":\\\"x\\\"}\"";

    private static Graph BuildGraphWithUids(Guid startUid, Guid nextUid)
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode((IAsyncLogic)new DummyState { Data = "start" }, isStart: true);
        NodeId next = builder.AddNode((IAsyncLogic)new DummyState { Data = "next" });
        NodeId last = builder.AddNode((IAsyncLogic)new DummyState { Data = "last" });
        builder.AddTransition(start, next);
        builder.AddTransition(next, last);
        builder.SetUid(start, startUid);
        builder.SetUid(next, nextUid);
        return builder.Build(throwOnError: false);
    }

    private static async Task<Graph> FromJson(GraphSerializer serializer, string json)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        return await serializer.FromJsonAsync(source);
    }

    [Test]
    public async Task json_roundtrip_preserves_uids()
    {
        Guid startUid = Guid.NewGuid();
        Guid nextUid = Guid.NewGuid();
        Graph graph = BuildGraphWithUids(startUid, nextUid);

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromJsonAsync(stream);

        Assert.That(roundTripped.Uids, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Uids![0], Is.EqualTo(startUid));
            Assert.That(roundTripped.Uids[1], Is.EqualTo(nextUid));
            Assert.That(roundTripped.Uids[2], Is.EqualTo(Guid.Empty), "Node without a uid stays uid-free.");
            Assert.That(roundTripped.TryGetNodeByUid(nextUid, out INode node), Is.True);
            Assert.That(node.Id.Index, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task binary_roundtrip_preserves_uids()
    {
        Guid startUid = Guid.NewGuid();
        Guid nextUid = Guid.NewGuid();
        Graph graph = BuildGraphWithUids(startUid, nextUid);

        await using MemoryStream stream = new();
        await _serializer.ToBinaryAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromBinaryAsync(stream);

        Assert.That(roundTripped.Uids, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Uids![0], Is.EqualTo(startUid));
            Assert.That(roundTripped.Uids[1], Is.EqualTo(nextUid));
            Assert.That(roundTripped.Uids[2], Is.EqualTo(Guid.Empty), "Node without a uid stays uid-free.");
        });
    }

    [Test]
    public async Task graph_without_uids_roundtrips_with_none()
    {
        GraphBuilder builder = new();
        builder.AddNode((IAsyncLogic)new DummyState { Data = "only" }, isStart: true);
        Graph graph = builder.Build(throwOnError: false);

        await using MemoryStream jsonStream = new();
        await _serializer.ToJsonAsync(graph, jsonStream);
        jsonStream.Position = 0;
        Graph fromJson = await _serializer.FromJsonAsync(jsonStream);

        await using MemoryStream binaryStream = new();
        await _serializer.ToBinaryAsync(graph, binaryStream);
        binaryStream.Position = 0;
        Graph fromBinary = await _serializer.FromBinaryAsync(binaryStream);

        Assert.Multiple(() =>
        {
            Assert.That(fromJson.Uids, Is.Null);
            Assert.That(fromBinary.Uids, Is.Null);
        });
    }

    [Test]
    public async Task uid_payload_carries_the_current_version_and_a_uids_section()
    {
        Guid uid = Guid.NewGuid();
        Graph graph = BuildGraphWithUids(uid, Guid.NewGuid());

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
            Assert.That(json, Does.Contain("\"uids\""));
            Assert.That(json, Does.Contain(uid.ToString("D")), "Uids ride as canonical \"D\" strings.");
        });
    }

    [Test]
    public async Task v4_payload_without_uids_still_reads()
    {
        string json = $$"""
            {
              "version": 4,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogic}} } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [],
              "name": null,
              "index": -1
            }
            """;

        Graph rebuilt = await FromJson(_serializer, json);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.NodeCount, Is.EqualTo(1));
            Assert.That(rebuilt.Uids, Is.Null, "Pre-v5 payloads read as uid-free graphs.");
        });
    }

    [Test]
    public async Task nested_subgraph_uids_roundtrip_independently()
    {
        // Uid uniqueness is per-graph: parent and child may carry the same uid, and each
        // nesting level rides its own uids section on the wire.
        Guid shared = Guid.NewGuid();

        Graph child = GraphBuilder
            .StartWithAsync((IAsyncLogic)new DummyState { Data = "child" })
            .WithUid(shared)
            .Build();
        Graph parent = GraphBuilder
            .StartWithAsync((IAsyncLogic)new DummyState { Data = "parent" })
            .WithUid(shared)
            .SubGraph(child)
            .Build();

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(parent, stream);
        stream.Position = 0;
        Graph rebuilt = await _serializer.FromJsonAsync(stream);

        LogicNode owner = (LogicNode)rebuilt.GetNodeByIndex(1);
        Graph rebuiltChild = ((ISubGraphProvider)owner.AsyncLogic).SubGraphs.First();

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.TryGetNodeByUid(shared, out INode parentNode), Is.True);
            Assert.That(parentNode.Id.Index, Is.EqualTo(0));
            Assert.That(rebuiltChild.TryGetNodeByUid(shared, out INode childNode), Is.True);
            Assert.That(childNode.Id.Index, Is.EqualTo(0));
        });
    }

    [Test]
    public void uid_dto_with_out_of_range_index_is_rejected()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogic}} } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [],
              "uids": [ { "index": 5, "uid": "{{Guid.NewGuid():D}}" } ],
              "name": null,
              "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(_serializer, json));
        Assert.That(ex!.Message, Does.Contain("out of range"));
    }

    [Test]
    public void uid_dto_with_empty_guid_is_rejected()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": {{DummyLogic}} } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [],
              "uids": [ { "index": 0, "uid": "00000000-0000-0000-0000-000000000000" } ],
              "name": null,
              "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(_serializer, json));
        Assert.That(ex!.Message, Does.Contain("Guid.Empty"));
    }
}
