using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// The data-built <see cref="SwitchState{T}"/> (spec 023): one tested blackboard key, literal
/// cases, at most one match. Pins case routing under both runtimes, the construction-time
/// distinctness guard (a switch is a lookup — ordered, first-match-wins rules are a chain of
/// choices), and the name-bound rebind form. The delegate-backed twin is covered by
/// <c>RelaySwitchStateTests</c>; the default arm has its own fixture in
/// <c>SwitchDefaultCaseTests</c>.
/// </summary>
[TestFixture]
[Category("branching_switch")]
public class SwitchStateTests
{
    private enum Mode
    {
        Patrol,
        Chase,
        Flee,
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static RelayState Probe(string name, List<string> trace) => new(() =>
    {
        trace.Add(name);
        return Result.Success;
    });

    private static (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) Boards()
    {
        BlackboardSchema schema = new("switching");
        BlackboardKey<string> mode = schema.Register("mode", "alpha");
        return (schema, new Blackboard(schema), mode);
    }

    /// <summary>
    /// A switch as the start node with one probe per case value, and either a probe default
    /// arm or the terminal <see cref="NodeId.Default"/>.
    /// </summary>
    private static Graph SwitchGraph(BlackboardSchema schema, BlackboardKey<string> key, List<string> trace,
        bool withDefault, params string[] caseValues)
    {
        GraphBuilder builder = new();
        SwitchCase<string>[] cases = new SwitchCase<string>[caseValues.Length];
        for (int i = 0; i < caseValues.Length; i++)
        {
            cases[i] = new SwitchCase<string>(caseValues[i], builder.AddNode(Probe($"case:{caseValues[i]}", trace)));
        }

        NodeId defaultTarget = withDefault ? builder.AddNode(Probe("default", trace)) : NodeId.Default;
        builder.AddNode((IAsyncLogic)new SwitchState<string>(key, cases, defaultTarget), isStart: true);
        builder.WithSchema(schema);
        return builder.Build(throwOnError: false);
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

    // ── Routing ──────────────────────────────────────────────────────────

    [Test]
    public async Task Switch_routes_to_the_matching_case([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "beta");
        List<string> trace = [];
        Graph graph = SwitchGraph(schema, mode, trace, withDefault: true, "alpha", "beta", "gamma");

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "case:beta" }));
        });
    }

    [Test]
    public async Task Switch_routes_to_the_default_when_no_case_matches([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "omega");
        List<string> trace = [];
        Graph graph = SwitchGraph(schema, mode, trace, withDefault: true, "alpha", "beta");

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "default" }));
        });
    }

    [Test]
    public async Task Switch_reads_the_key_at_selection_time_so_the_same_graph_reroutes([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        List<string> trace = [];
        Graph graph = SwitchGraph(schema, mode, trace, withDefault: true, "alpha", "beta");

        board.Set(mode, "alpha");
        await RunAsync(graph, board, sync);
        board.Set(mode, "beta");
        await RunAsync(graph, board, sync);

        Assert.That(trace, Is.EqualTo(new[] { "case:alpha", "case:beta" }));
    }

    [Test]
    public async Task Switch_routes_enum_keys([Values] bool sync)
    {
        BlackboardSchema schema = new("modes");
        BlackboardKey<Mode> key = schema.Register("mode", Mode.Patrol);
        Blackboard board = new(schema);
        board.Set(key, Mode.Flee);

        List<string> trace = [];
        GraphBuilder builder = new();
        SwitchCase<Mode>[] cases =
        [
            new(Mode.Chase, builder.AddNode(Probe("chase", trace))),
            new(Mode.Flee, builder.AddNode(Probe("flee", trace))),
        ];
        NodeId fallback = builder.AddNode(Probe("default", trace));
        builder.AddNode((IAsyncLogic)new SwitchState<Mode>(key, cases, fallback), isStart: true);
        builder.WithSchema(schema);
        Graph graph = builder.Build(throwOnError: false);

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "flee" }));
        });
    }

    // ── Name-bound (deserialized) form ───────────────────────────────────

    [Test]
    public async Task Unbound_switch_resolves_its_key_by_name_against_the_bound_schema([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "beta");

        List<string> trace = [];
        GraphBuilder builder = new();
        SwitchCase<string>[] cases =
        [
            new("alpha", builder.AddNode(Probe("case:alpha", trace))),
            new("beta", builder.AddNode(Probe("case:beta", trace))),
        ];
        NodeId fallback = builder.AddNode(Probe("default", trace));
        builder.AddNode((IAsyncLogic)SwitchState<string>.Unbound("mode", cases, fallback), isStart: true);
        builder.WithSchema(schema);
        Graph graph = builder.Build(throwOnError: false);

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "case:beta" }));
        });
    }

    [Test]
    public void Unbound_switch_whose_key_name_is_missing_throws([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _) = Boards();

        List<string> trace = [];
        GraphBuilder builder = new();
        SwitchCase<string>[] cases = [new("alpha", builder.AddNode(Probe("case:alpha", trace)))];
        NodeId fallback = builder.AddNode(Probe("default", trace));
        builder.AddNode((IAsyncLogic)SwitchState<string>.Unbound("ghost", cases, fallback), isStart: true);
        builder.WithSchema(schema);
        Graph graph = builder.Build(throwOnError: false);

        InvalidOperationException? ex = sync
            ? Assert.Throws<InvalidOperationException>(
                () => RunToEnd(graph.ToStateMachine().WithBlackboard(board)))
            : Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync());

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("ghost"));
            Assert.That(trace, Is.Empty, "An unresolvable key throws — it never silently falls to the default.");
        });
    }

    // ── Node surface ─────────────────────────────────────────────────────

    [Test]
    public void Execute_always_succeeds_because_a_decision_never_faults()
    {
        (_, _, BlackboardKey<string> mode) = Boards();
        SwitchState<string> switchState = new(mode, [new SwitchCase<string>("alpha", new NodeId(1))], new NodeId(2));

        Assert.That(((ILogic)switchState).Execute(), Is.EqualTo(Result.Success));
    }

    [Test]
    public void Static_targets_yield_the_case_arms_then_the_default()
    {
        (_, _, BlackboardKey<string> mode) = Boards();
        SwitchState<string> switchState = new(mode,
            [new SwitchCase<string>("alpha", new NodeId(1)), new SwitchCase<string>("beta", new NodeId(2))],
            new NodeId(3));

        Assert.That(((IDirector)switchState).EnumerateStaticTargets().ToArray(),
            Is.EqualTo(new[] { new NodeId(1), new NodeId(2), new NodeId(3) }));
    }

    // ── Construction-time rejections ─────────────────────────────────────

    [Test]
    public void A_value_cased_twice_is_rejected_naming_the_offending_value()
    {
        (_, _, BlackboardKey<string> mode) = Boards();

        ArgumentException? ex = Assert.Throws<ArgumentException>(() => _ = new SwitchState<string>(mode,
            [
                new SwitchCase<string>("alpha", new NodeId(1)),
                new SwitchCase<string>("beta", new NodeId(2)),
                new SwitchCase<string>("beta", new NodeId(3)),
            ],
            new NodeId(4)));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("cases"));
            Assert.That(ex.Message, Does.Contain("beta"),
                "The rejection must name the value the author cased twice.");
        });
    }

    [Test]
    public void An_empty_case_list_is_rejected()
    {
        (_, _, BlackboardKey<string> mode) = Boards();

        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new SwitchState<string>(mode, [], new NodeId(1)));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("cases"));
            Assert.That(ex.Message, Does.Contain("at least one case"));
        });
    }

    [Test]
    public void A_null_case_list_is_rejected()
    {
        (_, _, BlackboardKey<string> mode) = Boards();

        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new SwitchState<string>(mode, null!, new NodeId(1)));

        Assert.That(ex!.ParamName, Is.EqualTo("cases"));
    }

    [Test]
    public void An_invalid_key_is_rejected()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => _ = new SwitchState<string>(
            default, [new SwitchCase<string>("alpha", new NodeId(1))], new NodeId(2)));

        Assert.That(ex!.ParamName, Is.EqualTo("key"));
    }

    [Test]
    public void Unbound_rejects_an_empty_key_name()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => _ = SwitchState<string>.Unbound(
            string.Empty, [new SwitchCase<string>("alpha", new NodeId(1))], new NodeId(2)));

        Assert.That(ex!.ParamName, Is.EqualTo("keyName"));
    }
}
