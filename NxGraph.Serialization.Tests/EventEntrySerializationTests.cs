using System.Text;
using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 7: event entry dispatchers on the wire. The dispatch table
/// (key names, runtime-stable event type names, targets, Otherwise target) is plain structure
/// and serializes unconditionally; keys never ride (schemas do not serialize), so the read
/// side rebuilds unbound registrations and raise resolves the delivery key by name against
/// the machine's bound board.
/// </summary>
[TestFixture]
[Category("serialization")]
public class EventEntrySerializationTests
{
    private sealed record OrderPlaced(string OrderId, decimal Amount);

    private readonly record struct OrderCanceled(string OrderId);

    // ── Test doubles (ForkJoinSerializationTests pattern) ────────────────

    private interface IKeyed
    {
        string Key { get; }
    }

    private sealed class KeyedBoardState(string key, Func<BlackboardContext, Result> body)
        : ILogic, IAsyncLogic, IBlackboardSettable, IKeyed
    {
        private BlackboardContext _bb;

        public string Key => key;

        void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _bb = context;

        public Result Execute() => body(_bb);

        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => new(body(_bb));
    }

    private sealed class RegistryCodec : ILogicTextCodec
    {
        private readonly Dictionary<string, IAsyncLogic> _byKey = new();

        public T Register<T>(string key, T logic) where T : IAsyncLogic
        {
            _byKey[key] = logic;
            return logic;
        }

        public KeyedBoardState Handler(string key, Func<BlackboardContext, Result> body)
            => Register(key, new KeyedBoardState(key, body));

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

    private static EventEntryState DispatcherOf(Graph graph)
    {
        LogicNode start = (LogicNode)graph.StartNode;
        return (start.AsyncLogic as EventEntryState ?? start.Logic as EventEntryState)!;
    }

    private sealed class ShopFixture
    {
        public required BlackboardSchema Schema;
        public required BlackboardKey<OrderPlaced> Placed;
        public required BlackboardKey<OrderCanceled> Canceled;
        public required RegistryCodec Codec;
        public required List<string> Log;
        public required Graph Graph;
    }

    /// <summary>
    /// Shop event graph with codec-serializable handlers. Node indexing: chains build first
    /// (1=reserve, 2=charge, 3=refund, 4=otherwise); the dispatcher seeds as start (0).
    /// </summary>
    private static ShopFixture Shop()
    {
        BlackboardSchema schema = new("shop");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");
        BlackboardKey<OrderCanceled> canceled = schema.Register<OrderCanceled>("orderCanceled");
        RegistryCodec codec = new();
        List<string> log = [];

        Graph graph = GraphBuilder.StartWithEvents()
            .On(placed, e => e
                .To(codec.Handler("reserve", bb =>
                {
                    log.Add($"reserve:{bb.Get(placed).OrderId}");
                    return Result.Success;
                }))
                .To(codec.Handler("charge", bb =>
                {
                    log.Add($"charge:{bb.Get(placed).Amount}");
                    return Result.Success;
                })))
            .On(canceled, e => e
                .To(codec.Handler("refund", bb =>
                {
                    log.Add($"refund:{bb.Get(canceled).OrderId}");
                    return Result.Success;
                })))
            .Otherwise(e => e
                .To(codec.Handler("otherwise", _ =>
                {
                    log.Add("otherwise");
                    return Result.Success;
                })))
            .WithSchema(schema)
            .Build();

        return new ShopFixture
        {
            Schema = schema, Placed = placed, Canceled = canceled, Codec = codec, Log = log, Graph = graph,
        };
    }

    // ── Round-trips ──────────────────────────────────────────────────────

    [Test]
    public async Task Event_entry_roundtrips_with_marker_section_and_default_target([Values] bool binary)
    {
        ShopFixture shop = Shop();
        EventEntryState original = DispatcherOf(shop.Graph);

        Graph rebuilt = await RoundTrip(new GraphSerializer(shop.Codec), shop.Graph, binary);
        EventEntryState dispatcher = DispatcherOf(rebuilt);

        Assert.Multiple(() =>
        {
            Assert.That(dispatcher, Is.Not.Null, "The start node rebuilt as an event dispatcher.");
            Assert.That(dispatcher.Registrations, Has.Count.EqualTo(2));
            Assert.That(dispatcher.Registrations[0].KeyName, Is.EqualTo("orderPlaced"));
            Assert.That(dispatcher.Registrations[1].KeyName, Is.EqualTo("orderCanceled"));
            Assert.That(dispatcher.Registrations[0].EventTypeName,
                Is.EqualTo(original.Registrations[0].EventTypeName),
                "The runtime-stable event type name survives the trip.");
            Assert.That(dispatcher.Registrations[0].Target.Index,
                Is.EqualTo(original.Registrations[0].Target.Index));
            Assert.That(dispatcher.Registrations[1].Target.Index,
                Is.EqualTo(original.Registrations[1].Target.Index));
            Assert.That(dispatcher.DefaultTarget.Index, Is.EqualTo(original.DefaultTarget.Index),
                "The Otherwise target survives the trip.");
        });
    }

    [Test]
    public async Task Deserialized_graph_raise_resolves_event_type_and_key_by_name([Values] bool binary)
    {
        ShopFixture shop = Shop();
        Graph rebuilt = await RoundTrip(new GraphSerializer(shop.Codec), shop.Graph, binary);

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result placed = await machine.ExecuteAsync(new OrderPlaced("o-1", 42m));
        Result canceled = await machine.ExecuteAsync(new OrderCanceled("o-1"));
        Result plain = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.EqualTo(Result.Success));
            Assert.That(canceled, Is.EqualTo(Result.Success));
            Assert.That(plain, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[]
            {
                "reserve:o-1", "charge:42", "refund:o-1", "otherwise",
            }), "Both typed raises dispatched by name and the plain run took the Otherwise chain.");
        });
    }

    [Test]
    public async Task Deserialized_graph_raise_of_unregistered_type_throws_naming_registered_types()
    {
        ShopFixture shop = Shop();
        Graph rebuilt = await RoundTrip(new GraphSerializer(shop.Codec), shop.Graph, binary: false);
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(42));
        Assert.That(ex!.Message, Does.Contain("No event entry is registered").And.Contain("OrderPlaced"));
    }

    [Test]
    public async Task Deserialized_graph_raise_with_missing_key_name_throws()
    {
        ShopFixture shop = Shop();
        Graph rebuilt = await RoundTrip(new GraphSerializer(shop.Codec), shop.Graph, binary: false);

        BlackboardSchema other = new("other");
        other.Register<OrderPlaced>("differentName");
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new OrderPlaced("o-2", 1m)));
        Assert.That(ex!.Message, Does.Contain("'orderPlaced'").And.Contain("does not exist"));
    }

    [Test]
    public async Task Deserialized_graph_raise_with_mismatched_key_type_throws()
    {
        ShopFixture shop = Shop();
        Graph rebuilt = await RoundTrip(new GraphSerializer(shop.Codec), shop.Graph, binary: false);

        BlackboardSchema other = new("other");
        other.Register<int>("orderPlaced"); // same name, different value type
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new OrderPlaced("o-3", 1m)));
        Assert.That(ex!.Message, Does.Contain("'orderPlaced'").And.Contain("declared as"));
    }

    // ── Version stamps and back compatibility ────────────────────────────

    [Test]
    public async Task Payload_carries_version_seven_stamp()
    {
        ShopFixture shop = Shop();
        await using MemoryStream stream = new();
        await new GraphSerializer(shop.Codec).ToJsonAsync(shop.Graph, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.That(json, Does.Contain("\"version\": 7"));
    }

    private static GraphSerializer PlainSerializer()
    {
        RegistryCodec codec = new();
        codec.Register("a", new KeyedBoardState("a", _ => Result.Success));
        codec.Register("b", new KeyedBoardState("b", _ => Result.Success));
        return new GraphSerializer(codec);
    }

    [Test]
    public async Task Version_six_payload_reads_event_free()
    {
        string json = """
            {
              "version": 6,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "a" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(PlainSerializer(), json);
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();

        Assert.Multiple(() =>
        {
            Assert.That(((LogicNode)rebuilt.StartNode).AsyncLogic, Is.Not.InstanceOf<EventEntryState>());
            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await machine.ExecuteAsync(new OrderPlaced("x", 0m)));
            Assert.That(ex!.Message, Does.Contain("no event entries"),
                "A pre-v7 payload rebuilds as an ordinary graph with no event surface.");
        });
    }

    [Test]
    public void Newer_payload_version_is_rejected()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version + 1}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("newer than serializer version"));
    }

    // ── Marker-spoof defense and negative fixtures ───────────────────────

    [Test]
    public async Task Event_entry_marker_string_in_ordinary_logic_is_not_honored()
    {
        RegistryCodec codec = new();
        codec.Register("EventEntryState", new KeyedBoardState("EventEntryState", _ => Result.Success));

        Graph graph = GraphBuilder
            .StartWithAsync(codec.Deserialize("EventEntryState"))
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), graph, binary: false);

        Assert.Multiple(() =>
        {
            Assert.That(((LogicNode)rebuilt.StartNode).AsyncLogic, Is.Not.InstanceOf<EventEntryState>());
            Assert.That(((IKeyed)((LogicNode)rebuilt.StartNode).AsyncLogic).Key, Is.EqualTo("EventEntryState"));
        });
    }

    [Test]
    public void Event_entry_claim_on_a_non_marker_node_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "a" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "eventEntries": [
                {
                  "ownerIndex": 0, "defaultTarget": -1,
                  "entries": [ { "keyName": "k", "eventTypeName": "T", "targetIndex": 1 } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("does not reference an event-entry marker"));
    }

    [Test]
    public void Cross_section_claim_overlap_with_forks_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "EventEntryState" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "forks": [ { "ownerIndex": 0, "branches": [ 1 ] } ],
              "eventEntries": [
                {
                  "ownerIndex": 0, "defaultTarget": -1,
                  "entries": [ { "keyName": "k", "eventTypeName": "T", "targetIndex": 1 } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("claimed by both"));
    }

    [Test]
    public void Out_of_range_entry_target_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "EventEntryState" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "eventEntries": [
                {
                  "ownerIndex": 0, "defaultTarget": -1,
                  "entries": [ { "keyName": "k", "eventTypeName": "T", "targetIndex": 9 } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("out of range"));
    }

    [Test]
    public void Empty_entry_array_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "EventEntryState" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "b" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "eventEntries": [ { "ownerIndex": 0, "defaultTarget": -1, "entries": [] } ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(PlainSerializer(), json));
        Assert.That(ex!.Message, Does.Contain("at least one entry"));
    }

    // ── Durability: the full three-artifact loop ─────────────────────────

    [Test]
    public async Task Suspend_mid_handler_full_durability_round_trip_completes_and_reads_the_event()
    {
        ShopFixture shop = Shop();
        GraphSerializer graphSerializer = new(shop.Codec);
        BlackboardSerializer boardSerializer = new();

        Blackboard board = new(shop.Schema);
        AsyncStateMachine running = shop.Graph.ToAsyncStateMachine().WithBlackboard(board);

        Result step1 = await running.StepAsync(new OrderPlaced("o-9", 30m)); // dispatcher
        Result step2 = await running.StepAsync(); // reserve
        Assert.Multiple(() =>
        {
            Assert.That(step1, Is.EqualTo(Result.InProgress));
            Assert.That(step2, Is.EqualTo(Result.InProgress));
        });

        // The three durability artifacts: graph payload, machine snapshot, board payload.
        await using MemoryStream graphStream = new();
        await graphSerializer.ToJsonAsync(shop.Graph, graphStream);
        string snapshotJson = JsonSerializer.Serialize(running.Suspend());
        await using MemoryStream boardStream = new();
        await boardSerializer.ToJsonAsync(board, boardStream);

        // "Process boundary": rebuild everything fresh.
        graphStream.Position = 0;
        Graph rebuiltGraph = await graphSerializer.FromJsonAsync(graphStream);
        Blackboard restoredBoard = new(shop.Schema);
        boardStream.Position = 0;
        await boardSerializer.RestoreFromJsonAsync(restoredBoard, boardStream);

        AsyncStateMachine resumed = rebuiltGraph.ToAsyncStateMachine().WithBlackboard(restoredBoard);
        resumed.Resume(JsonSerializer.Deserialize<StateMachineSnapshot>(snapshotJson)!);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await resumed.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Does.Contain("charge:30"),
                "The handler's later step read the event payload from the restored board — no event replay.");
        });
    }
}
