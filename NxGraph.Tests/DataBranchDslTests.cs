using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Conditions;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// The data-built authoring surface (spec 023, <c>Authoring/Dsl.Conditions.cs</c>):
/// <c>.If(condition)</c>, <c>.If(match, conditions…)</c> and <c>.Switch(blackboardKey)</c> on
/// both <c>StartToken</c> (the branch as the graph's first node) and <c>StateToken</c>.
/// <c>.If(...)</c> returns the same <c>IfBuilder</c> the delegate path returns; <c>.Switch(key)</c>
/// returns <c>KeySwitchBuilder</c>, the data-built twin of <c>SwitchBuilder</c> that mirrors its
/// authoring surface. These tests pin that the chain shape is unchanged and that the data path
/// really builds the serializable states rather than a <c>Relay*</c> one.
/// </summary>
[TestFixture]
[Category("branching_dsl")]
public class DataBranchDslTests
{
    private static RelayState Probe(string name, List<string> trace) => new(() =>
    {
        trace.Add(name);
        return Result.Success;
    });

    private static Result Mark(string name, List<string> trace)
    {
        trace.Add(name);
        return Result.Success;
    }

    private static ValueTask<Result> MarkAsync(string name, List<string> trace)
    {
        trace.Add(name);
        return ResultHelpers.Success;
    }

    private static (BlackboardSchema schema, Blackboard board, BlackboardKey<bool> armed,
        BlackboardKey<string> mode) Boards()
    {
        BlackboardSchema schema = new("dsl-branching");
        BlackboardKey<bool> armed = schema.Register("armed", false);
        BlackboardKey<string> mode = schema.Register("mode", "alpha");
        return (schema, new Blackboard(schema), armed, mode);
    }

    private static Result RunToEnd(StateMachine machine)
    {
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    private static async Task<Result> RunAsync(Graph graph, Blackboard board, bool sync)
    {
        return sync
            ? RunToEnd(graph.ToStateMachine().WithBlackboard(board))
            : await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();
    }

    // ── .If(condition) ───────────────────────────────────────────────────

    [Test]
    public async Task If_condition_chains_from_a_state_token([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<bool> armed, _) = Boards();
        List<string> trace = [];

        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .If(new IsTrue(armed))
            .Then(Probe("then", trace))
            .Else(Probe("else", trace))
            .WithSchema(schema)
            .Build();

        board.Set(armed, true);
        Result thenRun = await RunAsync(graph, board, sync);
        board.Set(armed, false);
        Result elseRun = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(thenRun, Is.EqualTo(Result.Success));
            Assert.That(elseRun, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "then", "else" }));
        });
    }

    [Test]
    public async Task If_condition_starts_the_graph([Values] bool sync)
    {
        // The branch is the start node: it must run under both runtimes, exactly like the
        // delegate overloads (one class implements both logic slots and both director slots).
        (BlackboardSchema schema, Blackboard board, BlackboardKey<bool> armed, _) = Boards();
        board.Set(armed, true);
        List<string> trace = [];

        Graph graph = GraphBuilder.Start()
            .If(new IsTrue(armed))
            .Then(Probe("then", trace))
            .Else(Probe("else", trace))
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "then" }));
            Assert.That(graph.StartNode.Id.Index, Is.Zero);
        });
    }

    [Test]
    public async Task If_with_match_any_takes_the_true_arm_when_one_condition_holds([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<bool> armed, _) = Boards();
        board.Set(armed, true);
        List<string> trace = [];

        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .If(ConditionMatch.Any, new IsTrue(false), new IsTrue(armed))
            .Then(Probe("then", trace))
            .Else(Probe("else", trace))
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "then" }));
        });
    }

    [Test]
    public async Task If_with_match_all_takes_the_false_arm_when_one_condition_fails([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<bool> armed, _) = Boards();
        board.Set(armed, true);
        List<string> trace = [];

        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .If(ConditionMatch.All, new IsTrue(armed), new Not(new IsTrue(armed)))
            .Then(Probe("then", trace))
            .Else(Probe("else", trace))
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "else" }));
        });
    }

    [Test]
    public void If_condition_builds_a_data_choice_state_not_a_relay()
    {
        (BlackboardSchema schema, _, BlackboardKey<bool> armed, _) = Boards();

        Graph graph = GraphBuilder.Start()
            .If(new IsTrue(armed))
            .Then(new EmptyLogic())
            .Else(new EmptyLogic())
            .WithSchema(schema)
            .Build();

        LogicNode start = (LogicNode)graph.GetNodeByIndex(0);

        Assert.Multiple(() =>
        {
            Assert.That(start.AsyncLogic, Is.InstanceOf<ChoiceState>(),
                "The data path must build the serializable state, not a RelayChoiceState.");
            Assert.That(ReferenceEquals(start.Logic, start.AsyncLogic), Is.True,
                "One instance fills both logic slots, so either runtime family can run it.");
        });
    }

    // ── .Switch(key) ─────────────────────────────────────────────────────

    [Test]
    public async Task Switch_key_chains_from_a_state_token([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<string> mode) = Boards();
        List<string> trace = [];

        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .Switch(mode)
            .Case("alpha", Probe("case:alpha", trace))
            .Case("beta", Probe("case:beta", trace))
            .Default(Probe("default", trace))
            .End()
            .WithSchema(schema)
            .Build();

        board.Set(mode, "beta");
        Result matched = await RunAsync(graph, board, sync);
        board.Set(mode, "omega");
        Result unmatched = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(matched, Is.EqualTo(Result.Success));
            Assert.That(unmatched, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "case:beta", "default" }));
        });
    }

    [Test]
    public async Task Switch_key_starts_the_graph([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "alpha");
        List<string> trace = [];

        Graph graph = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", Probe("case:alpha", trace))
            .Default(Probe("default", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "case:alpha" }));
            Assert.That(graph.StartNode.Id.Index, Is.Zero);
        });
    }

    [Test]
    public void Switch_key_builds_a_data_switch_state_not_a_relay()
    {
        (BlackboardSchema schema, _, _, BlackboardKey<string> mode) = Boards();

        Graph graph = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", new EmptyLogic())
            .Default(new EmptyLogic())
            .End()
            .WithSchema(schema)
            .Build();

        LogicNode start = (LogicNode)graph.GetNodeByIndex(0);

        Assert.Multiple(() =>
        {
            Assert.That(start.AsyncLogic, Is.InstanceOf<SwitchState<string>>());
            Assert.That(ReferenceEquals(start.Logic, start.AsyncLogic), Is.True);
        });
    }

    [Test]
    public void Switch_data_mode_rejects_a_value_cased_twice_at_End()
    {
        (_, _, _, BlackboardKey<string> mode) = Boards();

        ArgumentException? ex = Assert.Throws<ArgumentException>(() => _ = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", new EmptyLogic())
            .Case("alpha", new EmptyLogic())
            .End());

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("cases"));
            Assert.That(ex.Message, Does.Contain("alpha"));
        });
    }

    [Test]
    public void Switch_data_mode_rejects_an_invalid_key()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = GraphBuilder.Start().Switch(default(BlackboardKey<string>)));

        Assert.That(ex!.ParamName, Is.EqualTo("key"));
    }

    [Test]
    public async Task Switch_key_mirrors_the_selector_builders_lambda_surface()
    {
        // KeySwitchBuilder must offer every convenience overload SwitchBuilder offers, so that
        // swapping a selector for a key changes the .Switch(...) call and nothing else. One graph
        // per lambda flavour — plain sync, plain async, context sync, context async — makes this a
        // compile-time completeness proof as well as a routing check.
        //
        // Each graph runs on the runtime its own arms can run on: the async-lambda flavours build
        // AsyncRelayState arms, which the sync StateMachine rejects at construction by design (it
        // names the first node lacking ILogic). That is the pre-existing rule for every async
        // lambda in the DSL, not something the data switch changes — so this test is not
        // parameterised over the runtime; the branch node itself is pinned on both runtimes by the
        // sibling tests and the parity conformance matrix.
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<string> mode) = Boards();
        List<string> trace = [];

        Graph plainSync = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", () => Mark("sync:alpha", trace))
            .Default(() => Mark("sync:default", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Graph plainAsync = GraphBuilder.Start()
            .Switch(mode)
            .CaseAsync("alpha", _ => MarkAsync("async:alpha", trace))
            .DefaultAsync(_ => MarkAsync("async:default", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Graph contextSync = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", bb => Mark($"bb:{bb.Get(mode)}", trace))
            .Default(bb => Mark($"bb:default:{bb.Get(mode)}", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Graph contextAsync = GraphBuilder.Start()
            .Switch(mode)
            .CaseAsync("alpha", (bb, _) => MarkAsync($"bbAsync:{bb.Get(mode)}", trace))
            .DefaultAsync((bb, _) => MarkAsync($"bbAsync:default:{bb.Get(mode)}", trace))
            .End()
            .WithSchema(schema)
            .Build();

        // (graph, runs on the sync machine)
        (Graph Graph, bool Sync)[] graphs =
        [
            (plainSync, true), (plainAsync, false), (contextSync, true), (contextAsync, false),
        ];

        board.Set(mode, "alpha");
        foreach ((Graph graph, bool runSync) in graphs)
        {
            Assert.That(await RunAsync(graph, board, runSync), Is.EqualTo(Result.Success));
        }

        board.Set(mode, "omega");
        foreach ((Graph graph, bool runSync) in graphs)
        {
            Assert.That(await RunAsync(graph, board, runSync), Is.EqualTo(Result.Success));
        }

        Assert.That(trace, Is.EqualTo(new[]
        {
            "sync:alpha", "async:alpha", "bb:alpha", "bbAsync:alpha",
            "sync:default", "async:default", "bb:default:omega", "bbAsync:default:omega",
        }));
    }
}
