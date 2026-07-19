using System.Text;
using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 8: behavior composites on the wire. The standard set (Log, closed
/// SetValue&lt;T&gt;) rides with zero options via the default registry; entries serialize
/// into the neutral field model; key bindings ride by name and rebind against the machine's
/// bound boards at execution; AgentTypeName closes the typed composites on read (the agent
/// itself never rides — re-attached via SetAgent).
/// </summary>
[TestFixture]
[Category("serialization")]
public class BehaviorSerializationTests
{
    public sealed class Hero
    {
        public List<string> Visits { get; } = [];
    }

    private sealed class GreetHero : IBehavior<Hero>, IAsyncBehavior<Hero>, ISerializableBehavior
    {
        public Result Execute(Hero agent, in BehaviorContext ctx)
        {
            agent.Visits.Add("greeted");
            return Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(Hero agent, BehaviorContext ctx, CancellationToken ct)
        {
            agent.Visits.Add("greeted");
            return ResultHelpers.Success;
        }

        public void Write(BehaviorFieldWriter writer)
        {
            // No fields — the greeting is pure agent access.
        }
    }

    private sealed class Beep(int times, BlackboardValue<string> sound)
        : IBehavior, IAsyncBehavior, ISerializableBehavior
    {
        public int Times { get; } = times;
        public BlackboardValue<string> Sound { get; } = sound;

        public Result Execute(in BehaviorContext ctx) => Result.Success;

        public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct) => ResultHelpers.Success;

        public void Write(BehaviorFieldWriter writer)
        {
            writer.WriteInt32("times", Times);
            writer.WriteBinding("sound", Sound);
        }
    }

    /// <summary>Not ISerializableBehavior and unknown to the registry — must fail loud on write.</summary>
    private sealed class OpaqueBehavior : IBehavior
    {
        public Result Execute(in BehaviorContext ctx) => Result.Success;
    }

    private sealed class DummyCodec : ILogicTextCodec
    {
        public string Serialize(IAsyncLogic data) => "noop";

        public IAsyncLogic Deserialize(string s) => new EmptyAsyncLogic();
    }

    private sealed class LogCapturingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Messages = [];

        public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct)
        {
            Messages.Add(message);
            return default;
        }
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

    private static async Task<string> ToJson(GraphSerializer serializer, Graph graph)
    {
        await using MemoryStream stream = new();
        await serializer.ToJsonAsync(graph, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<Graph> FromJson(GraphSerializer serializer, string json)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        return await serializer.FromJsonAsync(source);
    }

    private static IBehaviorComposite CompositeAt(Graph graph, int index)
    {
        LogicNode node = (LogicNode)graph.GetNodeByIndex(index);
        return (node.Logic as IBehaviorComposite ?? node.AsyncLogic as IBehaviorComposite)!;
    }

    private static (BlackboardSchema Schema, BlackboardKey<string> Message, BlackboardKey<int> Target) TestSchema()
    {
        BlackboardSchema schema = new("shop");
        BlackboardKey<string> message = schema.Register("msg", "default-msg");
        BlackboardKey<int> target = schema.Register("target", 0);
        return (schema, message, target);
    }

    // ── Standard-set round trips (zero options) ──────────────────────────

    [Test]
    public async Task Standard_set_roundtrips_with_zero_options([Values] bool binary)
    {
        (BlackboardSchema schema, BlackboardKey<string> message, BlackboardKey<int> target) = TestSchema();

        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Log(LogSeverity.Error, message), new SetValue<int>(target, 5)) // sync node
            .ToBehaviorsAsync(new Log("literal message")) // async node
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);

        IBehaviorComposite syncComposite = CompositeAt(rebuilt, 0);
        IBehaviorComposite asyncComposite = CompositeAt(rebuilt, 1);

        Assert.Multiple(() =>
        {
            Assert.That(syncComposite, Is.Not.Null.And.InstanceOf<BehaviorState>());
            Assert.That(asyncComposite, Is.Not.Null.And.InstanceOf<AsyncBehaviorState>());

            Log rebuiltLog = (Log)syncComposite.Entries[0];
            Assert.That(rebuiltLog.Severity.IsBound, Is.False);
            Assert.That(rebuiltLog.Severity.Literal, Is.EqualTo(LogSeverity.Error));
            Assert.That(rebuiltLog.Message.IsBound, Is.True);
            Assert.That(rebuiltLog.Message.KeyName, Is.EqualTo("msg"),
                "The key form rides by name only.");

            SetValue<int> rebuiltSet = (SetValue<int>)syncComposite.Entries[1];
            Assert.That(rebuiltSet.KeyName, Is.EqualTo("target"));
            Assert.That(rebuiltSet.Key.IsValid, Is.False, "Deserialized targets are name-bound.");
            Assert.That(rebuiltSet.Value.IsBound, Is.False);
            Assert.That(rebuiltSet.Value.Literal, Is.EqualTo(5));

            Log literalLog = (Log)asyncComposite.Entries[0];
            Assert.That(literalLog.Message.IsBound, Is.False);
            Assert.That(literalLog.Message.Literal, Is.EqualTo("literal message"));
        });
    }

    [Test]
    public async Task Payload_carries_markers_section_and_current_version_stamp()
    {
        (BlackboardSchema schema, BlackboardKey<string> message, BlackboardKey<int> target) = TestSchema();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Log(message))
            .ToBehaviorsAsync(new SetValue<int>(target, 1))
            .WithSchema(schema)
            .Build();

        string json = await ToJson(new GraphSerializer(new DummyCodec()), graph);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
            Assert.That(json, Does.Contain("\"BehaviorState\""));
            Assert.That(json, Does.Contain("\"AsyncBehaviorState\""));
            Assert.That(json, Does.Contain("\"behaviors\""));
            Assert.That(json, Does.Contain("NxGraph.Behaviors.Log"));
            // The JSON writer escapes the generic-arity backtick as ` (default encoder);
            // unescape it before the ordinal containment check.
            Assert.That(json.Replace("\\u0060", "`")
                    .Contains("NxGraph.Behaviors.SetValue`1[System.Int32]", StringComparison.Ordinal),
                Is.True, "SetValue rides under its runtime-stable closed-generic name.");
        });
    }

    [Test]
    public async Task Bindings_rebind_by_name_and_execute_on_the_rebuilt_graph([Values] bool binary)
    {
        (BlackboardSchema schema, BlackboardKey<string> message, BlackboardKey<int> target) = TestSchema();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Log(LogSeverity.Warning, message), new SetValue<int>(target, 42))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);

        LogCapturingAsyncObserver observer = new();
        Blackboard board = new(schema);
        board.Set(message, "from-board");
        Result result = await rebuilt.ToAsyncStateMachine(observer).WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "[Warning] from-board" }),
                "The key-form binding resolved by name against the bound board.");
            Assert.That(board.Get(target), Is.EqualTo(42),
                "The name-bound SetValue target resolved and wrote.");
        });
    }

    [Test]
    public async Task Rebind_against_a_schema_missing_the_key_throws_targeted()
    {
        (BlackboardSchema schema, BlackboardKey<string> message, _) = TestSchema();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Log(message))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary: false);

        BlackboardSchema other = new("other");
        other.Register<string>("differentName");
        LogCapturingAsyncObserver observer = new(); // a wired reporter forces the binding resolve
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine(observer)
            .WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'msg'").And.Contain("does not exist"));
    }

    [Test]
    public async Task Rebind_against_a_mismatched_value_type_throws_targeted()
    {
        (BlackboardSchema schema, BlackboardKey<string> message, _) = TestSchema();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Log(message))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary: false);

        BlackboardSchema other = new("other");
        other.Register<int>("msg"); // same name, different value type
        LogCapturingAsyncObserver observer = new();
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine(observer)
            .WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'msg'").And.Contain("declared as"));
    }

    // ── Custom behaviors via registered factory ──────────────────────────

    [Test]
    public async Task Custom_behavior_roundtrips_via_registered_factory([Values] bool binary)
    {
        BlackboardSchema schema = new("sounds");
        BlackboardKey<string> soundKey = schema.Register("sound", "beep");

        BehaviorRegistry registry = new();
        registry.Register(typeof(Beep).FullName!,
            fields => new Beep(fields.ReadInt32("times"), fields.ReadBinding<string>("sound")));

        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new Beep(3, soundKey), new Beep(1, "literal-buzz"))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);
        IBehaviorComposite composite = CompositeAt(rebuilt, 0);

        Assert.Multiple(() =>
        {
            Assert.That(composite.Entries, Has.Count.EqualTo(2));
            Beep first = (Beep)composite.Entries[0];
            Assert.That(first.Times, Is.EqualTo(3));
            Assert.That(first.Sound.KeyName, Is.EqualTo("sound"));
            Beep second = (Beep)composite.Entries[1];
            Assert.That(second.Times, Is.EqualTo(1));
            Assert.That(second.Sound.IsBound, Is.False);
            Assert.That(second.Sound.Literal, Is.EqualTo("literal-buzz"));
        });
    }

    [Test]
    public void Unserializable_behavior_fails_write_naming_the_registry()
    {
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new OpaqueBehavior())
            .Build();

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ToJson(new GraphSerializer(new DummyCodec()), graph));
        Assert.That(ex!.Message, Does.Contain("OpaqueBehavior").And.Contain("BehaviorRegistry"));
    }

    [Test]
    public async Task Unregistered_behavior_name_fails_read_naming_the_registry()
    {
        BehaviorRegistry writeRegistry = new();
        // Registered for the write-side test graph but not on the reading serializer.
        GraphSerializer writer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = writeRegistry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new Beep(2, "x"))
            .Build();

        string json = await ToJson(writer, graph); // Beep is ISerializableBehavior — write needs no factory

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("Beep").And.Contain("BehaviorRegistry"));
    }

    // ── Agent-typed composites ───────────────────────────────────────────

    [Test]
    public async Task Typed_composite_roundtrips_and_reattaches_the_agent([Values] bool binary)
    {
        BehaviorRegistry registry = new();
        registry.Register(typeof(GreetHero).FullName!, _ => new GreetHero());
        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync<Hero>(new Log("typed node"), new GreetHero())
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);
        IBehaviorComposite composite = CompositeAt(rebuilt, 0);

        Hero hero = new();
        Result result = await rebuilt.ToAsyncStateMachine<Hero>().WithAgent(hero).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(composite, Is.InstanceOf<AsyncBehaviorState<Hero>>(),
                "AgentTypeName closed the generic composite on read.");
            Assert.That(composite.AgentType, Is.EqualTo(typeof(Hero)));
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(hero.Visits, Is.EqualTo(new[] { "greeted" }),
                "The re-attached agent reached the typed entry.");
        });
    }

    [Test]
    public async Task Sync_typed_composite_roundtrips([Values] bool binary)
    {
        BehaviorRegistry registry = new();
        registry.Register(typeof(GreetHero).FullName!, _ => new GreetHero());
        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviors<Hero>(new GreetHero())
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);

        Assert.That(CompositeAt(rebuilt, 0), Is.InstanceOf<BehaviorState<Hero>>());
    }

    // ── Version stamps, back compatibility, spoof defense ────────────────

    [Test]
    public async Task Version_seven_payload_reads_behavior_free()
    {
        string json = """
            {
              "version": 7,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "noop" } ],
              "transitions": [ { "destination": -1 } ],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(new GraphSerializer(new DummyCodec()), json);

        Assert.That(((LogicNode)rebuilt.StartNode).AsyncLogic, Is.Not.InstanceOf<AsyncBehaviorState>(),
            "A pre-v8 payload rebuilds as an ordinary graph with no behavior surface.");
    }

    [Test]
    public async Task Behavior_marker_string_in_ordinary_logic_is_not_honored()
    {
        // A codec that legitimately emits the marker string for ordinary logic: without a
        // BehaviorDto claiming the index, the string must fall through to the codec.
        Graph graph = GraphBuilder.Start().ToAsync(new EmptyAsyncLogic()).Build();

        string json = (await ToJson(new GraphSerializer(new MarkerEmittingCodec()), graph))
            .Replace("\"logic\": \"noop\"", "\"logic\": \"BehaviorState\"");

        Graph rebuilt = await FromJson(new GraphSerializer(new MarkerEmittingCodec()), json);

        Assert.That(((LogicNode)rebuilt.StartNode).AsyncLogic, Is.InstanceOf<EmptyAsyncLogic>());
    }

    private sealed class MarkerEmittingCodec : ILogicTextCodec
    {
        public string Serialize(IAsyncLogic data) => "noop";

        public IAsyncLogic Deserialize(string s) => s is "noop" or "BehaviorState"
            ? new EmptyAsyncLogic()
            : throw new InvalidOperationException($"Unknown logic key '{s}'.");
    }

    [Test]
    public void Behavior_claim_on_a_non_marker_node_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "noop" } ],
              "transitions": [ { "destination": -1 } ],
              "behaviors": [
                {
                  "ownerIndex": 0, "isSync": true, "agentTypeName": null,
                  "entries": [ { "behaviorTypeName": "NxGraph.Behaviors.Log", "fields": [] } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("does not reference a").And.Contain("behavior marker"));
    }

    [Test]
    public void Marker_runtime_flavor_must_match_the_claim()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "AsyncBehaviorState" } ],
              "transitions": [ { "destination": -1 } ],
              "behaviors": [
                {
                  "ownerIndex": 0, "isSync": true, "agentTypeName": null,
                  "entries": [ { "behaviorTypeName": "NxGraph.Behaviors.Log", "fields": [] } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("sync").And.Contain("marked"));
    }

    [Test]
    public void Cross_section_claim_overlap_with_forks_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "BehaviorState" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "noop" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "forks": [ { "ownerIndex": 0, "branches": [ 1 ] } ],
              "behaviors": [
                { "ownerIndex": 0, "isSync": true, "agentTypeName": null, "entries": [] }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("claimed by both"));
    }

    [Test]
    public void Empty_entry_array_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "BehaviorState" } ],
              "transitions": [ { "destination": -1 } ],
              "behaviors": [
                { "ownerIndex": 0, "isSync": true, "agentTypeName": null, "entries": [] }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("at least one entry"));
    }
}
