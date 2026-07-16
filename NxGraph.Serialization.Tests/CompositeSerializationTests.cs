using System.Text;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 4: history and parallel composites on the wire. Round-trips per kind
/// (JSON + MessagePack), behavioral equivalence after the trip, region-order pinning,
/// nesting, marker-spoof defense, back-compat fixtures, and the agent/blackboard walks
/// on deserialized composites.
/// </summary>
[TestFixture]
[Category("serialization")]
public class CompositeSerializationTests
{
    // ── Test doubles ─────────────────────────────────────────────────────

    private interface IKeyed
    {
        string Key { get; }
    }

    /// <summary>
    /// Node logic whose wire form is just a registry key: the codec maps the key back to the
    /// registered instance, so round-tripped graphs share the authored delegates/flags and
    /// behavioral tests can steer the deserialized graph (fail-once nodes, logs, ...).
    /// </summary>
    private sealed class KeyedState(string key, Func<Result> body) : IAsyncLogic, ILogic, IKeyed
    {
        public string Key => key;
        public Result Execute() => body();
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => new(body());
    }

    private sealed class TestAgent;

    private sealed class KeyedAgentState(string key) : IAsyncLogic, ILogic, IKeyed, IAgentSettable<TestAgent>
    {
        public string Key => key;
        public TestAgent? Received;
        public void SetAgent(TestAgent agent) => Received = agent;
        public Result Execute() => Result.Success;
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => ResultHelpers.Success;
    }

    private sealed class KeyedBlackboardWriterState(string key, BlackboardKey<int> bbKey, int value)
        : IAsyncLogic, ILogic, IKeyed, IBlackboardSettable
    {
        private BlackboardContext _bb;
        public string Key => key;
        void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _bb = context;

        public Result Execute()
        {
            _bb.Set(bbKey, value);
            return Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => new(Execute());
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

    private static Result RunSyncToCompletion(StateMachine machine, out int inProgressTicks)
    {
        inProgressTicks = 0;
        Result result = Result.InProgress;
        for (int guard = 0; guard < 1_000 && result == Result.InProgress; guard++)
        {
            result = machine.Execute();
            if (result == Result.InProgress)
            {
                inProgressTicks++;
            }
        }

        return result;
    }

    private static async Task<Graph> FromJson(GraphSerializer serializer, string json)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        return await serializer.FromJsonAsync(source);
    }

    // ── Round-trips per kind, with behavioral equivalence ────────────────

    [Test]
    public async Task Async_history_composite_roundtrips_and_resumes_at_last_active([Values] bool binary)
    {
        List<string> log = [];
        bool repaired = false;

        RegistryCodec codec = new();
        codec.Log("c0", log);
        codec.Register("c1", new KeyedState("c1", () =>
        {
            log.Add("c1");
            return repaired ? Result.Success : Result.Failure;
        }));
        codec.Log("c2", log);
        codec.Register("repair", new KeyedState("repair", () =>
        {
            repaired = true;
            log.Add("repair");
            return Result.Success;
        }));

        Graph child = GraphBuilder
            .StartWithAsync(codec.Deserialize("c0"))
            .ToAsync(codec.Deserialize("c1"))
            .ToAsync(codec.Deserialize("c2"))
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(child, history: true)
            .SetName("Sub");
        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(codec.Deserialize("repair")));
        repair.Goto("Sub");
        Graph parent = sub.OnError(repair).Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.StartNode;
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(composite.AsyncLogic, Is.InstanceOf<AsyncHistoryState>(),
                "The history marker must rebuild an AsyncHistoryState, not a plain nested machine.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "After the repair, the deserialized child resumed at the failed node c1 — c0 did not re-run.");
        });
    }

    [Test]
    public async Task Sync_history_composite_roundtrips_with_mode_and_resumes_at_last_active([Values] bool binary)
    {
        List<string> log = [];
        bool repaired = false;

        RegistryCodec codec = new();
        codec.Log("c0", log);
        codec.Register("c1", new KeyedState("c1", () =>
        {
            log.Add("c1");
            return repaired ? Result.Success : Result.Failure;
        }));
        codec.Log("c2", log);
        codec.Register("repair", new KeyedState("repair", () =>
        {
            repaired = true;
            log.Add("repair");
            return Result.Success;
        }));

        Graph child = GraphBuilder
            .StartWith((ILogic)codec.Deserialize("c0"))
            .To((ILogic)codec.Deserialize("c1"))
            .To((ILogic)codec.Deserialize("c2"))
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, child, history: true)
            .SetName("Sub");
        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode((ILogic)codec.Deserialize("repair")));
        repair.Goto("Sub");
        Graph parent = sub.OnError(repair).Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(composite.Logic, Is.InstanceOf<HistoryState>(),
            "The sync history marker must rebuild a sync HistoryState so the node stays sync-runnable.");
        Assert.That(((HistoryState)composite.Logic!).Mode, Is.EqualTo(ParallelStepMode.RunToJoin),
            "The step mode is structure and must survive the trip.");

        Result result = RunSyncToCompletion(new StateMachine(rebuilt), out _);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "After the repair, the deserialized child resumed at the failed node c1 — c0 did not re-run.");
        });
    }

    [Test]
    public async Task Sync_history_RoundPerTick_mode_survives_the_trip()
    {
        RegistryCodec codec = new();
        codec.Register("c0", new KeyedState("c0", () => Result.Success));

        Graph child = GraphBuilder.StartWith((ILogic)codec.Deserialize("c0")).Build();
        Graph parent = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RoundPerTick, child, history: true)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(((HistoryState)composite.Logic!).Mode, Is.EqualTo(ParallelStepMode.RoundPerTick));
    }

    [Test]
    public async Task Async_parallel_composite_roundtrips_joins_and_preserves_region_order([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();

        Graph r0 = GraphBuilder.StartWithAsync(codec.Log("r0", log)).Build();
        Graph r1 = GraphBuilder.StartWithAsync(codec.Log("r1", log)).Build();
        Graph r2 = GraphBuilder.StartWithAsync(codec.Log("r2", log)).Build();

        Graph parent = GraphBuilder
            .StartWithAsync(codec.Log("p0", log))
            .Parallel(r0, r1, r2)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.GetNodeByIndex(1);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(composite.AsyncLogic, Is.InstanceOf<AsyncParallelState>());
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "p0", "r0", "r1", "r2" }),
                "Region order is identity (RegionMask bits) and must be preserved on the wire.");
        });
    }

    [Test]
    public async Task Sync_parallel_RunToJoin_roundtrips_and_joins([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();

        Graph rA = GraphBuilder.StartWith((ILogic)codec.Log("a0", log)).Build();
        Graph rB = GraphBuilder.StartWith((ILogic)codec.Log("b0", log)).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, rA, rB)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(composite.Logic, Is.InstanceOf<ParallelState>(),
            "The sync parallel marker must rebuild a sync ParallelState so the node stays sync-runnable.");
        Assert.That(((ParallelState)composite.Logic!).Mode, Is.EqualTo(ParallelStepMode.RunToJoin));

        Result result = RunSyncToCompletion(new StateMachine(rebuilt), out _);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0" }), "Both regions joined in region order.");
        });
    }

    [Test]
    public async Task Sync_parallel_RoundPerTick_still_returns_InProgress_per_round_after_roundtrip()
    {
        List<string> log = [];
        RegistryCodec codec = new();

        Graph rA = GraphBuilder
            .StartWith((ILogic)codec.Log("a0", log))
            .To((ILogic)codec.Log("a1", log))
            .Build();
        Graph rB = GraphBuilder
            .StartWith((ILogic)codec.Log("b0", log))
            .To((ILogic)codec.Log("b1", log))
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, rA, rB)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(((ParallelState)composite.Logic!).Mode, Is.EqualTo(ParallelStepMode.RoundPerTick),
            "RoundPerTick is structure and must survive the trip.");

        Result result = RunSyncToCompletion(new StateMachine(rebuilt), out int inProgressTicks);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(inProgressTicks, Is.GreaterThanOrEqualTo(1),
                "One round per tick: the deserialized composite must still spread rounds across Execute() calls.");
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1" }),
                "Rounds interleave the regions one node per round, in region order.");
        });
    }

    // ── Nesting ──────────────────────────────────────────────────────────

    [Test]
    public async Task Composite_inside_a_nested_machine_roundtrips([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();

        Graph region = GraphBuilder.StartWithAsync(codec.Log("r0", log)).Build();
        Graph mid = GraphBuilder
            .StartWithAsync(codec.Log("m0", log))
            .Parallel(region)
            .Build();
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Log("p0", log))
            .SubGraph(mid)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "p0", "m0", "r0" }));
        });
    }

    [Test]
    public async Task Nested_machine_inside_a_composite_roundtrips([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();

        Graph grandchild = GraphBuilder.StartWithAsync(codec.Log("g0", log)).Build();
        Graph region = GraphBuilder
            .StartWithAsync(codec.Log("r0", log))
            .SubGraph(grandchild)
            .Build();
        Graph parent = GraphBuilder
            .Start()
            .Parallel(region)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "r0", "g0" }));
        });
    }

    [Test]
    public void Composite_nesting_deeper_than_the_cap_is_rejected_on_serialize()
    {
        RegistryCodec codec = new();
        Graph current = GraphBuilder
            .StartWithAsync(codec.Register("leaf", new KeyedState("leaf", () => Result.Success)))
            .Build();
        // MaxSubGraphDepth is 64; exceed it comfortably.
        for (int i = 0; i < 70; i++)
        {
            current = GraphBuilder.Start().SubGraph(current, history: true).Build();
        }

        GraphSerializer serializer = new(codec);
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.ToJsonAsync(current, new MemoryStream()));
    }

    // ── Marker-spoof defense ─────────────────────────────────────────────

    [Test]
    public async Task Composite_marker_string_in_ordinary_logic_is_not_honored()
    {
        // A codec legitimately emitting "ParallelState" for ordinary logic must round-trip as
        // ordinary logic: markers are honored only when a CompositeDto claims the node index.
        RegistryCodec codec = new();
        codec.Register("ParallelState", new KeyedState("ParallelState", () => Result.Success));

        Graph graph = GraphBuilder
            .StartWithAsync(codec.Deserialize("ParallelState"))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: false);
        LogicNode node = (LogicNode)rebuilt.StartNode;

        Assert.Multiple(() =>
        {
            Assert.That(node.AsyncLogic, Is.Not.InstanceOf<AsyncParallelState>());
            Assert.That(((IKeyed)node.AsyncLogic).Key, Is.EqualTo("ParallelState"));
        });
    }

    [Test]
    public void Composite_marker_with_no_claiming_composite_fails_loud()
    {
        // A node marked "HistoryState" with no CompositeDto claiming it is not a composite:
        // the string goes to the codec like any other payload, and a codec that cannot decode
        // it fails loud instead of producing a silent placeholder node.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "HistoryState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [],
              "name": null,
              "index": -1
            }
            """;

        GraphSerializer serializer = new(new RegistryCodec());
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("Unknown logic key"));
    }

    private const string MinimalChildJson = """
        { "version": 4,
          "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
          "transitions": [ { "destination": -1 } ],
          "subGraphs": [], "composites": [], "name": null, "index": -1 }
        """;

    [Test]
    public void Composite_claim_on_a_non_marker_node_fails_loud()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 2, "mode": 0, "children": [ {{MinimalChildJson}} ] } ],
              "name": null,
              "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("a", new KeyedState("a", () => Result.Success));
        codec.Register("c0", new KeyedState("c0", () => Result.Success));

        GraphSerializer serializer = new(codec);
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("does not reference"));
    }

    [Test]
    public void Composite_kind_and_marker_mismatch_fails_loud()
    {
        // Node marked "HistoryState" but the claiming CompositeDto says AsyncParallel (kind 2):
        // a corrupt or crafted payload, not something to guess at.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "HistoryState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 2, "mode": 0, "children": [ {{MinimalChildJson}} ] } ],
              "name": null,
              "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("c0", new KeyedState("c0", () => Result.Success));

        GraphSerializer serializer = new(codec);
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("kind"));
    }

    [Test]
    public void Unknown_composite_kind_fails_loud()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "HistoryState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 9, "mode": 0, "children": [ {{MinimalChildJson}} ] } ],
              "name": null,
              "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("c0", new KeyedState("c0", () => Result.Success));

        GraphSerializer serializer = new(codec);
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("Unknown composite kind"));
    }

    // ── Versioning and back-compat ───────────────────────────────────────

    [Test]
    public async Task Composite_payload_carries_the_current_version_and_the_kind_marker()
    {
        RegistryCodec codec = new();
        Graph region = GraphBuilder
            .StartWithAsync(codec.Register("r0", new KeyedState("r0", () => Result.Success)))
            .Build();
        Graph parent = GraphBuilder.Start().Parallel(region).Build();

        await using MemoryStream stream = new();
        await new GraphSerializer(codec).ToJsonAsync(parent, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
            Assert.That(json, Does.Contain("ParallelState"));
        });
    }

    [Test]
    public void A_payload_newer_than_the_reader_is_rejected_by_the_version_gate()
    {
        // This is exactly what a v3-pinned reader does with a v4 payload: its gate compares
        // the wire version against its own SerializationVersion and rejects anything newer.
        // Here the same mechanism is exercised one version ahead of the current reader.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version + 1}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [],
              "name": null,
              "index": -1
            }
            """;

        GraphSerializer serializer = new(new RegistryCodec());
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("newer"));
    }

    [Test]
    public async Task V3_payload_with_a_sync_nested_machine_still_reads()
    {
        string json = """
            {
              "version": 3,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "p0", "logic": "p0" },
                { "$type": "txt", "index": 1, "name": "child", "logic": "SyncStateMachine" }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [
                { "ownerIndex": 1,
                  "graph": { "version": 3,
                             "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                             "transitions": [ { "destination": -1 } ],
                             "subGraphs": [], "name": null, "index": -1 } }
              ],
              "name": null,
              "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("p0", new KeyedState("p0", () => Result.Success));
        codec.Register("c0", new KeyedState("c0", () => Result.Success));

        Graph rebuilt = await FromJson(new GraphSerializer(codec), json);
        LogicNode owner = (LogicNode)rebuilt.GetNodeByIndex(1);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.NodeCount, Is.EqualTo(2));
            Assert.That(owner.Logic, Is.InstanceOf<StateMachine>());
        });
    }

    [Test]
    public async Task V2_payload_with_retry_policies_still_reads()
    {
        string json = """
            {
              "version": 2,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "retryPolicies": [ { "index": 0, "maxAttempts": 2, "backoffTicks": 0, "backoffKind": 0 } ],
              "name": null,
              "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("a", new KeyedState("a", () => Result.Success));

        Graph rebuilt = await FromJson(new GraphSerializer(codec), json);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.NodeCount, Is.EqualTo(1));
            Assert.That(rebuilt.RetryPolicies, Is.Not.Null);
            Assert.That(rebuilt.RetryPolicies![0].MaxAttempts, Is.EqualTo(2));
        });
    }

    // ── What still throws (unconfigured options) ─────────────────────────

    [Test]
    public void Async_dynamic_parallel_without_a_selector_registry_throws_the_targeted_error()
    {
        RegistryCodec codec = new();
        Graph region = GraphBuilder
            .StartWithAsync(codec.Register("r0", new KeyedState("r0", () => Result.Success)))
            .Build();
        Graph graph = GraphBuilder
            .Start()
            .Parallel(_ => RegionMask.Bit(0), region)
            .Build();

        GraphSerializer serializer = new(codec);
        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(
            async () => await serializer.ToJsonAsync(graph, new MemoryStream()));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("dynamic parallel composite"));
            Assert.That(ex.Message, Does.Contain("GraphSerializerOptions.SelectorRegistry"),
                "The targeted error names the option that unlocks the feature.");
        });
    }

    [Test]
    public void Custom_subgraph_provider_containers_without_a_container_codec_throw_the_targeted_error()
    {
        RegistryCodec codec = new();
        Graph child = GraphBuilder
            .StartWithAsync(codec.Register("c0", new KeyedState("c0", () => Result.Success)))
            .Build();
        Graph graph = GraphBuilder
            .StartWithAsync(new AsyncCompositeState(new AsyncStateMachine(child)))
            .Build();

        GraphSerializer serializer = new(codec);
        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(
            async () => await serializer.ToJsonAsync(graph, new MemoryStream()));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("AsyncCompositeState"));
            Assert.That(ex.Message, Does.Contain("GraphSerializerOptions.ContainerCodec"),
                "The targeted error names the option that unlocks the feature.");
        });
    }

    // ── Agent and blackboard walks on deserialized composites ───────────

    [Test]
    public async Task Deserialized_composite_participates_in_agent_stamping()
    {
        RegistryCodec codec = new();
        KeyedAgentState agentState = codec.Register("agent", new KeyedAgentState("agent"));

        Graph child = GraphBuilder.StartWithAsync(agentState).Build();
        Graph parent = GraphBuilder.Start().SubGraph(child, history: true).Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);

        TestAgent agent = new();
        rebuilt.SetAgent(agent);

        Assert.That(agentState.Received, Is.SameAs(agent),
            "The ISubGraphProvider walk must reach nodes inside the deserialized composite's child graph.");
    }

    [Test]
    public async Task Deserialized_composite_forwards_blackboards_to_its_regions()
    {
        BlackboardSchema schema = new("test");
        BlackboardKey<int> key = schema.Register<int>("value");

        RegistryCodec codec = new();
        Graph region = GraphBuilder
            .StartWithAsync(codec.Register("writer", new KeyedBlackboardWriterState("writer", key, 42)))
            .Build();
        Graph parent = GraphBuilder.Start().Parallel(region).Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);

        Blackboard board = new(schema);
        Result result = await rebuilt.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(key), Is.EqualTo(42),
                "The IBlackboardSettable walk must forward the stamped context into the deserialized composite's regions.");
        });
    }
}
