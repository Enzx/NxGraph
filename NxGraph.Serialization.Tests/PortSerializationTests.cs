using System.Text;
using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 12: sub-graph ports on the wire. The nested-machine SubGraphs entries gain
/// OwnsBoard plus a ports list, and CompositeDto gains sparse per-region ports (entry
/// presence = owns-board), each port riding as direction, source key name or field-model
/// literal, target name, and runtime-stable value type name. Everything rebuilds name-bound:
/// a deserialized child graph carries no schema, so the host supplies the board to own by
/// binding a Graph-scoped board on the child machine (the adoption seam), and port keys
/// resolve per application with targeted errors. Pre-v12 payloads read shared-board.
/// </summary>
[TestFixture]
[Category("serialization")]
public class PortSerializationTests
{
    // ── Test doubles (the OutcomeKeySerializationTests recipe) ───────────

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

        public KeyedState Ok(string key) => Register(key, new KeyedState(key, () => Result.Success));

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

    private static async Task<Graph> FromJson(GraphSerializer serializer, string json)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        return await serializer.FromJsonAsync(source);
    }

    private static (BlackboardSchema Schema, Blackboard Board, BlackboardKey<int> Seed,
        BlackboardKey<int> Verdict) ParentBoards()
    {
        BlackboardSchema schema = new("port-wire-parent", BlackboardScope.Graph);
        BlackboardKey<int> seed = schema.Register("seed", 0);
        BlackboardKey<int> verdict = schema.Register("verdict", -1);
        return (schema, new Blackboard(schema), seed, verdict);
    }

    /// <summary>
    /// A fully serializable child: one behavior node copying <c>childIn</c> to
    /// <c>childOut</c> (the standard set rides with zero options), keys on the child's own
    /// Graph schema. The unused <c>childBonus</c> key exists so a literal input has a target.
    /// </summary>
    private static (Graph Graph, BlackboardKey<int> In, BlackboardKey<int> Bonus, BlackboardKey<int> Out)
        Child()
    {
        BlackboardSchema schema = new("port-wire-child", BlackboardScope.Graph);
        BlackboardKey<int> input = schema.Register("childIn", 0);
        BlackboardKey<int> bonus = schema.Register("childBonus", 0);
        BlackboardKey<int> output = schema.Register("childOut", 0);
        // The sync behavior composite runs under both runtimes (via the sync-logic adapter),
        // so one child recipe serves the async and the sync nested-machine round-trips.
        Graph graph = GraphBuilder
            .Start()
            .ToBehaviors(new SetValue<int>(output, input)).SetName("c-copy")
            .WithSchema(schema)
            .Build();
        return (graph, input, bonus, output);
    }

    private static SubGraphPorts Ports(BlackboardKey<int> seed, BlackboardKey<int> verdict,
        (Graph Graph, BlackboardKey<int> In, BlackboardKey<int> Bonus, BlackboardKey<int> Out) child) =>
        SubGraphPorts.OwnBoard()
            .In(seed, child.In)
            .In(5, child.Bonus)
            .Out(child.Out, verdict);

    /// <summary>
    /// The adoption seam: a deserialized child graph carries no schema declarations, so the
    /// host supplies the board to own by binding a Graph-scoped board on the child machine —
    /// port names then resolve against this board's schema per application.
    /// </summary>
    private static Blackboard AdoptionBoard()
    {
        BlackboardSchema schema = new("port-wire-adopted", BlackboardScope.Graph);
        schema.Register("childIn", 0);
        schema.Register("childBonus", 0);
        schema.Register("childOut", 0);
        return new Blackboard(schema);
    }

    private static void AssertPortsSurvived(bool ownsBoard, IReadOnlyList<ISubGraphPort> ports)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ownsBoard, Is.True, "The board-ownership declaration survives the trip.");
            Assert.That(ports, Has.Count.EqualTo(3));
            Assert.That(ports[0].IsInput, Is.True);
            Assert.That(ports[0].SourceKeyName, Is.EqualTo("seed"));
            Assert.That(ports[0].TargetKeyName, Is.EqualTo("childIn"));
            Assert.That(ports[0].ValueType, Is.EqualTo(typeof(int)));
            Assert.That(ports[1].IsInput, Is.True);
            Assert.That(ports[1].SourceKeyName, Is.Null, "The literal input has no source key.");
            Assert.That(ports[1].SourceLiteral, Is.EqualTo(5));
            Assert.That(ports[1].TargetKeyName, Is.EqualTo("childBonus"));
            Assert.That(ports[2].IsInput, Is.False);
            Assert.That(ports[2].SourceKeyName, Is.EqualTo("childOut"));
            Assert.That(ports[2].TargetKeyName, Is.EqualTo("verdict"));
        });
    }

    // ── Round-trips: nested machines (both markers) ──────────────────────

    [Test]
    public async Task Async_nested_machine_with_ports_roundtrips_and_runs_isolated([Values] bool binary)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict) =
            ParentBoards();
        _ = schema;
        RegistryCodec codec = new();
        codec.Ok("p0");

        (Graph, BlackboardKey<int>, BlackboardKey<int>, BlackboardKey<int>) child = Child();
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Deserialize("p0"))
            .SubGraph(child.Item1, history: false, null, null, Ports(seed, verdict, child)).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        AsyncStateMachine nested = (AsyncStateMachine)((LogicNode)rebuilt.GetNodeByIndex(1)).AsyncLogic;
        AssertPortsSurvived(nested.OwnsBoard, nested.Ports);

        // Adoption: the rebuilt child is schema-less; the host binds the board to own.
        nested.SetBlackboard(AdoptionBoard());

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        board.Set(seed, 42);
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(42),
                "The rebuilt ports resolved by name: seed in, childIn→childOut via the behavior, " +
                "childOut back out to verdict.");
        });
    }

    [Test]
    public async Task Sync_nested_machine_with_ports_roundtrips_and_runs_isolated([Values] bool binary)
    {
        (_, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict) = ParentBoards();
        RegistryCodec codec = new();
        codec.Ok("p0");

        (Graph, BlackboardKey<int>, BlackboardKey<int>, BlackboardKey<int>) child = Child();
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Deserialize("p0"))
            .SubGraph(ParallelStepMode.RunToJoin, child.Item1, history: false, null, null,
                Ports(seed, verdict, child)).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        StateMachine nested = (StateMachine)((LogicNode)rebuilt.GetNodeByIndex(1)).Logic!;
        AssertPortsSurvived(nested.OwnsBoard, nested.Ports);

        nested.SetBlackboard(AdoptionBoard());

        // Machine-level step mode does not ride: the rebuilt nested machine carries the
        // RoundPerTick default, so drive the parent as a sync RunToJoin machine (the parent
        // loop absorbs the child's InProgress ticks).
        StateMachine machine = rebuilt.ToStateMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        machine.SetBlackboard(board);
        board.Set(seed, 42);
        Result result = machine.Execute();
        await Task.CompletedTask;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(42));
        });
    }

    // ── Round-trips: history and parallel composites ─────────────────────

    [Test]
    public async Task History_composite_with_ports_roundtrips_and_runs_isolated([Values] bool binary)
    {
        (_, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict) = ParentBoards();
        RegistryCodec codec = new();

        (Graph, BlackboardKey<int>, BlackboardKey<int>, BlackboardKey<int>) child = Child();
        Graph parent = GraphBuilder
            .Start()
            .SubGraph(child.Item1, history: true, null, null, Ports(seed, verdict, child)).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        AsyncHistoryState composite = (AsyncHistoryState)((LogicNode)rebuilt.StartNode).AsyncLogic;
        AssertPortsSurvived(composite.Child.OwnsBoard, composite.Child.Ports);

        composite.Child.SetBlackboard(AdoptionBoard());

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        board.Set(seed, 42);
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(42));
        });
    }

    [Test]
    public async Task Parallel_with_per_region_ports_roundtrips_and_runs_isolated([Values] bool binary)
    {
        (_, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict) = ParentBoards();
        RegistryCodec codec = new();
        codec.Ok("r1");

        // Region 0 owns a board with ports; region 1 keeps the shared-board default.
        (Graph, BlackboardKey<int>, BlackboardKey<int>, BlackboardKey<int>) region0 = Child();
        Graph region1 = GraphBuilder.StartWithAsync(codec.Deserialize("r1")).Build();
        Graph parent = GraphBuilder
            .Start()
            .Parallel([
                new ParallelRegion(region0.Item1, Ports(seed, verdict, region0)),
                region1,
            ]).SetName("par")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        AsyncParallelState composite = (AsyncParallelState)((LogicNode)rebuilt.StartNode).AsyncLogic;
        AssertPortsSurvived(composite.Regions[0].OwnsBoard, composite.Regions[0].Ports);
        Assert.Multiple(() =>
        {
            Assert.That(composite.Regions[1].OwnsBoard, Is.False,
                "The undeclared region keeps the shared-board default.");
            Assert.That(composite.Regions[1].Ports, Is.Empty);
        });

        composite.Regions[0].SetBlackboard(AdoptionBoard());

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        board.Set(seed, 42);
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(42));
        });
    }

    // ── Targeted errors on deserialized graphs ───────────────────────────

    [Test]
    public async Task Running_a_rebuilt_ports_graph_without_adopting_a_board_fails_with_a_targeted_error()
    {
        (_, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict) = ParentBoards();
        RegistryCodec codec = new();
        codec.Ok("p0");

        (Graph, BlackboardKey<int>, BlackboardKey<int>, BlackboardKey<int>) child = Child();
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Deserialize("p0"))
            .SubGraph(child.Item1, history: false, null, null, Ports(seed, verdict, child))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("owns the child's Graph board").And.Contain("SetBlackboard"),
            "A deserialized child carries no schema — the error names the adoption seam.");
    }

    // ── Version stamps and back compatibility ────────────────────────────

    [Test]
    public async Task Payload_carries_current_version_stamp_and_the_ports_fields()
    {
        (_, _, BlackboardKey<int> seed, BlackboardKey<int> verdict) = ParentBoards();
        RegistryCodec codec = new();
        codec.Ok("p0");

        (Graph, BlackboardKey<int>, BlackboardKey<int>, BlackboardKey<int>) child = Child();
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Deserialize("p0"))
            .SubGraph(child.Item1, history: false, null, null, Ports(seed, verdict, child))
            .Build();

        await using MemoryStream stream = new();
        await new GraphSerializer(codec).ToJsonAsync(parent, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
            Assert.That(json, Does.Contain("\"ownsBoard\": true"));
            Assert.That(json, Does.Contain("\"targetName\": \"childIn\""));
            Assert.That(json, Does.Contain("\"sourceKeyName\": \"seed\""));
        });
    }

    [Test]
    public async Task Version_eleven_payload_reads_shared_board_and_port_free()
    {
        RegistryCodec codec = new();
        codec.Ok("c0");

        // A pre-v12 payload: nested machine and history composite entries in the v11 shapes.
        string json = """
            {
              "version": 11,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "nested", "logic": "StateMachine" },
                { "$type": "txt", "index": 1, "name": "kept", "logic": "HistoryState" }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [
                {
                  "ownerIndex": 0,
                  "outcomeCodeKeyName": "verdict",
                  "graph": {
                    "version": 11,
                    "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                    "transitions": [ { "destination": -1 } ],
                    "subGraphs": [], "composites": [], "name": null, "index": -1
                  }
                }
              ],
              "composites": [
                {
                  "ownerIndex": 1,
                  "kind": 0,
                  "mode": 0,
                  "children": [
                    {
                      "version": 11,
                      "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                      "transitions": [ { "destination": -1 } ],
                      "subGraphs": [], "composites": [], "name": null, "index": -1
                    }
                  ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(new GraphSerializer(codec), json);
        AsyncStateMachine nested = (AsyncStateMachine)((LogicNode)rebuilt.StartNode).AsyncLogic;
        AsyncHistoryState history = (AsyncHistoryState)((LogicNode)rebuilt.GetNodeByIndex(1)).AsyncLogic;

        Assert.Multiple(() =>
        {
            Assert.That(nested.OwnsBoard, Is.False, "A pre-v12 payload rebuilds shared-board.");
            Assert.That(nested.Ports, Is.Empty);
            Assert.That(nested.OutcomeCodeKeyName, Is.EqualTo("verdict"),
                "The v11 fields still read beside the absent v12 ones.");
            Assert.That(history.Child.OwnsBoard, Is.False);
            Assert.That(history.Child.Ports, Is.Empty);
        });
    }

    [Test]
    public void Ports_without_board_ownership_are_rejected()
    {
        RegistryCodec codec = new();
        codec.Ok("c0");

        string json = """
            {
              "version": 12,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "nested", "logic": "StateMachine" }
              ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [
                {
                  "ownerIndex": 0,
                  "ownsBoard": false,
                  "ports": [
                    { "direction": 1, "sourceKeyName": "childOut", "sourceLiteral": null,
                      "targetName": "verdict", "valueTypeName": "System.Int32" }
                  ],
                  "graph": {
                    "version": 12,
                    "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                    "transitions": [ { "destination": -1 } ],
                    "subGraphs": [], "composites": [], "name": null, "index": -1
                  }
                }
              ],
              "composites": [],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(codec), json));
        Assert.That(ex!.Message, Does.Contain("does not declare board ownership"));
    }

    [Test]
    public void Region_ports_with_an_out_of_range_region_index_are_rejected()
    {
        RegistryCodec codec = new();
        codec.Ok("c0");

        string json = """
            {
              "version": 12,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "par", "logic": "ParallelState" }
              ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [],
              "composites": [
                {
                  "ownerIndex": 0,
                  "kind": 2,
                  "mode": 0,
                  "children": [
                    {
                      "version": 12,
                      "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                      "transitions": [ { "destination": -1 } ],
                      "subGraphs": [], "composites": [], "name": null, "index": -1
                    }
                  ],
                  "regionPorts": [ { "regionIndex": 1, "ports": [] } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(codec), json));
        Assert.That(ex!.Message, Does.Contain("region 1").And.Contain("out of range"));
    }
}
