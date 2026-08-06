using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Conditions;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// The data-built <see cref="ChoiceState"/> (spec 023): a condition list, a match mode, two
/// arms. Pins the combination semantics (including short-circuit evaluation, which the
/// side-effect-free <see cref="ICondition"/> contract makes legal), routing of both arms under
/// both runtimes, the terminal <see cref="NodeId.Default"/> arm, and the construction-time
/// rejections. The delegate-backed twin is covered by <c>RelayChoiceStateTests</c>.
/// </summary>
[TestFixture]
[Category("branching_choice")]
public class ChoiceStateTests
{
    private const string TrueArm = "true-arm";
    private const string FalseArm = "false-arm";

    // ── Test doubles ─────────────────────────────────────────────────────

    /// <summary>Returns a fixed answer and counts how often it was asked.</summary>
    private sealed class CountingCondition(bool answer) : ICondition
    {
        public int Evaluations { get; private set; }

        public bool Evaluate(in BehaviorContext ctx)
        {
            Evaluations++;
            return answer;
        }
    }

    /// <summary>Throws if evaluated — the short-circuit tripwire.</summary>
    private sealed class ExplodingCondition : ICondition
    {
        public bool Evaluate(in BehaviorContext ctx) =>
            throw new InvalidOperationException("evaluated past the short-circuit point");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static RelayState Probe(string name, List<string> trace) => new(() =>
    {
        trace.Add(name);
        return Result.Success;
    });

    /// <summary>
    /// A choice as the start node, each arm a probe state. An arm marked terminal is wired to
    /// <see cref="NodeId.Default"/> instead — the director's terminal exit.
    /// </summary>
    private static Graph ChoiceGraph(IReadOnlyList<ICondition> conditions, ConditionMatch match,
        List<string> trace, bool trueArmTerminal = false, bool falseArmTerminal = false)
    {
        GraphBuilder builder = new();
        NodeId yes = trueArmTerminal ? NodeId.Default : builder.AddNode(Probe(TrueArm, trace));
        NodeId no = falseArmTerminal ? NodeId.Default : builder.AddNode(Probe(FalseArm, trace));
        builder.AddNode((IAsyncLogic)new ChoiceState(conditions, match, yes, no), isStart: true);
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

    private static async Task<Result> RunAsync(Graph graph, bool sync)
    {
        return sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();
    }

    // ── Combination semantics ────────────────────────────────────────────

    [Test]
    public async Task All_short_circuits_at_the_first_false([Values] bool sync)
    {
        CountingCondition first = new(false);
        List<string> trace = [];
        Graph graph = ChoiceGraph([first, new ExplodingCondition()], ConditionMatch.All, trace);

        Result result = await RunAsync(graph, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { FalseArm }));
            Assert.That(first.Evaluations, Is.EqualTo(1),
                "All stops walking at the first false — the tripwire condition must never run.");
        });
    }

    [Test]
    public async Task Any_short_circuits_at_the_first_true([Values] bool sync)
    {
        CountingCondition first = new(true);
        List<string> trace = [];
        Graph graph = ChoiceGraph([first, new ExplodingCondition()], ConditionMatch.Any, trace);

        Result result = await RunAsync(graph, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { TrueArm }));
            Assert.That(first.Evaluations, Is.EqualTo(1),
                "Any stops walking at the first true — the tripwire condition must never run.");
        });
    }

    [Test]
    public async Task All_takes_the_true_arm_only_when_every_condition_holds([Values] bool sync)
    {
        CountingCondition a = new(true);
        CountingCondition b = new(true);
        List<string> trace = [];

        Result result = await RunAsync(ChoiceGraph([a, b], ConditionMatch.All, trace), sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { TrueArm }));
            Assert.That(a.Evaluations, Is.EqualTo(1));
            Assert.That(b.Evaluations, Is.EqualTo(1), "All walks the whole list when nothing is false.");
        });
    }

    [Test]
    public async Task Any_takes_the_false_arm_only_when_every_condition_fails([Values] bool sync)
    {
        CountingCondition a = new(false);
        CountingCondition b = new(false);
        List<string> trace = [];

        Result result = await RunAsync(ChoiceGraph([a, b], ConditionMatch.Any, trace), sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { FalseArm }));
            Assert.That(b.Evaluations, Is.EqualTo(1), "Any walks the whole list when nothing is true.");
        });
    }

    // ── Arm routing ──────────────────────────────────────────────────────

    [Test]
    public async Task Both_arms_route_to_their_own_node([Values] bool sync)
    {
        List<string> trueTrace = [];
        List<string> falseTrace = [];

        Result trueRun = await RunAsync(ChoiceGraph([new IsTrue(true)], ConditionMatch.All, trueTrace), sync);
        Result falseRun = await RunAsync(ChoiceGraph([new IsTrue(false)], ConditionMatch.All, falseTrace), sync);

        Assert.Multiple(() =>
        {
            Assert.That(trueRun, Is.EqualTo(Result.Success));
            Assert.That(falseRun, Is.EqualTo(Result.Success));
            Assert.That(trueTrace, Is.EqualTo(new[] { TrueArm }));
            Assert.That(falseTrace, Is.EqualTo(new[] { FalseArm }));
        });
    }

    [Test]
    public async Task A_default_true_arm_terminates_the_run([Values] bool sync)
    {
        List<string> trace = [];
        Graph graph = ChoiceGraph([new IsTrue(true)], ConditionMatch.All, trace, trueArmTerminal: true);

        Result result = await RunAsync(graph, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success), "NodeId.Default is the director's terminal exit.");
            Assert.That(trace, Is.Empty, "The false arm's probe must not run.");
        });
    }

    [Test]
    public async Task A_default_false_arm_terminates_the_run([Values] bool sync)
    {
        List<string> trace = [];
        Graph graph = ChoiceGraph([new IsTrue(false)], ConditionMatch.All, trace, falseArmTerminal: true);

        Result result = await RunAsync(graph, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.Empty, "The true arm's probe must not run.");
        });
    }

    // ── Node surface ─────────────────────────────────────────────────────

    [Test]
    public void The_single_condition_constructor_is_an_All_of_one()
    {
        ChoiceState choice = new(new IsTrue(true), new NodeId(1), new NodeId(2));

        Assert.Multiple(() =>
        {
            Assert.That(choice.Match, Is.EqualTo(ConditionMatch.All));
            Assert.That(choice.Conditions, Has.Count.EqualTo(1));
            Assert.That(choice.SelectNext(), Is.EqualTo(new NodeId(1)));
        });
    }

    [Test]
    public void Execute_always_succeeds_because_a_decision_never_faults()
    {
        ChoiceState choice = new(new IsTrue(false), new NodeId(1), new NodeId(2));

        Assert.That(((ILogic)choice).Execute(), Is.EqualTo(Result.Success));
    }

    [Test]
    public void Static_targets_yield_the_true_arm_then_the_false_arm()
    {
        // Reachability validation and the Mermaid exporter walk this — order is the contract.
        ChoiceState choice = new([new IsTrue(true)], ConditionMatch.All, new NodeId(7), new NodeId(9));

        Assert.That(((IDirector)choice).EnumerateStaticTargets().ToArray(),
            Is.EqualTo(new[] { new NodeId(7), new NodeId(9) }));
    }

    [Test]
    public void The_condition_list_is_copied_so_a_later_mutation_cannot_change_the_decision()
    {
        List<ICondition> conditions = [new IsTrue(true)];
        ChoiceState choice = new(conditions, ConditionMatch.All, new NodeId(1), new NodeId(2));

        conditions[0] = new IsTrue(false);
        conditions.Add(new IsTrue(false));

        Assert.That(choice.SelectNext(), Is.EqualTo(new NodeId(1)),
            "The built graph's decision must not be reachable through the caller's list.");
    }

    // ── Construction-time rejections ─────────────────────────────────────

    [Test]
    public void An_empty_condition_list_is_rejected()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new ChoiceState([], ConditionMatch.All, new NodeId(1), new NodeId(2)));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("conditions"));
            Assert.That(ex.Message, Does.Contain("At least one condition"));
        });
    }

    [Test]
    public void A_null_condition_list_is_rejected()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new ChoiceState(null!, ConditionMatch.All, new NodeId(1), new NodeId(2)));

        Assert.That(ex!.ParamName, Is.EqualTo("conditions"));
    }

    [Test]
    public void A_null_condition_entry_is_rejected_naming_its_index()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new ChoiceState([new IsTrue(true), null!], ConditionMatch.All,
                new NodeId(1), new NodeId(2)));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("conditions"));
            Assert.That(ex.Message, Does.Contain("index 1"));
        });
    }

    [Test]
    public void A_null_single_condition_is_rejected_through_the_same_parameter_name()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new ChoiceState((ICondition)null!, new NodeId(1), new NodeId(2)));

        Assert.That(ex!.ParamName, Is.EqualTo("conditions"));
    }
}
