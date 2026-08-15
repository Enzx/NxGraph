using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Hierarchical outcomes: a composite wrapping a child machine publishes the child's terminal
/// outcome — code and name — onto declared blackboard keys through the stamped parent-level
/// context, the moment the child run reaches a terminal, so a parent branches on <i>which</i>
/// outcome a sub-graph ended with using an ordinary switch. Covers publish timing (success,
/// failure, outcome-less terminals), partial declarations, Node-scope construction rejection,
/// the history lift-back rule, and the motivating parent-switch shape, on both runtimes.
/// </summary>
[TestFixture]
public class HierarchicalOutcomeTests
{
    private static (BlackboardSchema Schema, Blackboard Board, BlackboardKey<int> Code,
        BlackboardKey<string> Name) Boards()
    {
        BlackboardSchema schema = new("hierarchical-outcomes", BlackboardScope.Graph);
        BlackboardKey<int> code = schema.Register("verdict", -1);
        BlackboardKey<string> name = schema.Register("verdictName", "unset");
        return (schema, new Blackboard(schema), code, name);
    }

    /// <summary>
    /// Child with three terminals: 1 = "Approved" (success), 2 = "Refused" (failure when
    /// <paramref name="refusedFails"/>), and an outcome-less "plain" default — routed by a
    /// delegate switch reading <paramref name="desired"/> at run time.
    /// </summary>
    private static Graph Child(Func<int> desired, bool refusedFails = false)
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("c-start");
        GraphBuilder builder = start.Builder;

        NodeId approved = builder.AddNode(new RelayState(() => Result.Success));
        builder.TokenFor(approved).SetName("approved").WithOutcome(1, "Approved");
        NodeId refused = builder.AddNode(new RelayState(() => refusedFails ? Result.Failure : Result.Success));
        builder.TokenFor(refused).SetName("refused").WithOutcome(2, "Refused");
        NodeId plain = builder.AddNode(new RelayState(() => Result.Success));
        builder.TokenFor(plain).SetName("plain");

        Dictionary<int, NodeId> cases = new() { [1] = approved, [2] = refused };
        start.To(new RelaySwitchState<int>(desired, cases, plain));
        return builder.Build();
    }

    private static Graph Parent(bool sync, Graph child, BlackboardSchema schema,
        BlackboardKey<int>? code = null, BlackboardKey<string>? name = null)
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("p-start");
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, history: false, code, name)
            : start.SubGraph(child, history: false, code, name);
        return composite.SetName("nested").WithSchema(schema).Build();
    }

    private static async ValueTask<Result> RunAsync(Graph graph, Blackboard board, bool sync)
    {
        if (sync)
        {
            StateMachine machine = graph.ToStateMachine();
            machine.SetStepMode(ParallelStepMode.RunToJoin);
            machine.SetBlackboard(board);
            return machine.Execute();
        }

        AsyncStateMachine asyncMachine = graph.ToAsyncStateMachine();
        asyncMachine.SetBlackboard(board);
        return await asyncMachine.ExecuteAsync();
    }

    // ── Publish semantics ────────────────────────────────────────────────

    [Test]
    public async Task success_terminal_publishes_code_and_name([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        Graph parent = Parent(sync, Child(() => 1), schema, code, name);

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(code), Is.EqualTo(1));
            Assert.That(board.Get(name), Is.EqualTo("Approved"));
        });
    }

    [Test]
    public async Task failure_terminal_publishes_its_declared_outcome([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        Graph parent = Parent(sync, Child(() => 2, refusedFails: true), schema, code, name);

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure),
                "The child's failure still fails the composite — publication adds a channel, " +
                "not a fault-model change.");
            Assert.That(board.Get(code), Is.EqualTo(2));
            Assert.That(board.Get(name), Is.EqualTo("Refused"));
        });
    }

    [Test]
    public async Task outcome_less_terminal_publishes_zero_and_empty([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        Graph parent = Parent(sync, Child(() => 0), schema, code, name);

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(code), Is.Zero,
                "A terminal with no declared outcome publishes the machine's own 'nothing recorded' " +
                "reading — hosts route on non-zero codes.");
            Assert.That(board.Get(name), Is.Empty);
        });
    }

    [Test]
    public async Task code_only_declaration_leaves_the_name_key_alone([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        Graph parent = Parent(sync, Child(() => 1), schema, code);

        await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(board.Get(code), Is.EqualTo(1));
            Assert.That(board.Get(name), Is.EqualTo("unset"), "No name key declared — nothing writes it.");
        });
    }

    [Test]
    public async Task name_only_declaration_leaves_the_code_key_alone([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        Graph parent = Parent(sync, Child(() => 1), schema, name: name);

        await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(board.Get(code), Is.EqualTo(-1), "No code key declared — nothing writes it.");
            Assert.That(board.Get(name), Is.EqualTo("Approved"));
        });
    }

    [Test]
    public async Task keys_are_not_published_before_the_child_ends([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        int codeSeenInsideChild = int.MinValue;

        // The child's own first node reads the key: at that point the previous run's value
        // (the registered default here) must still be visible — publication happens at the
        // terminal, never at entry.
        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("c-start");
        Graph child = start.To(new RelayState(() => Result.Success)).SetName("c-end")
            .WithOutcome(9, "Done").Build();
        // Reading through the child is indirect; probe from the parent side instead: a node
        // between start and the composite snapshots the pre-publish value.
        StateToken pStart = GraphBuilder.StartWith(() => Result.Success).SetName("p-start");
        StateToken probe = pStart.To(bb =>
        {
            codeSeenInsideChild = bb.Get(code);
            return Result.Success;
        }).SetName("probe");
        StateToken composite = sync
            ? probe.SubGraph(ParallelStepMode.RunToJoin, child, false, code, name)
            : probe.SubGraph(child, false, code, name);
        Graph parent = composite.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(codeSeenInsideChild, Is.EqualTo(-1), "Nothing publishes before the terminal.");
            Assert.That(board.Get(code), Is.EqualTo(9));
            Assert.That(board.Get(name), Is.EqualTo("Done"));
        });
    }

    // ── Construction rejections ──────────────────────────────────────────

    [Test]
    public void node_scoped_outcome_keys_are_rejected_at_construction_naming_the_key()
    {
        BlackboardSchema scratch = new("outcome-scratch", BlackboardScope.Node);
        BlackboardKey<int> nodeCode = scratch.Register("nodeVerdict", 0);
        BlackboardKey<string> nodeName = scratch.Register("nodeVerdictName", "");
        Graph child = GraphBuilder.StartWith(() => Result.Success).Build();

        Assert.Multiple(() =>
        {
            ArgumentException? ex = Assert.Throws<ArgumentException>(
                () => _ = new AsyncStateMachine(child, null, nodeCode));
            Assert.That(ex!.Message, Does.Contain("'nodeVerdict'").And.Contain("Node-scoped"));

            ex = Assert.Throws<ArgumentException>(() => _ = new StateMachine(child, null, nodeCode));
            Assert.That(ex!.Message, Does.Contain("'nodeVerdict'"));

            ex = Assert.Throws<ArgumentException>(
                () => _ = new AsyncHistoryState(child, outcomeNameKey: nodeName));
            Assert.That(ex!.Message, Does.Contain("'nodeVerdictName'"));

            ex = Assert.Throws<ArgumentException>(
                () => _ = new HistoryState(child, ParallelStepMode.RunToJoin, outcomeNameKey: nodeName));
            Assert.That(ex!.Message, Does.Contain("'nodeVerdictName'"));

            // The DSL is the same construction path — the rejection reaches it unchanged.
            ex = Assert.Throws<ArgumentException>(() => _ = GraphBuilder
                .StartWith(() => Result.Success)
                .SubGraph(child, outcomeCode: nodeCode));
            Assert.That(ex!.Message, Does.Contain("'nodeVerdict'"));
        });
    }

    [Test]
    public void default_constructed_outcome_keys_are_rejected_at_construction()
    {
        Graph child = GraphBuilder.StartWith(() => Result.Success).Build();

        Assert.That(() => _ = new AsyncStateMachine(child, null, default(BlackboardKey<int>)),
            Throws.ArgumentException.With.Message.Contains("Invalid outcome key"));
    }

    // ── History: publish only at genuine terminals ───────────────────────

    [Test]
    public void sync_history_lift_back_reentry_publishes_only_when_the_resumed_session_ends()
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        bool repaired = false;

        // Child: gate (fails until repaired, outcome 5 "GateFailed") → done (outcome 9 "Done").
        Graph child = GraphBuilder
            .StartWith(() => repaired ? Result.Success : Result.Failure).SetName("gate")
            .WithOutcome(5, "GateFailed")
            .To(() => Result.Success).SetName("done").WithOutcome(9, "Done")
            .Build();

        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("p-start");
        StateToken composite = start
            .SubGraph(ParallelStepMode.RoundPerTick, child, history: true, code, name).SetName("work");
        GraphBuilder builder = composite.Builder;
        StateToken repair = builder.TokenFor(builder.AddNode(new RelayState(() =>
        {
            repaired = true;
            return Result.Success;
        }))).SetName("repair");
        composite.OnError(repair);
        repair.Goto("work");
        Graph parent = composite.WithSchema(schema).Build();

        StateMachine machine = parent.ToStateMachine();
        machine.SetBlackboard(board);

        bool failurePublished = false;
        int sentinelTicksAfterReentry = 0;
        Result result = Result.InProgress;
        for (int guard = 0; guard < 1_000 && result == Result.InProgress; guard++)
        {
            result = machine.Execute();
            if (!failurePublished && board.Get(code) == 5)
            {
                Assert.That(board.Get(name), Is.EqualTo("GateFailed"));
                failurePublished = true;
                // Overwrite with sentinels: any publication before the resumed session's own
                // terminal — the lift-back re-entry in particular — would disturb them.
                board.Set(code, -99);
                board.Set(name, "sentinel");
            }
            else if (failurePublished && board.Get(code) == -99)
            {
                sentinelTicksAfterReentry++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(failurePublished, Is.True, "The failure terminal publishes too.");
            Assert.That(sentinelTicksAfterReentry, Is.GreaterThan(0),
                "The lift-back re-entry itself publishes nothing — the sentinels survive at " +
                "least the resumed session's first tick.");
            Assert.That(board.Get(code), Is.EqualTo(9), "The resumed session's end publishes.");
            Assert.That(board.Get(name), Is.EqualTo("Done"));
        });
    }

    [Test]
    public async Task async_history_publishes_the_failure_then_the_resumed_sessions_end()
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, BlackboardKey<string> name) =
            Boards();
        bool repaired = false;
        (int Code, string Name) seenAtRepair = default;

        Graph child = GraphBuilder
            .StartWith(() => repaired ? Result.Success : Result.Failure).SetName("gate")
            .WithOutcome(5, "GateFailed")
            .To(() => Result.Success).SetName("done").WithOutcome(9, "Done")
            .Build();

        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("p-start");
        StateToken composite = start.SubGraph(child, history: true, code, name).SetName("work");
        GraphBuilder builder = composite.Builder;
        // The repair node runs between the two composite visits — snapshot the keys there.
        StateToken repair = builder.TokenFor(builder.AddNode(new RelayState(() =>
        {
            seenAtRepair = (board.Get(code), board.Get(name));
            repaired = true;
            return Result.Success;
        }))).SetName("repair");
        composite.OnError(repair);
        repair.Goto("work");
        Graph parent = composite.WithSchema(schema).Build();

        AsyncStateMachine machine = parent.ToAsyncStateMachine();
        machine.SetBlackboard(board);
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seenAtRepair, Is.EqualTo((5, "GateFailed")),
                "The failed session's terminal published before the parent's failure edge ran.");
            Assert.That(board.Get(code), Is.EqualTo(9),
                "The resumed session ended at 'done' — its terminal outcome is the final published value.");
            Assert.That(board.Get(name), Is.EqualTo("Done"));
        });
    }

    // ── The motivating shape: a parent switch routes on the child's outcome ──

    [TestCase(1, "approved-arm")]
    [TestCase(2, "refused-arm")]
    [TestCase(0, "other-arm")]
    public async Task parent_switch_routes_three_ways_on_the_childs_outcome(int desired, string expectedArm)
    {
        foreach (bool sync in new[] { false, true })
        {
            (BlackboardSchema schema, Blackboard board, BlackboardKey<int> code, _) = Boards();
            string? arm = null;

            Graph child = Child(() => desired);
            StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("p-start");
            StateToken composite = sync
                ? start.SubGraph(ParallelStepMode.RunToJoin, child, false, code)
                : start.SubGraph(child, false, code);
            Graph parent = composite.SetName("nested")
                .Switch(code)
                .Case(1, () =>
                {
                    arm = "approved-arm";
                    return Result.Success;
                })
                .Case(2, () =>
                {
                    arm = "refused-arm";
                    return Result.Success;
                })
                .Default(() =>
                {
                    arm = "other-arm";
                    return Result.Success;
                })
                .End()
                .WithSchema(schema)
                .Build();

            Result result = await RunAsync(parent, board, sync);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Result.Success));
                Assert.That(arm, Is.EqualTo(expectedArm),
                    $"[{(sync ? "sync" : "async")}] the immediately following switch reads the " +
                    "freshly published outcome.");
            });
        }
    }

    // ── Validator lint ───────────────────────────────────────────────────

    [Test]
    public void validator_warns_when_keys_are_declared_but_the_child_declares_no_outcomes(
        [Values] bool sync)
    {
        (BlackboardSchema schema, _, BlackboardKey<int> code, _) = Boards();
        Graph child = GraphBuilder.StartWith(() => Result.Success).Build(); // no WithOutcome anywhere
        Graph parent = Parent(sync, child, schema, code);

        Diagnostics.Validations.GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Diagnostics.Validations.Severity.Warning &&
                d.Message.Contains("no outcome codes")), Is.True,
            "One Warning: the parent would only ever read 0 / the empty string.");
    }

    [Test]
    public void validator_stays_quiet_when_the_child_declares_outcomes([Values] bool sync)
    {
        (BlackboardSchema schema, _, BlackboardKey<int> code, _) = Boards();
        Graph parent = Parent(sync, Child(() => 1), schema, code);

        Diagnostics.Validations.GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("no outcome codes")), Is.False);
    }

    [Test]
    public void validator_stays_quiet_when_no_keys_are_declared([Values] bool sync)
    {
        (BlackboardSchema schema, _, _, _) = Boards();
        Graph child = GraphBuilder.StartWith(() => Result.Success).Build();
        Graph parent = Parent(sync, child, schema);

        Diagnostics.Validations.GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("no outcome codes")), Is.False);
    }
}
