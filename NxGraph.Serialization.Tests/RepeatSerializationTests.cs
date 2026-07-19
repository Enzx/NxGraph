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
/// Payload version 9: nested behavior entries on the wire (spec 015). All four repeat forms
/// (<c>Repeat</c>/<c>AsyncRepeat</c> plus the <c>&lt;TAgent&gt;</c> twins) ride with zero
/// options via the default registry; bodies encode recursively through the serializer's entry
/// codec (so nested user behaviors follow the top-level rules); key-bound counts and index
/// keys rebind by name; read-side nesting is capped; a wrong-family body entry fails with a
/// targeted error instead of a cast exception; pre-v9 payloads read unchanged.
/// </summary>
[TestFixture]
[Category("serialization")]
public class RepeatSerializationTests
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

    /// <summary>Sync-only — the wrong-family probe for a crafted AsyncRepeat body.</summary>
    private sealed class SyncOnlyBeep : IBehavior, ISerializableBehavior
    {
        public Result Execute(in BehaviorContext ctx) => Result.Success;

        public void Write(BehaviorFieldWriter writer)
        {
        }
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

    private static (BlackboardSchema Schema, BlackboardKey<int> Trips, BlackboardKey<int> Index,
        BlackboardKey<int> Target) LoopSchema()
    {
        BlackboardSchema schema = new("loops");
        BlackboardKey<int> trips = schema.Register("trips", 2);
        BlackboardKey<int> index = schema.Register("i", -1);
        BlackboardKey<int> target = schema.Register("target", 0);
        return (schema, trips, index, target);
    }

    // ── Untyped forms round-trip with zero options ───────────────────────

    [Test]
    public async Task Untyped_repeat_forms_roundtrip_with_zero_options([Values] bool binary)
    {
        (BlackboardSchema schema, BlackboardKey<int> trips, BlackboardKey<int> index,
            BlackboardKey<int> target) = LoopSchema();

        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Repeat(trips, index, new Log("sync tick"), new SetValue<int>(target, 5)))
            .ToBehaviorsAsync(new AsyncRepeat(3, new Log("async tick")))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);

        Repeat syncRepeat = (Repeat)CompositeAt(rebuilt, 0).Entries[0];
        AsyncRepeat asyncRepeat = (AsyncRepeat)CompositeAt(rebuilt, 1).Entries[0];

        Assert.Multiple(() =>
        {
            Assert.That(syncRepeat.Count.IsBound, Is.True);
            Assert.That(syncRepeat.Count.KeyName, Is.EqualTo("trips"), "The count key rides by name only.");
            Assert.That(syncRepeat.IndexKeyName, Is.EqualTo("i"));
            Assert.That(syncRepeat.Body, Has.Count.EqualTo(2));
            Assert.That(syncRepeat.Body[0], Is.InstanceOf<Log>());
            Assert.That(syncRepeat.Body[1], Is.InstanceOf<SetValue<int>>(),
                "Nested standard entries reconstruct through the same registry dispatch.");

            Assert.That(asyncRepeat.Count.IsBound, Is.False);
            Assert.That(asyncRepeat.Count.Literal, Is.EqualTo(3));
            Assert.That(asyncRepeat.IndexKeyName, Is.Null, "No index key rides as a null name.");
            Assert.That(asyncRepeat.Body, Has.Count.EqualTo(1));
            Assert.That(asyncRepeat.Body[0], Is.InstanceOf<Log>());
        });
    }

    [Test]
    public async Task Rebound_count_and_index_key_execute_on_the_rebuilt_graph([Values] bool binary)
    {
        (BlackboardSchema schema, BlackboardKey<int> trips, BlackboardKey<int> index,
            BlackboardKey<int> target) = LoopSchema();

        // The body copies the live index into the target key — after the trips resolved from
        // the board, the target holds the last 0-based index.
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(trips, index, new Log("tick"), new SetValue<int>(target, index)))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);

        LogCapturingAsyncObserver observer = new();
        Blackboard board = new(schema);
        board.Set(trips, 3);
        Result result = await rebuilt.ToAsyncStateMachine(observer).WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Has.Count.EqualTo(3),
                "The name-bound count resolved against the bound board.");
            Assert.That(board.Get(target), Is.EqualTo(2),
                "The name-bound index key was written 0-based before each iteration.");
        });
    }

    // ── Typed forms close the generic and re-attach the agent ────────────

    [Test]
    public async Task Typed_repeat_roundtrips_closing_the_generic_and_reattaching_the_agent([Values] bool binary)
    {
        BehaviorRegistry registry = new();
        registry.Register(typeof(GreetHero).FullName!, _ => new GreetHero());
        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync<Hero>(new AsyncRepeat<Hero>(2, new Log("typed"), new GreetHero()))
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);
        IBehaviorComposite composite = CompositeAt(rebuilt, 0);

        Hero hero = new();
        Result result = await rebuilt.ToAsyncStateMachine<Hero>().WithAgent(hero).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(composite, Is.InstanceOf<AsyncBehaviorState<Hero>>());
            Assert.That(composite.Entries[0], Is.InstanceOf<AsyncRepeat<Hero>>(),
                "The stable-name prefix closed AsyncRepeat<TAgent> on read.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(hero.Visits, Is.EqualTo(new[] { "greeted", "greeted" }),
                "The re-attached agent reached the typed body entry every iteration.");
        });
    }

    [Test]
    public async Task Sync_typed_repeat_roundtrips([Values] bool binary)
    {
        BehaviorRegistry registry = new();
        registry.Register(typeof(GreetHero).FullName!, _ => new GreetHero());
        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviors<Hero>(new Repeat<Hero>(2, new GreetHero()))
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);
        IBehaviorComposite composite = CompositeAt(rebuilt, 0);

        Assert.Multiple(() =>
        {
            Assert.That(composite, Is.InstanceOf<BehaviorState<Hero>>());
            Assert.That(composite.Entries[0], Is.InstanceOf<Repeat<Hero>>());
        });
    }

    // ── User behaviors inside a body ─────────────────────────────────────

    [Test]
    public async Task User_behavior_in_a_body_roundtrips_via_registered_factory([Values] bool binary)
    {
        BlackboardSchema schema = new("sounds");
        BlackboardKey<string> soundKey = schema.Register("sound", "beep");

        BehaviorRegistry registry = new();
        registry.Register(typeof(Beep).FullName!,
            fields => new Beep(fields.ReadInt32("times"), fields.ReadBinding<string>("sound")));

        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(2, new Beep(3, soundKey)))
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);
        AsyncRepeat repeat = (AsyncRepeat)CompositeAt(rebuilt, 0).Entries[0];

        Assert.Multiple(() =>
        {
            Assert.That(repeat.Body, Has.Count.EqualTo(1));
            Beep beep = (Beep)repeat.Body[0];
            Assert.That(beep.Times, Is.EqualTo(3), "The nested entry wrote itself via ISerializableBehavior.");
            Assert.That(beep.Sound.KeyName, Is.EqualTo("sound"),
                "Nested bindings ride by name, exactly like top-level entries.");
        });
    }

    [Test]
    public async Task Unregistered_user_behavior_in_a_body_fails_read_naming_the_registry()
    {
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(2, new Beep(1, "x")))
            .Build();

        // Beep is ISerializableBehavior — the write needs no factory; the zero-options reader does.
        string json = await ToJson(new GraphSerializer(new DummyCodec()), graph);

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("Beep").And.Contain("BehaviorRegistry"));
    }

    // ── Crafted payloads ─────────────────────────────────────────────────

    [Test]
    public async Task Nesting_beyond_the_depth_cap_is_rejected_on_read()
    {
        // Construction is bottom-up and legal at any depth; the write side is uncapped. The
        // read side treats 40 nested bodies as a crafted payload and stops at the cap.
        Repeat deep = new(1, new Log("leaf"));
        for (int i = 0; i < 40; i++)
        {
            deep = new Repeat(1, deep);
        }

        Graph graph = GraphBuilder.Start().ToBehaviors(deep).Build();
        GraphSerializer serializer = new(new DummyCodec());

        await using MemoryStream stream = new();
        await serializer.ToBinaryAsync(graph, stream);
        stream.Position = 0;

        // MessagePack wraps formatter errors — the targeted message rides the innermost exception.
        Exception? ex = Assert.CatchAsync(async () => await serializer.FromBinaryAsync(stream));
        while (ex!.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        Assert.Multiple(() =>
        {
            Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
            Assert.That(ex!.Message, Does.Contain("nesting depth"));
        });
    }

    [Test]
    public void Wrong_family_body_entry_fails_with_the_targeted_error()
    {
        // A crafted payload smuggles a registered sync-only behavior into an AsyncRepeat body
        // — the reconstruction must fail naming the offender, never as a cast exception.
        BehaviorRegistry registry = new();
        registry.Register(typeof(SyncOnlyBeep).FullName!, _ => new SyncOnlyBeep());
        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { BehaviorRegistry = registry });

        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "AsyncBehaviorState" } ],
              "transitions": [ { "destination": -1 } ],
              "behaviors": [
                {
                  "ownerIndex": 0, "isSync": false, "agentTypeName": null,
                  "entries": [
                    {
                      "behaviorTypeName": "NxGraph.Behaviors.AsyncRepeat",
                      "fields": [
                        { "name": "count",
                          "value": { "kind": 7, "binding": { "keyName": null, "literal": { "kind": 2, "integer": 1 } } } },
                        { "name": "indexKey", "value": { "kind": 0, "text": null } },
                        { "name": "body",
                          "value": { "kind": 8, "entries": [
                            { "behaviorTypeName": "{{typeof(SyncOnlyBeep).FullName}}", "fields": [] }
                          ] } }
                      ]
                    }
                  ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("SyncOnlyBeep").And.Contain("IAsyncBehavior"));
    }

    // ── Version stamps and back compatibility ────────────────────────────

    [Test]
    public async Task Payload_carries_the_version_nine_stamp_and_stable_repeat_names()
    {
        (BlackboardSchema schema, BlackboardKey<int> trips, BlackboardKey<int> index, _) = LoopSchema();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Repeat(trips, index, new Log("tick")))
            .WithSchema(schema)
            .Build();

        string json = await ToJson(new GraphSerializer(new DummyCodec()), graph);

        Assert.Multiple(() =>
        {
            Assert.That(SerializationVersion.Version, Is.EqualTo(9));
            Assert.That(json, Does.Contain("\"version\": 9"));
            Assert.That(json, Does.Contain("NxGraph.Behaviors.Repeat"));
            Assert.That(json, Does.Contain("NxGraph.Behaviors.Log"),
                "The nested body entry rides under its own stable name.");
        });
    }

    [Test]
    public async Task Version_eight_payload_reads_unchanged()
    {
        // A v8-era payload: behavior field values carry no entries slot. It must rebuild
        // exactly as before — the v9 change lives entirely inside the field model.
        string json = """
            {
              "version": 8,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "BehaviorState" } ],
              "transitions": [ { "destination": -1 } ],
              "behaviors": [
                {
                  "ownerIndex": 0, "isSync": true, "agentTypeName": null,
                  "entries": [
                    {
                      "behaviorTypeName": "NxGraph.Behaviors.Log",
                      "fields": [
                        { "name": "severity",
                          "value": { "kind": 7, "binding": { "keyName": null, "literal": { "kind": 6, "text": "Warning" } } } },
                        { "name": "message",
                          "value": { "kind": 7, "binding": { "keyName": null, "literal": { "kind": 0, "text": "old payload" } } } }
                      ]
                    }
                  ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(new GraphSerializer(new DummyCodec()), json);
        Log log = (Log)CompositeAt(rebuilt, 0).Entries[0];

        Assert.Multiple(() =>
        {
            Assert.That(log.Severity.Literal, Is.EqualTo(LogSeverity.Warning));
            Assert.That(log.Message.Literal, Is.EqualTo("old payload"));
        });
    }
}
