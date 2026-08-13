using System.Text;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 11: hierarchical outcome keys on the wire. The nested-machine SubGraphs
/// entries and the history kinds of CompositeDto carry optional outcome key names (names
/// only — keys never ride); a deserialized graph rebuilds name-bound and resolves the keys
/// against the machine's bound boards per publish, with targeted miss/mismatch errors.
/// Pre-v11 payloads read outcome-key-free; non-history composite kinds reject smuggled names.
/// </summary>
[TestFixture]
[Category("serialization")]
public class OutcomeKeySerializationTests
{
    // ── Test doubles ─────────────────────────────────────────────────────

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

    private static (BlackboardSchema Schema, Blackboard Board, BlackboardKey<int> Code,
        BlackboardKey<string> Name) Boards()
    {
        BlackboardSchema schema = new("outcome-wire", BlackboardScope.Graph);
        BlackboardKey<int> code = schema.Register("verdict", -1);
        BlackboardKey<string> name = schema.Register("verdictName", "unset");
        return (schema, new Blackboard(schema), code, name);
    }

    private static Graph Child(RegistryCodec codec)
    {
        return GraphBuilder
            .StartWithAsync(codec.Ok("c0"))
            .ToAsync(codec.Ok("c1"))
            .SetName("c-end").WithOutcome(7, "Landed")
            .Build();
    }

    // ── Round-trips: nested machines (both markers) ──────────────────────

    [Test]
    public async Task Async_nested_machine_with_outcome_keys_roundtrips_and_publishes([Values] bool binary)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        RegistryCodec codec = new();
        codec.Ok("p0");

        Graph parent = GraphBuilder
            .StartWithAsync(codec.Deserialize("p0"))
            .SubGraph(Child(codec), history: false, code, name).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.GetNodeByIndex(1);
        AsyncStateMachine nested = (AsyncStateMachine)composite.AsyncLogic;

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(nested.OutcomeCodeKeyName, Is.EqualTo("verdict"),
                "The declaration survives the trip as a key name.");
            Assert.That(nested.OutcomeNameKeyName, Is.EqualTo("verdictName"));
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(code), Is.EqualTo(7),
                "The name-bound keys resolved against the bound board and published.");
            Assert.That(board.Get(name), Is.EqualTo("Landed"));
        });
    }

    [Test]
    public async Task Sync_nested_machine_with_outcome_keys_roundtrips_and_publishes([Values] bool binary)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        RegistryCodec codec = new();
        codec.Ok("p0");

        Graph parent = GraphBuilder
            .StartWithAsync(codec.Deserialize("p0"))
            .SubGraph(ParallelStepMode.RunToJoin, Child(codec), history: false, code, name).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        LogicNode composite = (LogicNode)rebuilt.GetNodeByIndex(1);
        StateMachine nested = (StateMachine)composite.Logic!;

        // Machine-level step mode does not ride: the rebuilt nested machine carries the
        // RoundPerTick default, so drive the parent as a sync RunToJoin machine (the parent
        // loop absorbs the child's InProgress ticks) — the publish is the child machine's
        // own, either way.
        StateMachine machine = rebuilt.ToStateMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        machine.SetBlackboard(board);
        Result result = machine.Execute();
        await Task.CompletedTask;

        Assert.Multiple(() =>
        {
            Assert.That(nested.OutcomeCodeKeyName, Is.EqualTo("verdict"));
            Assert.That(nested.OutcomeNameKeyName, Is.EqualTo("verdictName"));
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(code), Is.EqualTo(7));
            Assert.That(board.Get(name), Is.EqualTo("Landed"));
        });
    }

    // ── Round-trips: history composites ──────────────────────────────────

    [Test]
    public async Task Async_history_composite_with_outcome_keys_roundtrips_and_publishes([Values] bool binary)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        RegistryCodec codec = new();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(Child(codec), history: true, code, name).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        AsyncHistoryState composite = (AsyncHistoryState)((LogicNode)rebuilt.StartNode).AsyncLogic;

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(composite.OutcomeCodeKeyName, Is.EqualTo("verdict"));
            Assert.That(composite.OutcomeNameKeyName, Is.EqualTo("verdictName"));
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(code), Is.EqualTo(7));
            Assert.That(board.Get(name), Is.EqualTo("Landed"));
        });
    }

    [Test]
    public async Task Sync_history_composite_with_outcome_keys_roundtrips_and_publishes([Values] bool binary)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        RegistryCodec codec = new();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, Child(codec), history: true, code, name).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        HistoryState composite = (HistoryState)((LogicNode)rebuilt.StartNode).Logic!;

        StateMachine machine = rebuilt.ToStateMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        machine.SetBlackboard(board);
        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(composite.OutcomeCodeKeyName, Is.EqualTo("verdict"));
            Assert.That(composite.OutcomeNameKeyName, Is.EqualTo("verdictName"));
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(code), Is.EqualTo(7));
            Assert.That(board.Get(name), Is.EqualTo("Landed"));
        });
    }

    [Test]
    public async Task Partial_declaration_roundtrips_as_declared([Values] bool binary)
    {
        (_, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) = Boards();
        RegistryCodec codec = new();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(Child(codec), history: false, outcomeCode: code).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary);
        AsyncStateMachine nested = (AsyncStateMachine)((LogicNode)rebuilt.StartNode).AsyncLogic;

        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(nested.OutcomeCodeKeyName, Is.EqualTo("verdict"));
            Assert.That(nested.OutcomeNameKeyName, Is.Null);
            Assert.That(board.Get(code), Is.EqualTo(7));
            Assert.That(board.Get(name), Is.EqualTo("unset"), "The undeclared key stays untouched.");
        });
    }

    // ── Resolution errors on deserialized graphs ─────────────────────────

    [Test]
    public async Task Deserialized_keys_miss_with_a_targeted_error_when_the_board_lacks_them()
    {
        RegistryCodec codec = new();
        (_, _, BlackboardKey<int> code, _) = Boards();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(Child(codec), history: false, outcomeCode: code).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);

        // Bind a board whose schema does not declare 'verdict' at all.
        BlackboardSchema other = new("other-schema", BlackboardScope.Graph);
        other.Register("unrelated", 0);
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'verdict'").And.Contain("does not exist"));
    }

    [Test]
    public async Task Deserialized_keys_mismatch_with_a_targeted_error_when_the_type_changed()
    {
        RegistryCodec codec = new();
        (_, _, BlackboardKey<int> code, _) = Boards();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(Child(codec), history: false, outcomeCode: code).SetName("nested")
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(codec), parent, binary: false);

        // Same name, different value type.
        BlackboardSchema other = new("other-schema", BlackboardScope.Graph);
        other.Register("verdict", "not-an-int");
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine();
        machine.SetBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'verdict'").And.Contain("declared as"));
    }

    // ── Version stamps and back compatibility ────────────────────────────

    [Test]
    public async Task Payload_carries_current_version_stamp()
    {
        (_, _, BlackboardKey<int> code, _) = Boards();
        RegistryCodec codec = new();
        Graph parent = GraphBuilder
            .Start()
            .SubGraph(Child(codec), history: false, outcomeCode: code)
            .Build();

        await using MemoryStream stream = new();
        await new GraphSerializer(codec).ToJsonAsync(parent, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
            Assert.That(json, Does.Contain("\"outcomeCodeKeyName\": \"verdict\""));
        });
    }

    [Test]
    public async Task Version_ten_payload_reads_outcome_key_free()
    {
        RegistryCodec codec = new();
        codec.Ok("c0");

        // A pre-v11 payload: nested machine and history composite entries with no outcome
        // key names — the shapes v10 wrote.
        string json = """
            {
              "version": 10,
              "nodes": [
                { "$type": "txt", "index": 0, "name": "nested", "logic": "StateMachine" },
                { "$type": "txt", "index": 1, "name": "kept", "logic": "HistoryState" }
              ],
              "transitions": [ { "destination": 1 }, { "destination": -1 } ],
              "subGraphs": [
                {
                  "ownerIndex": 0,
                  "graph": {
                    "version": 10,
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
                      "version": 10,
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

        // No keys declared: the rebuilt graph runs board-less exactly like today's payloads.
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(nested.OutcomeCodeKeyName, Is.Null,
                "A pre-v11 payload rebuilds outcome-key-free.");
            Assert.That(nested.OutcomeNameKeyName, Is.Null);
            Assert.That(history.OutcomeCodeKeyName, Is.Null);
            Assert.That(history.OutcomeNameKeyName, Is.Null);
            Assert.That(result, Is.EqualTo(Result.Success));
        });
    }

    [Test]
    public async Task Outcome_key_names_on_a_parallel_kind_are_rejected()
    {
        RegistryCodec codec = new();
        codec.Ok("c0");

        string json = """
            {
              "version": 11,
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
                      "version": 11,
                      "nodes": [ { "$type": "txt", "index": 0, "name": "c0", "logic": "c0" } ],
                      "transitions": [ { "destination": -1 } ],
                      "subGraphs": [], "composites": [], "name": null, "index": -1
                    }
                  ],
                  "outcomeCodeKeyName": "verdict"
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(codec), json));
        Assert.That(ex!.Message, Does.Contain("outcome key name").And.Contain("history"));
    }
}
