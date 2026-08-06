using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Conditions;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Condition semantics (spec 023): the standard <see cref="ICondition"/> set evaluated against
/// a machine-stamped context. <c>BehaviorContext</c>'s constructor is internal, so every case
/// drives a real run over a one-node <see cref="ChoiceState"/> graph and reads back which arm
/// the decision took — the same way the library will be used.
/// <para>
/// The load-bearing contract pinned here: a condition that is <b>false</b> is not a fault (the
/// run still ends <c>Success</c>, down the false arm), while a genuine wiring fault — a
/// name-bound key missing from every bound schema, or declared with a different value type —
/// <b>throws</b> and is never reported as false.
/// </para>
/// </summary>
[TestFixture]
[Category("conditions")]
public class ConditionTests
{
    private const string TrueArm = "true-arm";
    private const string FalseArm = "false-arm";

    // ── Fixtures ─────────────────────────────────────────────────────────

    /// <summary>
    /// A three-node graph: the start node is a data-built choice whose arms are two probe
    /// states appending their name to <paramref name="trace"/>.
    /// </summary>
    private static Graph ChoiceGraph(IReadOnlyList<ICondition> conditions, ConditionMatch match,
        List<string> trace, BlackboardSchema? schema)
    {
        GraphBuilder builder = new();
        NodeId yes = builder.AddNode(new RelayState(() =>
        {
            trace.Add(TrueArm);
            return Result.Success;
        }));
        NodeId no = builder.AddNode(new RelayState(() =>
        {
            trace.Add(FalseArm);
            return Result.Success;
        }));
        builder.AddNode((IAsyncLogic)new ChoiceState(conditions, match, yes, no), isStart: true);

        if (schema is not null)
        {
            builder.WithSchema(schema);
        }

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

    /// <summary>Runs one condition through both runtimes' shared graph shape and returns the arm taken.</summary>
    private static async Task<string> ArmAsync(ICondition condition, bool sync,
        BlackboardSchema? schema = null, Blackboard? board = null)
    {
        List<string> trace = [];
        Graph graph = ChoiceGraph([condition], ConditionMatch.All, trace, schema);

        Result result;
        if (sync)
        {
            StateMachine machine = graph.ToStateMachine();
            if (board is not null)
            {
                machine = machine.WithBlackboard(board);
            }

            result = RunToEnd(machine);
        }
        else
        {
            AsyncStateMachine machine = graph.ToAsyncStateMachine();
            if (board is not null)
            {
                machine = machine.WithBlackboard(board);
            }

            result = await machine.ExecuteAsync();
        }

        Assert.That(result, Is.EqualTo(Result.Success),
            "A decision never faults — a false condition routes, it does not fail the node.");
        return trace.Single();
    }

    private static (BlackboardSchema schema, Blackboard board) Boards(out BlackboardKey<string> mode,
        out BlackboardKey<string> expected, out BlackboardKey<bool> armed)
    {
        BlackboardSchema schema = new("conditions");
        mode = schema.Register("mode", "patrol");
        expected = schema.Register("expected", "patrol");
        armed = schema.Register("armed", false);
        return (schema, new Blackboard(schema));
    }

    // ── KeyEquals ────────────────────────────────────────────────────────

    [Test]
    public async Task KeyEquals_takes_the_true_arm_when_the_slot_matches_the_literal([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board) = Boards(out BlackboardKey<string> mode, out _, out _);
        board.Set(mode, "chase");

        string arm = await ArmAsync(new KeyEquals<string>(mode, "chase"), sync, schema, board);

        Assert.That(arm, Is.EqualTo(TrueArm));
    }

    [Test]
    public async Task KeyEquals_takes_the_false_arm_when_the_slot_differs([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board) = Boards(out BlackboardKey<string> mode, out _, out _);
        board.Set(mode, "patrol");

        string arm = await ArmAsync(new KeyEquals<string>(mode, "chase"), sync, schema, board);

        Assert.That(arm, Is.EqualTo(FalseArm));
    }

    [Test]
    public async Task KeyEquals_compares_a_key_against_another_key([Values] bool sync)
    {
        // The expected side is a BlackboardValue<T> binding, so a rule may compare two slots.
        (BlackboardSchema schema, Blackboard board) =
            Boards(out BlackboardKey<string> mode, out BlackboardKey<string> expected, out _);
        board.Set(mode, "chase");
        board.Set(expected, "chase");

        string equalArm = await ArmAsync(new KeyEquals<string>(mode, expected), sync, schema, board);

        board.Set(expected, "flee");
        string differingArm = await ArmAsync(new KeyEquals<string>(mode, expected), sync, schema, board);

        Assert.Multiple(() =>
        {
            Assert.That(equalArm, Is.EqualTo(TrueArm));
            Assert.That(differingArm, Is.EqualTo(FalseArm));
        });
    }

    [Test]
    public void KeyEquals_rejects_an_invalid_key()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new KeyEquals<int>(default, 1));

        Assert.That(ex!.ParamName, Is.EqualTo("key"));
    }

    [Test]
    public void KeyEquals_unbound_rejects_an_empty_key_name()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = KeyEquals<int>.Unbound(string.Empty, 1));

        Assert.That(ex!.ParamName, Is.EqualTo("keyName"));
    }

    // ── IsTrue ───────────────────────────────────────────────────────────

    [Test]
    public async Task IsTrue_reads_a_literal_without_touching_any_board([Values] bool sync)
    {
        // No schema, no bound board: a literal binding resolves without blackboard access.
        string trueArm = await ArmAsync(new IsTrue(true), sync);
        string falseArm = await ArmAsync(new IsTrue(false), sync);

        Assert.Multiple(() =>
        {
            Assert.That(trueArm, Is.EqualTo(TrueArm));
            Assert.That(falseArm, Is.EqualTo(FalseArm));
        });
    }

    [Test]
    public async Task IsTrue_reads_a_bool_key([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board) = Boards(out _, out _, out BlackboardKey<bool> armed);
        board.Set(armed, true);

        string arm = await ArmAsync(new IsTrue(armed), sync, schema, board);

        Assert.That(arm, Is.EqualTo(TrueArm));
    }

    // ── Not ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Not_inverts_the_inner_condition([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board) = Boards(out BlackboardKey<string> mode, out _, out _);
        board.Set(mode, "patrol");

        string arm = await ArmAsync(new Not(new KeyEquals<string>(mode, "chase")), sync, schema, board);

        Assert.That(arm, Is.EqualTo(TrueArm), "'not equal' is expressible only through Not.");
    }

    [Test]
    public async Task Not_nests_over_another_negation([Values] bool sync)
    {
        string arm = await ArmAsync(new Not(new Not(new IsTrue(true))), sync);

        Assert.That(arm, Is.EqualTo(TrueArm));
    }

    [Test]
    public void Not_rejects_a_null_inner_condition()
    {
        ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(() => _ = new Not(null!));

        Assert.That(ex!.ParamName, Is.EqualTo("condition"));
    }

    // ── Name-bound (deserialized) keys ───────────────────────────────────

    [Test]
    public async Task Unbound_key_resolves_by_name_against_the_bound_schema([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board) = Boards(out BlackboardKey<string> mode, out _, out _);
        board.Set(mode, "chase");

        string arm = await ArmAsync(KeyEquals<string>.Unbound("mode", "chase"), sync, schema, board);

        Assert.That(arm, Is.EqualTo(TrueArm),
            "A rebuilt condition resolves its key by name against the machine's bound boards.");
    }

    [Test]
    public void Unbound_key_missing_from_every_bound_schema_throws_instead_of_reporting_false([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board) = Boards(out _, out _, out _);
        List<string> trace = [];
        Graph graph = ChoiceGraph([KeyEquals<string>.Unbound("ghost", "chase")], ConditionMatch.All, trace, schema);

        InvalidOperationException? ex = sync
            ? Assert.Throws<InvalidOperationException>(
                () => RunToEnd(graph.ToStateMachine().WithBlackboard(board)))
            : Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync());

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("ghost"));
            Assert.That(trace, Is.Empty, "A wiring fault throws — it must never be reported as a false arm.");
        });
    }

    [Test]
    public void Unbound_key_declared_with_a_different_value_type_throws([Values] bool sync)
    {
        BlackboardSchema schema = new("mismatched");
        BlackboardKey<int> tier = schema.Register("mode", 2);
        Blackboard board = new(schema);
        board.Set(tier, 2);

        List<string> trace = [];
        Graph graph = ChoiceGraph([KeyEquals<string>.Unbound("mode", "chase")], ConditionMatch.All, trace, schema);

        InvalidOperationException? ex = sync
            ? Assert.Throws<InvalidOperationException>(
                () => RunToEnd(graph.ToStateMachine().WithBlackboard(board)))
            : Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync());

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("mode").And.Contain("System.Int32"));
            Assert.That(trace, Is.Empty, "A type mismatch throws — it must never be reported as a false arm.");
        });
    }

    // ── Scope reach ──────────────────────────────────────────────────────

    [Test]
    public async Task Conditions_read_node_scoped_scratch_defaults([Values] bool sync)
    {
        // Unlike ports (spec 010), conditions accept any key scope — they resolve within one
        // visit. A machine auto-creates its Node board, so no binding is involved.
        BlackboardSchema scratch = new("scratch", BlackboardScope.Node);
        BlackboardKey<int> tier = scratch.Register("tier", 2);

        string arm = await ArmAsync(new KeyEquals<int>(tier, 2), sync, scratch);

        Assert.That(arm, Is.EqualTo(TrueArm));
    }
}
