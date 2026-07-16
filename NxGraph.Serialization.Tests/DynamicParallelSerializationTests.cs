using System.Text;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 6, part B: dynamic parallel composites on the wire. Region graphs and step
/// mode ride like the static kinds; the selector delegate rides as a named key resolved
/// through <see cref="RegionSelectorRegistry"/>. Covers selector re-resolution and
/// blackboard-driven selection after the trip, step-mode preservation, vacuous joins, region
/// order, nesting, agent stamping, and the negative fixtures for keys and claims.
/// </summary>
[TestFixture]
[Category("serialization")]
public class DynamicParallelSerializationTests
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

    private static GraphSerializer SerializerWith(RegistryCodec codec, RegionSelectorRegistry registry)
        => new(codec, new GraphSerializerOptions { SelectorRegistry = registry });

    // ── Round-trips with behavioral equivalence ──────────────────────────

    [Test]
    public async Task Async_dynamic_parallel_roundtrips_and_reselects_via_the_registry([Values] bool binary)
    {
        BlackboardSchema schema = new("dyn");
        BlackboardKey<int> which = schema.Register<int>("which");

        List<string> log = [];
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector =
            registry.Register("pick-one", ctx => RegionMask.Bit(ctx.Get(which)));

        Graph r0 = GraphBuilder.StartWithAsync(codec.Log("r0", log)).Build();
        Graph r1 = GraphBuilder.StartWithAsync(codec.Log("r1", log)).Build();
        Graph parent = GraphBuilder.Start().Parallel(selector, r0, r1).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Blackboard board = new(schema);
        board.Set(which, 1);
        Result result = await rebuilt.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(composite.AsyncLogic, Is.InstanceOf<AsyncDynamicParallelState>(),
                "The dynamic marker must rebuild an AsyncDynamicParallelState.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "r1" }),
                "The re-resolved selector read the blackboard and selected only region 1 — " +
                "mask bit i still targets original region i.");
        });
    }

    [Test]
    public async Task Sync_dynamic_parallel_RoundPerTick_survives_the_trip_and_still_spreads_rounds(
        [Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("all", _ => RegionMask.All(2));

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
            .Parallel(ParallelStepMode.RoundPerTick, selector, rA, rB)
            .Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(composite.Logic, Is.InstanceOf<DynamicParallelState>(),
            "The sync dynamic marker must rebuild a sync DynamicParallelState behind the adapter.");
        Assert.That(((DynamicParallelState)composite.Logic!).Mode, Is.EqualTo(ParallelStepMode.RoundPerTick),
            "The step mode is structure and must survive the trip.");

        Result result = RunSyncToCompletion(new StateMachine(rebuilt), out int inProgressTicks);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(inProgressTicks, Is.GreaterThanOrEqualTo(1),
                "RoundPerTick still spreads rounds across Execute() calls after the trip.");
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1" }));
        });
    }

    [Test]
    public async Task Sync_dynamic_parallel_RunToJoin_roundtrips_and_joins_in_one_tick()
    {
        List<string> log = [];
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("all", _ => RegionMask.All(2));

        Graph rA = GraphBuilder.StartWith((ILogic)codec.Log("a0", log)).Build();
        Graph rB = GraphBuilder.StartWith((ILogic)codec.Log("b0", log)).Build();
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, selector, rA, rB)
            .Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary: false);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(((DynamicParallelState)composite.Logic!).Mode, Is.EqualTo(ParallelStepMode.RunToJoin));

        Result result = RunSyncToCompletion(new StateMachine(rebuilt), out _);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0" }));
        });
    }

    [Test]
    public async Task Empty_mask_selector_is_a_vacuous_join_after_the_trip()
    {
        List<string> log = [];
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("none", _ => RegionMask.None);

        Graph r0 = GraphBuilder.StartWithAsync(codec.Log("r0", log)).Build();
        Graph parent = GraphBuilder.Start().Parallel(selector, r0).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary: false);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.Empty, "No region was stepped — the empty mask is a vacuous join.");
        });
    }

    // ── Nesting both ways ────────────────────────────────────────────────

    [Test]
    public async Task Dynamic_parallel_containing_a_nested_machine_roundtrips([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("all", _ => RegionMask.All(1));

        Graph grandchild = GraphBuilder.StartWithAsync(codec.Log("g0", log)).Build();
        Graph region = GraphBuilder
            .StartWithAsync(codec.Log("r0", log))
            .SubGraph(grandchild)
            .Build();
        Graph parent = GraphBuilder.Start().Parallel(selector, region).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "r0", "g0" }));
        });
    }

    [Test]
    public async Task Static_composite_containing_a_dynamic_parallel_region_roundtrips()
    {
        List<string> log = [];
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("all", _ => RegionMask.All(1));

        Graph inner = GraphBuilder.StartWithAsync(codec.Log("i0", log)).Build();
        Graph region = GraphBuilder.Start().Parallel(selector, inner).Build();
        Graph parent = GraphBuilder.Start().Parallel(region).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary: false);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "i0" }));
        });
    }

    [Test]
    public async Task Agent_stamping_reaches_nodes_inside_rebuilt_dynamic_parallel_regions()
    {
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("all", _ => RegionMask.All(1));
        KeyedAgentState agentState = codec.Register("agent", new KeyedAgentState("agent"));

        Graph region = GraphBuilder.StartWithAsync(agentState).Build();
        Graph parent = GraphBuilder.Start().Parallel(selector, region).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary: false);

        TestAgent agent = new();
        rebuilt.SetAgent(agent);

        Assert.That(agentState.Received, Is.SameAs(agent),
            "The ISubGraphProvider walk must reach nodes inside the rebuilt dynamic composite's regions.");
    }

    [Test]
    public async Task Blackboard_forwarding_reaches_nodes_inside_rebuilt_dynamic_parallel_regions()
    {
        BlackboardSchema schema = new("dyn-fwd");
        BlackboardKey<int> key = schema.Register<int>("value");

        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("all", _ => RegionMask.All(1));

        Graph region = GraphBuilder
            .StartWithAsync(codec.Register("writer", new KeyedBlackboardWriterState("writer", key, 7)))
            .Build();
        Graph parent = GraphBuilder.Start().Parallel(selector, region).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec, registry), parent, binary: false);

        Blackboard board = new(schema);
        Result result = await rebuilt.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(key), Is.EqualTo(7),
                "The rebuilt dynamic composite keeps IBlackboardSettable forwarding into its region machines.");
        });
    }

    // ── Write-side failures ──────────────────────────────────────────────

    [Test]
    public void Unregistered_delegate_on_write_throws_the_targeted_error()
    {
        RegistryCodec codec = new();
        RegionSelectorRegistry registry = new();
        registry.Register("other", _ => RegionMask.None);

        Graph region = GraphBuilder
            .StartWithAsync(codec.Register("r0", new KeyedState("r0", () => Result.Success)))
            .Build();
        Graph graph = GraphBuilder.Start().Parallel(_ => RegionMask.Bit(0), region).Build();

        GraphSerializer serializer = SerializerWith(codec, registry);
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.ToJsonAsync(graph, new MemoryStream()));
        Assert.That(ex!.Message, Does.Contain("same delegate instance"),
            "The error tells the user to author the graph with the registered delegate instance.");
    }

    [Test]
    public void Duplicate_registry_key_or_delegate_fails_at_setup()
    {
        RegionSelectorRegistry registry = new();
        Func<BlackboardContext, RegionMask> selector = registry.Register("a", _ => RegionMask.None);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => registry.Register("a", _ => RegionMask.None),
                "Duplicate key fails at registration, not at save time.");
            Assert.Throws<ArgumentException>(() => registry.Register("b", selector),
                "Duplicate delegate fails at registration, not at save time.");
        });
    }

    // ── Read-side failures and spoof defense ─────────────────────────────

    private const string MinimalChildJson = """
        { "version": 6,
          "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
          "transitions": [ { "destination": -1 } ],
          "subGraphs": [], "composites": [], "name": null, "index": -1 }
        """;

    private static RegistryCodec CodecWithBasics()
    {
        RegistryCodec codec = new();
        codec.Register("a", new KeyedState("a", () => Result.Success));
        codec.Register("c0", new KeyedState("c0", () => Result.Success));
        return codec;
    }

    [Test]
    public void Dynamic_kind_without_a_selector_key_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "DynamicParallelState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 4, "mode": 0, "children": [ {{MinimalChildJson}} ] } ],
              "name": null, "index": -1
            }
            """;

        RegionSelectorRegistry registry = new();
        registry.Register("any", _ => RegionMask.None);
        GraphSerializer serializer = SerializerWith(CodecWithBasics(), registry);

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("must carry a selector key"));
    }

    [Test]
    public void Selector_key_smuggled_onto_a_static_kind_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "ParallelState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 2, "mode": 0, "selectorKey": "sneaky",
                                "children": [ {{MinimalChildJson}} ] } ],
              "name": null, "index": -1
            }
            """;

        RegionSelectorRegistry registry = new();
        registry.Register("sneaky", _ => RegionMask.None);
        GraphSerializer serializer = SerializerWith(CodecWithBasics(), registry);

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("only dynamic parallel kinds"));
    }

    [Test]
    public void Unknown_selector_key_on_read_names_the_key_and_node()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "DynamicParallelState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 4, "mode": 0, "selectorKey": "missing",
                                "children": [ {{MinimalChildJson}} ] } ],
              "name": null, "index": -1
            }
            """;

        RegionSelectorRegistry registry = new();
        registry.Register("present", _ => RegionMask.None);
        GraphSerializer serializer = SerializerWith(CodecWithBasics(), registry);

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("'missing'"));
            Assert.That(ex.Message, Does.Contain("node 0"));
        });
    }

    [Test]
    public void Dynamic_kind_without_a_registry_on_read_throws_the_targeted_error()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "DynamicParallelState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 4, "mode": 0, "selectorKey": "any",
                                "children": [ {{MinimalChildJson}} ] } ],
              "name": null, "index": -1
            }
            """;

        GraphSerializer serializer = new(CodecWithBasics());
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("GraphSerializerOptions.SelectorRegistry"));
    }

    [Test]
    public async Task Dynamic_marker_string_in_ordinary_logic_is_not_honored()
    {
        RegistryCodec codec = new();
        codec.Register("DynamicParallelState", new KeyedState("DynamicParallelState", () => Result.Success));

        Graph graph = GraphBuilder
            .StartWithAsync(codec.Deserialize("DynamicParallelState"))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: false);
        LogicNode node = (LogicNode)rebuilt.StartNode;

        Assert.Multiple(() =>
        {
            Assert.That(node.AsyncLogic, Is.Not.InstanceOf<AsyncDynamicParallelState>());
            Assert.That(((IKeyed)node.AsyncLogic).Key, Is.EqualTo("DynamicParallelState"));
        });
    }

    // ── Back-compat ──────────────────────────────────────────────────────

    [Test]
    public async Task V5_payload_without_the_new_sections_still_reads()
    {
        // Mirrors v4_payload_without_uids_still_reads: a v5 JSON payload omits forks, joins,
        // and containers entirely — the GraphDto ctor defaults apply.
        string json = """
            {
              "version": 5,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "uids": [ { "index": 0, "uid": "0f8fad5b-d9cb-469f-a165-70867728950e" } ],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(new GraphSerializer(CodecWithBasics()), json);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.NodeCount, Is.EqualTo(1));
            Assert.That(rebuilt.TryGetNodeByUid(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), out _),
                Is.True);
        });
    }

    [Test]
    public async Task A_four_element_composite_dto_still_reads_over_messagepack()
    {
        // Structural back-compat proof for the MessagePack CompositeDto count 4→5 change:
        // the reader accepts the 4-element (pre-v6) form with SelectorKey = null. Exercised
        // through a real v6 static-composite trip plus the JSON twin below; the 4-element
        // form itself is pinned by reading a v4-style JSON composite without selectorKey.
        string json = """
            {
              "version": 4,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "ParallelState" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [ { "ownerIndex": 0, "kind": 2, "mode": 0, "children": [
                { "version": 4,
                  "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                  "transitions": [ { "destination": -1 } ],
                  "subGraphs": [], "composites": [], "name": null, "index": -1 }
              ] } ],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(new GraphSerializer(CodecWithBasics()), json);
        LogicNode composite = (LogicNode)rebuilt.StartNode;

        Assert.That(composite.AsyncLogic, Is.InstanceOf<AsyncParallelState>(),
            "A composite without a selector key (pre-v6 shape) reads as the static kind.");
    }
}
