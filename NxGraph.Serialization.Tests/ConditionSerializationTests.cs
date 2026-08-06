using System.Text;
using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Conditions;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 10: data-built branching on the wire. The standard condition set
/// (<c>IsTrue</c>, <c>Not</c>, closed <c>KeyEquals&lt;T&gt;</c>) rides with zero options via
/// the default registry; conditions serialize into the same neutral field model behaviors use;
/// the tested keys ride by name and rebind against the machine's bound boards at evaluation —
/// so a branching graph survives the trip and still decides the same way.
/// </summary>
[TestFixture]
[Category("serialization")]
public class ConditionSerializationTests
{
    // ── Test conditions ──────────────────────────────────────────────────

    /// <summary>
    /// Custom condition: true when its bound operand is at least 3. Serializable on its own
    /// (<see cref="ISerializableCondition"/>), reconstructed through a registered factory.
    /// </summary>
    private sealed class AtLeastThree(BlackboardValue<int> operand) : ICondition, ISerializableCondition
    {
        public BlackboardValue<int> Operand { get; } = operand;

        public bool Evaluate(in BehaviorContext ctx) => ctx.Resolve(Operand) >= 3;

        public void Write(BehaviorFieldWriter writer) => writer.WriteBinding("operand", Operand);
    }

    /// <summary>Not ISerializableCondition and unknown to the registry — must fail loud on write.</summary>
    private sealed class OpaqueCondition : ICondition
    {
        public bool Evaluate(in BehaviorContext ctx) => true;
    }

    private sealed class DummyCodec : ILogicTextCodec
    {
        public string Serialize(IAsyncLogic data) => "noop";

        public IAsyncLogic Deserialize(string s) => new EmptyAsyncLogic();
    }

    /// <summary>A codec that legitimately emits the branch marker strings for ordinary logic.</summary>
    private sealed class MarkerEmittingCodec : ILogicTextCodec
    {
        public string Serialize(IAsyncLogic data) => "noop";

        public IAsyncLogic Deserialize(string s) => s is "noop" or "ChoiceState" or "SwitchState"
            ? new EmptyAsyncLogic()
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

    private static IChoiceNode ChoiceAt(Graph graph, int index)
    {
        LogicNode node = (LogicNode)graph.GetNodeByIndex(index);
        return (node.Logic as IChoiceNode ?? node.AsyncLogic as IChoiceNode)!;
    }

    private static ISwitchNode SwitchAt(Graph graph, int index)
    {
        LogicNode node = (LogicNode)graph.GetNodeByIndex(index);
        return (node.Logic as ISwitchNode ?? node.AsyncLogic as ISwitchNode)!;
    }

    /// <summary>The gate schema every branch fixture below reads and writes.</summary>
    private sealed class Gate
    {
        public BlackboardSchema Schema { get; } = new("gate");
        public BlackboardKey<bool> Open { get; }
        public BlackboardKey<int> Level { get; }
        public BlackboardKey<int> Result { get; }

        public Gate()
        {
            Open = Schema.Register("open", false);
            Level = Schema.Register("level", 0);
            Result = Schema.Register("result", 0);
        }

        /// <summary>Runs <paramref name="graph"/> over a freshly seeded board and reports the arm taken.</summary>
        public async Task<int> Decide(Graph graph, bool open, int level)
        {
            Blackboard board = new(Schema);
            board.Set(Open, open);
            board.Set(Level, level);
            Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();
            Assert.That(result, Is.EqualTo(NxGraph.Result.Success));
            return board.Get(Result);
        }
    }

    /// <summary>An arm that records which way the branch went, and serializes with zero options.</summary>
    private static BehaviorState Arm(BlackboardKey<int> result, int marker) =>
        new(new SetValue<int>(result, marker));

    // ── Choice round trips (zero options) ────────────────────────────────

    [Test]
    public async Task Choice_graph_roundtrips_and_decides_the_same_way([Values] bool binary)
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(ConditionMatch.All, new IsTrue(gate.Open), new KeyEquals<int>(gate.Level, 3))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);
        IChoiceNode choice = ChoiceAt(rebuilt, 0);

        Assert.Multiple(() =>
        {
            Assert.That(choice, Is.Not.Null.And.InstanceOf<ChoiceState>());
            Assert.That(choice.Match, Is.EqualTo(ConditionMatch.All));
            Assert.That(choice.Conditions, Has.Count.EqualTo(2));
            Assert.That(choice.Conditions[0], Is.InstanceOf<IsTrue>());
            Assert.That(choice.Conditions[1], Is.InstanceOf<KeyEquals<int>>());
            Assert.That(choice.TrueTarget.Index, Is.EqualTo(1), "The true arm's pad index rides as structure.");
            Assert.That(choice.FalseTarget.Index, Is.EqualTo(2));
        });

        Assert.Multiple(async () =>
        {
            // All: both conditions must hold.
            Assert.That(await gate.Decide(rebuilt, open: true, level: 3), Is.EqualTo(1));
            Assert.That(await gate.Decide(rebuilt, open: true, level: 4), Is.EqualTo(2));
            Assert.That(await gate.Decide(rebuilt, open: false, level: 3), Is.EqualTo(2));
            // The pre-trip graph agrees, arm for arm.
            Assert.That(await gate.Decide(graph, open: true, level: 3), Is.EqualTo(1));
            Assert.That(await gate.Decide(graph, open: false, level: 3), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Any_match_roundtrips_and_decides_the_same_way([Values] bool binary)
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(ConditionMatch.Any, new IsTrue(gate.Open), new KeyEquals<int>(gate.Level, 3))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);

        Assert.That(ChoiceAt(rebuilt, 0).Match, Is.EqualTo(ConditionMatch.Any));
        Assert.Multiple(async () =>
        {
            Assert.That(await gate.Decide(rebuilt, open: true, level: 9), Is.EqualTo(1));
            Assert.That(await gate.Decide(rebuilt, open: false, level: 3), Is.EqualTo(1));
            Assert.That(await gate.Decide(rebuilt, open: false, level: 9), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Nested_not_roundtrips([Values] bool binary)
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new Not(new IsTrue(gate.Open)))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);
        IChoiceNode choice = ChoiceAt(rebuilt, 0);

        Assert.Multiple(() =>
        {
            Assert.That(choice.Conditions, Has.Count.EqualTo(1));
            Not not = (Not)choice.Conditions[0];
            Assert.That(not.Inner, Is.InstanceOf<IsTrue>(),
                "The nested condition rides through the field model's Conditions slot.");
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await gate.Decide(rebuilt, open: false, level: 0), Is.EqualTo(1));
            Assert.That(await gate.Decide(rebuilt, open: true, level: 0), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Payload_carries_markers_sections_and_current_version_stamp()
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new KeyEquals<int>(gate.Level, 3))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        string json = await ToJson(new GraphSerializer(new DummyCodec()), graph);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain($"\"version\": {SerializationVersion.Version}"));
            Assert.That(json, Does.Contain("\"ChoiceState\""));
            Assert.That(json, Does.Contain("\"choices\""));
            Assert.That(json, Does.Contain("\"switches\""));
            // The JSON writer escapes the generic-arity backtick as ` (default encoder);
            // unescape it before the ordinal containment check.
            Assert.That(json.Replace("\\u0060", "`")
                    .Contains("NxGraph.Conditions.KeyEquals`1[System.Int32]", StringComparison.Ordinal),
                Is.True, "KeyEquals rides under its runtime-stable closed-generic name.");
        });
    }

    // ── KeyEquals rebinding on a deserialized graph ──────────────────────

    [Test]
    public async Task Key_equals_rebinds_by_name_and_decides_on_the_rebuilt_graph([Values] bool binary)
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new KeyEquals<int>(gate.Level, 3))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);
        KeyEquals<int> condition = (KeyEquals<int>)ChoiceAt(rebuilt, 0).Conditions[0];

        Assert.Multiple(() =>
        {
            Assert.That(condition.KeyName, Is.EqualTo("level"), "The key rides by name only.");
            Assert.That(condition.Key.IsValid, Is.False, "Deserialized conditions are name-bound.");
            Assert.That(condition.Expected.IsBound, Is.False);
            Assert.That(condition.Expected.Literal, Is.EqualTo(3));
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await gate.Decide(rebuilt, open: false, level: 3), Is.EqualTo(1));
            Assert.That(await gate.Decide(rebuilt, open: false, level: 4), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Key_equals_expected_side_may_be_another_key([Values] bool binary)
    {
        Gate gate = new();
        BlackboardKey<int> expected = gate.Schema.Register("expected", 0);
        Graph graph = GraphBuilder.Start()
            .If(new KeyEquals<int>(gate.Level, expected))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);
        KeyEquals<int> condition = (KeyEquals<int>)ChoiceAt(rebuilt, 0).Conditions[0];

        Assert.Multiple(() =>
        {
            Assert.That(condition.Expected.IsBound, Is.True);
            Assert.That(condition.Expected.KeyName, Is.EqualTo("expected"));
        });

        Blackboard board = new(gate.Schema);
        board.Set(gate.Level, 7);
        board.Set(expected, 7);
        Result result = await rebuilt.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(gate.Result), Is.EqualTo(1), "Key-against-key comparison survived the trip.");
        });
    }

    [Test]
    public async Task Rebind_against_a_schema_missing_the_key_throws_targeted()
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new KeyEquals<int>(gate.Level, 3))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary: false);

        BlackboardSchema other = new("other");
        other.Register<int>("differentName");
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'level'").And.Contain("does not exist"));
    }

    [Test]
    public async Task Rebind_against_a_mismatched_value_type_throws_targeted()
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new KeyEquals<int>(gate.Level, 3))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary: false);

        BlackboardSchema other = new("other");
        other.Register<string>("level"); // same name, different value type
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'level'").And.Contain("declared as"));
    }

    // ── Switch round trips ───────────────────────────────────────────────

    private sealed class Router
    {
        public BlackboardSchema Schema { get; } = new("router");
        public BlackboardKey<string> Mode { get; }
        public BlackboardKey<int> Result { get; }

        public Router()
        {
            Mode = Schema.Register("mode", "a");
            Result = Schema.Register("result", 0);
        }

        public Graph Build() =>
            GraphBuilder.Start()
                .Switch(Mode)
                .Case("a", Arm(Result, 1))
                .Case("b", Arm(Result, 2))
                .Default(Arm(Result, 9))
                .End()
                .SetName("mode-switch")
                .WithSchema(Schema)
                .Build();

        public async Task<int> Route(Graph graph, string mode)
        {
            Blackboard board = new(Schema);
            board.Set(Mode, mode);
            Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();
            Assert.That(result, Is.EqualTo(NxGraph.Result.Success));
            return board.Get(Result);
        }
    }

    [Test]
    public async Task Switch_graph_roundtrips_with_its_cases_and_default([Values] bool binary)
    {
        Router router = new();
        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), router.Build(), binary);
        ISwitchNode switchNode = SwitchAt(rebuilt, 0);

        Assert.Multiple(() =>
        {
            Assert.That(switchNode, Is.Not.Null.And.InstanceOf<SwitchState<string>>());
            Assert.That(switchNode.KeyName, Is.EqualTo("mode"));
            Assert.That(switchNode.ValueType, Is.EqualTo(typeof(string)));
            Assert.That(switchNode.CaseCount, Is.EqualTo(2));
            Assert.That(switchNode.CaseValueAt(0), Is.EqualTo("a"));
            Assert.That(switchNode.CaseTargetAt(0).Index, Is.EqualTo(1));
            Assert.That(switchNode.CaseValueAt(1), Is.EqualTo("b"));
            Assert.That(switchNode.CaseTargetAt(1).Index, Is.EqualTo(2));
            Assert.That(switchNode.DefaultTarget.Index, Is.EqualTo(3));
            Assert.That(((SwitchState<string>)switchNode).Key.IsValid, Is.False,
                "Deserialized switches are name-bound.");
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await router.Route(rebuilt, "a"), Is.EqualTo(1));
            Assert.That(await router.Route(rebuilt, "b"), Is.EqualTo(2));
            Assert.That(await router.Route(rebuilt, "zzz"), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Switch_rebind_against_a_schema_missing_the_key_throws_targeted()
    {
        Router router = new();
        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), router.Build(), binary: false);

        BlackboardSchema other = new("other");
        other.Register("differentName", "a");
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'mode'").And.Contain("does not exist"));
    }

    [Test]
    public async Task Switch_rebind_against_a_mismatched_value_type_throws_targeted()
    {
        Router router = new();
        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), router.Build(), binary: false);

        BlackboardSchema other = new("other");
        other.Register("mode", 0); // same name, different value type
        AsyncStateMachine machine = rebuilt.ToAsyncStateMachine().WithBlackboard(new Blackboard(other));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("'mode'").And.Contain("declared as"));
    }

    [Test]
    public async Task Enum_cases_roundtrip([Values] bool binary)
    {
        BlackboardSchema schema = new("severity");
        BlackboardKey<LogSeverity> key = schema.Register("severity", LogSeverity.Info);
        BlackboardKey<int> result = schema.Register("result", 0);

        Graph graph = GraphBuilder.Start()
            .Switch(key)
            .Case(LogSeverity.Warning, Arm(result, 1))
            .Default(Arm(result, 9))
            .End()
            .WithSchema(schema)
            .Build();

        Graph rebuilt = await RoundTrip(new GraphSerializer(new DummyCodec()), graph, binary);
        ISwitchNode switchNode = SwitchAt(rebuilt, 0);

        Assert.Multiple(() =>
        {
            Assert.That(switchNode.ValueType, Is.EqualTo(typeof(LogSeverity)));
            Assert.That(switchNode.CaseValueAt(0), Is.EqualTo(LogSeverity.Warning),
                "Enum case literals ride as member names, like every enum in the field model.");
        });
    }

    [Test]
    public void Switch_over_a_type_outside_the_field_model_fails_naming_the_node()
    {
        BlackboardSchema schema = new("ids");
        BlackboardKey<Guid> key = schema.Register("id", Guid.Empty);

        Graph graph = GraphBuilder.Start()
            .Switch(key)
            .Case(Guid.Parse("11111111-1111-1111-1111-111111111111"), new EmptyLogic())
            .Default(new EmptyLogic())
            .End()
            .SetName("id-switch")
            .WithSchema(schema)
            .Build();

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ToJson(new GraphSerializer(new DummyCodec()), graph));
        Assert.That(ex!.Message, Does.Contain("id-switch").And.Contain("outside the behavior field model"));
    }

    // ── Custom conditions ────────────────────────────────────────────────

    [Test]
    public async Task Custom_condition_roundtrips_via_registered_factory([Values] bool binary)
    {
        Gate gate = new();
        ConditionRegistry registry = new();
        registry.Register(typeof(AtLeastThree).FullName!,
            fields => new AtLeastThree(fields.ReadBinding<int>("operand")));

        GraphSerializer serializer = new(new DummyCodec(),
            new GraphSerializerOptions { ConditionRegistry = registry });

        Graph graph = GraphBuilder.Start()
            .If(new AtLeastThree(gate.Level))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        Graph rebuilt = await RoundTrip(serializer, graph, binary);
        AtLeastThree condition = (AtLeastThree)ChoiceAt(rebuilt, 0).Conditions[0];

        Assert.That(condition.Operand.KeyName, Is.EqualTo("level"));
        Assert.Multiple(async () =>
        {
            Assert.That(await gate.Decide(rebuilt, open: false, level: 5), Is.EqualTo(1));
            Assert.That(await gate.Decide(rebuilt, open: false, level: 1), Is.EqualTo(2));
        });
    }

    [Test]
    public void Unserializable_condition_fails_write_naming_the_registry()
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new OpaqueCondition())
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .Build();

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ToJson(new GraphSerializer(new DummyCodec()), graph));
        Assert.That(ex!.Message, Does.Contain("OpaqueCondition").And.Contain("ConditionRegistry"));
    }

    [Test]
    public async Task Unregistered_condition_name_fails_read_naming_the_registry()
    {
        Gate gate = new();
        Graph graph = GraphBuilder.Start()
            .If(new AtLeastThree(gate.Level))
            .Then(Arm(gate.Result, 1))
            .Else(Arm(gate.Result, 2))
            .WithSchema(gate.Schema)
            .Build();

        // AtLeastThree is ISerializableCondition — the write needs no factory; the read does.
        string json = await ToJson(new GraphSerializer(new DummyCodec()), graph);

        NotSupportedException? ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("AtLeastThree").And.Contain("ConditionRegistry"));
    }

    // ── Version stamps, back compatibility, spoof defense ────────────────

    [Test]
    public async Task Version_nine_payload_reads_branch_free()
    {
        string json = """
            {
              "version": 9,
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "noop" } ],
              "transitions": [ { "destination": -1 } ],
              "name": null, "index": -1
            }
            """;

        Graph rebuilt = await FromJson(new GraphSerializer(new DummyCodec()), json);

        Assert.That(((LogicNode)rebuilt.StartNode).AsyncLogic, Is.Not.InstanceOf<IChoiceNode>(),
            "A pre-v10 payload rebuilds as an ordinary graph with no branch surface.");
    }

    [Test]
    public async Task Branch_marker_strings_in_ordinary_logic_are_not_honored()
    {
        // Without a ChoiceDto/SwitchDto claiming the index, the marker string must fall
        // through to the ordinary logic codec.
        Graph graph = GraphBuilder.Start().ToAsync(new EmptyAsyncLogic()).Build();
        GraphSerializer serializer = new(new MarkerEmittingCodec());

        string choiceJson = (await ToJson(serializer, graph))
            .Replace("\"logic\": \"noop\"", "\"logic\": \"ChoiceState\"");
        string switchJson = (await ToJson(serializer, graph))
            .Replace("\"logic\": \"noop\"", "\"logic\": \"SwitchState\"");

        Graph rebuiltChoice = await FromJson(serializer, choiceJson);
        Graph rebuiltSwitch = await FromJson(serializer, switchJson);

        Assert.Multiple(() =>
        {
            Assert.That(((LogicNode)rebuiltChoice.StartNode).AsyncLogic, Is.InstanceOf<EmptyAsyncLogic>());
            Assert.That(((LogicNode)rebuiltSwitch.StartNode).AsyncLogic, Is.InstanceOf<EmptyAsyncLogic>());
        });
    }

    [Test]
    public void Choice_claim_on_a_non_marker_node_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "noop" } ],
              "transitions": [ { "destination": -1 } ],
              "choices": [
                {
                  "ownerIndex": 0, "match": 0, "trueTarget": -1, "falseTarget": -1,
                  "conditions": [ { "conditionTypeName": "NxGraph.Conditions.IsTrue", "fields": [] } ]
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("does not reference a choice marker"));
    }

    [Test]
    public void Cross_section_claim_overlap_with_forks_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [
                { "$type": "txt", "index": 0, "name": "a", "logic": "ChoiceState" },
                { "$type": "txt", "index": 1, "name": "b", "logic": "noop" }
              ],
              "transitions": [ { "destination": -1 }, { "destination": -1 } ],
              "forks": [ { "ownerIndex": 0, "branches": [ 1 ] } ],
              "choices": [
                { "ownerIndex": 0, "match": 0, "trueTarget": 1, "falseTarget": -1, "conditions": [] }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("claimed by both"));
    }

    [Test]
    public void Empty_condition_array_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "ChoiceState" } ],
              "transitions": [ { "destination": -1 } ],
              "choices": [
                { "ownerIndex": 0, "match": 0, "trueTarget": -1, "falseTarget": -1, "conditions": [] }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("at least one condition"));
    }

    [Test]
    public void Empty_case_array_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "SwitchState" } ],
              "transitions": [ { "destination": -1 } ],
              "switches": [
                {
                  "ownerIndex": 0, "keyName": "mode", "valueTypeName": "System.String",
                  "cases": [], "defaultTarget": -1
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("at least one case"));
    }

    [Test]
    public void Unresolvable_switch_value_type_throws_naming_it()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "SwitchState" } ],
              "transitions": [ { "destination": -1 } ],
              "switches": [
                {
                  "ownerIndex": 0, "keyName": "mode", "valueTypeName": "Nowhere.Missing",
                  "cases": [
                    { "targetIndex": -1, "literal": { "kind": 7, "binding": { "literal": { "kind": 0, "text": "a" } } } }
                  ],
                  "defaultTarget": -1
                }
              ],
              "name": null, "index": -1
            }
            """;

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(new GraphSerializer(new DummyCodec()), json));
        Assert.That(ex!.Message, Does.Contain("Nowhere.Missing").And.Contain("cannot be resolved"));
    }
}
