using System.Text;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 6, part A: token fork/join nodes on the wire. Fork branch arrays and join
/// policies are plain structure and serialize unconditionally — no options needed. Covers
/// round-trips per join policy (JSON + MessagePack) re-run under both token machines, branch
/// order, nesting, UID coexistence, marker-spoof defense, and negative fixtures.
/// </summary>
[TestFixture]
[Category("serialization")]
public class ForkJoinSerializationTests
{
    // ── Test doubles (CompositeSerializationTests pattern) ───────────────

    private interface IKeyed
    {
        string Key { get; }
    }

    private sealed class KeyedState(string key, Func<Result> body) : IAsyncLogic, ILogic, IKeyed
    {
        public string Key => key;
        public Result Execute() => body();
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => new(body());
    }

    private sealed class RegistryCodec : ILogicTextCodec
    {
        private readonly Dictionary<string, IAsyncLogic> _byKey = new();

        public T Register<T>(string key, T logic) where T : IAsyncLogic
        {
            _byKey[key] = logic;
            return logic;
        }

        public KeyedState Log(string key, List<string> log)
            => Register(key, new KeyedState(key, () =>
            {
                log.Add(key);
                return Result.Success;
            }));

        public string Serialize(IAsyncLogic data) => ((IKeyed)data).Key;

        public IAsyncLogic Deserialize(string s) => _byKey.TryGetValue(s, out IAsyncLogic? logic)
            ? logic
            : throw new InvalidOperationException($"Unknown logic key '{s}'.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<Graph> RoundTrip(GraphSerializer serializer, Graph graph, bool binary)
    {
        await using MemoryStream stream = new();
        if (binary)
        {
            await serializer.ToBinaryAsync(graph, stream);
            stream.Position = 0;
            return await serializer.FromBinaryAsync(stream);
        }

        await serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;
        return await serializer.FromJsonAsync(stream);
    }

    private static Result RunSyncToCompletion(TokenMachine machine)
    {
        Result result = Result.InProgress;
        for (int guard = 0; guard < 1_000 && result == Result.InProgress; guard++)
        {
            result = machine.Execute();
        }

        return result;
    }

    private static async Task<Graph> FromJson(GraphSerializer serializer, string json)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        return await serializer.FromJsonAsync(source);
    }

    /// <summary>load → fork(a → join → finish, b → join), async-authored.</summary>
    private static Graph Diamond(RegistryCodec codec, List<string> log, JoinPolicy policy)
    {
        JoinState join = new(policy);
        return GraphBuilder.StartWithAsync(codec.Log("load", log))
            .ForkTo(
                b => b.ToAsync(codec.Log("a", log)).To(join).ToAsync(codec.Log("finish", log)),
                b => b.ToAsync(codec.Log("b", log)).To(join))
            .Build();
    }

    // ── Round-trips, re-run under both runtimes ──────────────────────────

    [Test]
    public async Task Fork_join_diamond_roundtrips_and_reruns_under_the_async_token_machine([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();
        Graph graph = Diamond(codec, log, JoinPolicy.All(2));

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary);

        // Diamond indexing: 0=load, branch heads build first (1=a, 2=join, 3=finish, 4=b),
        // the fork node itself is added last (5).
        LogicNode forkNode = (LogicNode)rebuilt.GetNodeByIndex(5);
        ForkState fork = (forkNode.Logic as ForkState ?? forkNode.AsyncLogic as ForkState)!;
        Result result = await rebuilt.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fork.Branches, Has.Count.EqualTo(2), "Both branches survived the trip.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }),
                "Both branch states executed and the joined token ran the tail — branch 0 " +
                "continues the arriving token, so 'a' precedes 'b' in the round interleaving.");
        });
    }

    [Test]
    public async Task Fork_join_diamond_roundtrips_and_reruns_under_the_sync_token_machine([Values] bool binary)
    {
        // Sync-authored: branch heads and the join arrive through the ForkBranch.To(ILogic)
        // path (join wrapped in SyncLogicAdapter on the wire side). One marker per kind — the
        // rebuilt join carries no adapter, which is semantics-preserving because fork/join
        // never execute and populate both logic slots.
        List<string> log = [];
        RegistryCodec codec = new();
        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder.StartWith((ILogic)codec.Log("load", log))
            .ForkTo(
                b => b.To((ILogic)codec.Log("a", log)).To(join).To((ILogic)codec.Log("finish", log)),
                b => b.To((ILogic)codec.Log("b", log)).To(join))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary);
        Result result = RunSyncToCompletion(rebuilt.ToTokenMachine());

        LogicNode joinNode = (LogicNode)rebuilt.GetNodeByIndex(2);
        Assert.Multiple(() =>
        {
            Assert.That(joinNode.Logic, Is.InstanceOf<JoinState>(),
                "The rebuilt join populates the sync logic slot, so it stays sync-runnable.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }));
        });
    }

    [TestCase((byte)JoinKind.All, 2)]
    [TestCase((byte)JoinKind.Any, 1)]
    [TestCase((byte)JoinKind.Quorum, 2)]
    public async Task Join_policy_roundtrips_with_exact_kind_and_count(byte kind, int count)
    {
        JoinPolicy policy = (JoinKind)kind switch
        {
            JoinKind.All => JoinPolicy.All(count),
            JoinKind.Any => JoinPolicy.Any,
            _ => JoinPolicy.Quorum(count),
        };

        List<string> log = [];
        RegistryCodec codec = new();
        Graph graph = Diamond(codec, log, policy);

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: true);
        LogicNode joinNode = (LogicNode)rebuilt.GetNodeByIndex(2);
        JoinState join = (joinNode.Logic as JoinState ?? joinNode.AsyncLogic as JoinState)!;

        Assert.Multiple(() =>
        {
            Assert.That(join.Policy.Kind, Is.EqualTo((JoinKind)kind));
            Assert.That(join.Policy.Count, Is.EqualTo(count));
        });
    }

    [Test]
    public async Task Fork_as_start_node_roundtrips([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();
        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder.Start()
            .ForkTo(
                b => b.ToAsync(codec.Log("a", log)).To(join),
                b => b.ToAsync(codec.Log("b", log)).To(join))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary);
        LogicNode startNode = (LogicNode)rebuilt.StartNode;
        Result result = await rebuilt.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(startNode.Logic as ForkState ?? startNode.AsyncLogic as ForkState, Is.Not.Null,
                "The start node is a fork — the root token fans out immediately at run start.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    // ── Nesting and section coexistence ──────────────────────────────────

    [Test]
    public async Task Fork_join_inside_a_subgraph_payload_roundtrips([Values] bool binary)
    {
        // Sections are per-GraphDto and recurse for free — the nested machine's payload
        // carries its own fork/join sections.
        List<string> log = [];
        RegistryCodec codec = new();
        Graph child = Diamond(codec, log, JoinPolicy.All(2));
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Log("p0", log))
            .SubGraph(child)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode owner = (LogicNode)rebuilt.GetNodeByIndex(1);
        Graph rebuiltChild = ((NxGraph.Fsm.Async.AsyncStateMachine)owner.AsyncLogic).Graph;
        Result result = await rebuiltChild.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }));
        });
    }

    [Test]
    public async Task Fork_join_inside_a_composite_region_roundtrips()
    {
        List<string> log = [];
        RegistryCodec codec = new();
        Graph region = Diamond(codec, log, JoinPolicy.All(2));
        Graph parent = GraphBuilder.Start().Parallel(region).Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);
        LogicNode composite = (LogicNode)rebuilt.StartNode;
        var parallel = (NxGraph.Fsm.Async.AsyncParallelState)composite.AsyncLogic;
        Result result = await parallel.Regions[0].Graph.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }));
        });
    }

    [Test]
    public async Task Fork_node_carrying_a_uid_roundtrips()
    {
        Guid uid = Guid.NewGuid();
        List<string> log = [];
        RegistryCodec codec = new();
        JoinState join = new(JoinPolicy.All(2));
        StateToken load = GraphBuilder.StartWithAsync(codec.Log("load", log));
        ForkToken fork = load.ForkTo(
            b => b.ToAsync(codec.Log("a", log)).To(join),
            b => b.ToAsync(codec.Log("b", log)).To(join));
        fork.Builder.SetUid(fork.Id, uid);
        Graph graph = fork.Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: false);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.TryGetNodeByUid(uid, out INode node), Is.True,
                "The sparse uid section coexists with the fork section on the same node index.");
            Assert.That(((LogicNode)node).Logic as ForkState ?? ((LogicNode)node).AsyncLogic as ForkState,
                Is.Not.Null, "The uid-carrying node is the rebuilt fork.");
        });
    }

    [Test]
    public async Task Deserialized_fork_keeps_its_empty_transition_slot_and_matches_lints_and_mermaid()
    {
        List<string> log = [];
        RegistryCodec codec = new();
        Graph graph = Diamond(codec, log, JoinPolicy.All(2));

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: false);

        GraphValidationResult originalReport = graph.Validate();
        GraphValidationResult rebuiltReport = rebuilt.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.GetTransitionByIndex(5).IsEmpty, Is.True,
                "The fork's transition slot stays empty — branches replace the success edge.");
            Assert.That(rebuiltReport.Diagnostics.Select(i => i.ToString()),
                Is.EqualTo(originalReport.Diagnostics.Select(i => i.ToString())),
                "The validator token lints on the rebuilt graph match the original.");
            Assert.That(rebuilt.ToMermaid(), Is.EqualTo(graph.ToMermaid()),
                "Mermaid output (fork bars, branch edges, policy-labeled join) survives the trip.");
        });
    }

    // ── Marker-spoof defense and negative fixtures ───────────────────────

    [Test]
    public async Task Fork_marker_string_in_ordinary_logic_is_not_honored()
    {
        // A codec legitimately emitting "ForkState"/"JoinState" for ordinary logic must
        // round-trip as ordinary logic: markers are honored only when the matching section
        // claims the node index.
        RegistryCodec codec = new();
        codec.Register("ForkState", new KeyedState("ForkState", () => Result.Success));
        codec.Register("JoinState", new KeyedState("JoinState", () => Result.Success));

        Graph graph = GraphBuilder
            .StartWithAsync(codec.Deserialize("ForkState"))
            .ToAsync(codec.Deserialize("JoinState"))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: false);

        Assert.Multiple(() =>
        {
            Assert.That(((LogicNode)rebuilt.StartNode).AsyncLogic, Is.Not.InstanceOf<ForkState>());
            Assert.That(((IKeyed)((LogicNode)rebuilt.StartNode).AsyncLogic).Key, Is.EqualTo("ForkState"));
            Assert.That(((IKeyed)((LogicNode)rebuilt.GetNodeByIndex(1)).AsyncLogic).Key, Is.EqualTo("JoinState"));
        });
    }

    private const string TwoPlainNodesJson = """
          "nodes": [
            { "$type": "txt", "index": 0, "name": "a", "logic": "ForkState" },
            { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
          ],
          "transitions": [ { "destination": -1 }, { "destination": -1 } ],
          "subGraphs": [], "composites": [],
        """;

    private static GraphSerializer PlainSerializer()
    {
        RegistryCodec codec = new();
        codec.Register("a", new KeyedState("a", () => Result.Success));
        codec.Register("b", new KeyedState("b", () => Result.Success));
        return new GraphSerializer(codec);
    }

    [Test]
    public void Fork_claim_on_a_non_marker_node_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "a" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "forks": [ { "ownerIndex": 0, "branches": [ 1 ] } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("does not reference a fork marker"));
    }

    [Test]
    public void Duplicate_fork_owner_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
            {{TwoPlainNodesJson}}
              "forks": [
                { "ownerIndex": 0, "branches": [ 1 ] },
                { "ownerIndex": 0, "branches": [ 1 ] }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("duplicated"));
    }

    [Test]
    public void Cross_section_claim_overlap_throws()
    {
        // The same node index claimed by Forks and Joins: with markerless container claims in
        // the format, section disjointness is checked explicitly.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
            {{TwoPlainNodesJson}}
              "forks": [ { "ownerIndex": 0, "branches": [ 1 ] } ],
              "joins": [ { "ownerIndex": 0, "kind": 1, "count": 1 } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("claimed by both"));
    }

    [Test]
    public void Out_of_range_fork_owner_throws()
    {
        // Both nodes are ordinary payload — the out-of-range claim is the only defect, so the
        // rebuild pass's range check is what fires (not a codec decode failure).
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "a" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "forks": [ { "ownerIndex": 9, "branches": [ 1 ] } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("out of range"));
    }

    [Test]
    public void Out_of_range_branch_index_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
            {{TwoPlainNodesJson}}
              "forks": [ { "ownerIndex": 0, "branches": [ 7 ] } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("branch index 7 out of range"));
    }

    [Test]
    public void Empty_branch_array_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
            {{TwoPlainNodesJson}}
              "forks": [ { "ownerIndex": 0, "branches": [] } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("at least one branch"));
    }

    [TestCase(3, 1, Description = "unknown kind")]
    [TestCase(0, 0, Description = "All with count 0 (default-struct policy)")]
    [TestCase(1, 2, Description = "Any must have count 1")]
    public void Invalid_join_policy_throws(int kind, int count)
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "JoinState" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "joins": [ { "ownerIndex": 0, "kind": {{kind}}, "count": {{count}} } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("invalid policy"));
    }

    [Test]
    public void New_marker_strings_collide_with_no_existing_marker()
    {
        string[] markers =
        [
            NodeId.Default.Name, // legacy "Default" alias
            NodeId.StateMachineMarker.Name,
            NodeId.SyncStateMachineMarker.Name,
            NodeId.HistoryStateMarker.Name,
            NodeId.SyncHistoryStateMarker.Name,
            NodeId.ParallelStateMarker.Name,
            NodeId.SyncParallelStateMarker.Name,
            NodeId.ForkStateMarker.Name,
            NodeId.JoinStateMarker.Name,
            NodeId.DynamicParallelStateMarker.Name,
            NodeId.SyncDynamicParallelStateMarker.Name,
            NodeId.EventEntryStateMarker.Name,
        ];

        Assert.That(markers, Is.Unique, "Every wire marker string must be distinct.");
    }
}
