using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization;

namespace NxGraph.Tests;

/// <summary>
/// Sub-graph ports: a machine-wrapping composite runs its child graph on the child's own
/// Graph-scoped board — created from the child's declared schema, owned by the composite —
/// with declared inputs applied at every fresh child start and declared outputs copied back
/// at the child's terminal. Covers isolation (sequential and parallel siblings, undeclared
/// parent keys), literal and key-bound inputs, success/failure outputs, declaration order,
/// fresh-start reset versus the history opt-out, construction rejections, the owned-board
/// enumeration walk with the full durability artifact loop, and the validator lints — on
/// both runtimes.
/// </summary>
[TestFixture]
public class SubGraphPortTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static (BlackboardSchema Schema, Blackboard Board, BlackboardKey<int> Seed,
        BlackboardKey<int> Verdict, BlackboardKey<int> VerdictB) ParentBoards()
    {
        BlackboardSchema schema = new("port-parent", BlackboardScope.Graph);
        BlackboardKey<int> seed = schema.Register("seed", 0);
        BlackboardKey<int> verdict = schema.Register("verdict", -1);
        BlackboardKey<int> verdictB = schema.Register("verdictB", -1);
        return (schema, new Blackboard(schema), seed, verdict, verdictB);
    }

    /// <summary>
    /// Child recipe: <c>childOut = childIn + childBonus</c>, all keys on the child's own
    /// Graph schema (fresh schema instance per call — same key names every time, which is
    /// exactly the sibling collision the ports feature removes).
    /// </summary>
    private static (Graph Graph, BlackboardKey<int> In, BlackboardKey<int> Bonus, BlackboardKey<int> Out)
        AddingChild()
    {
        BlackboardSchema schema = new("port-child", BlackboardScope.Graph);
        BlackboardKey<int> input = schema.Register("childIn", 0);
        BlackboardKey<int> bonus = schema.Register("childBonus", 0);
        BlackboardKey<int> output = schema.Register("childOut", 0);

        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("c-start")
            .To(bb =>
            {
                bb.Set(output, bb.Get(input) + bb.Get(bonus));
                return Result.Success;
            }).SetName("c-add")
            .WithSchema(schema)
            .Build();
        return (graph, input, bonus, output);
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

    // ── Inputs and outputs ───────────────────────────────────────────────

    [Test]
    public async Task literal_and_key_inputs_apply_and_the_output_copies_back([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict, _) =
            ParentBoards();
        (Graph child, BlackboardKey<int> childIn, BlackboardKey<int> childBonus, BlackboardKey<int> childOut) =
            AddingChild();

        SubGraphPorts ports = SubGraphPorts.OwnBoard()
            .In(seed, childIn) // parent key → child key
            .In(5, childBonus) // literal → child key
            .Out(childOut, verdict); // child key → parent key

        StateToken start = GraphBuilder.StartWith(bb =>
        {
            bb.Set(seed, 37);
            return Result.Success;
        }).SetName("p-seed");
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, ports: ports)
            : start.SubGraph(child, ports: ports);
        Graph parent = composite.SetName("nested").WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(42),
                "seed (37) flowed in through the key input, 5 through the literal, and the sum came back.");
            Assert.That(board.Get(seed), Is.EqualTo(37), "The parent board keeps its own keys untouched.");
        });
    }

    [Test]
    public async Task outputs_copy_on_the_failure_path_too([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<int> verdict, _) = ParentBoards();

        BlackboardSchema childSchema = new("failing-child", BlackboardScope.Graph);
        BlackboardKey<int> diagnostic = childSchema.Register("diagnostic", 0);
        Graph child = GraphBuilder
            .StartWith(bb =>
            {
                bb.Set(diagnostic, 13);
                return Result.Failure; // the child fails after writing its diagnostic
            })
            .WithSchema(childSchema)
            .Build();

        SubGraphPorts ports = SubGraphPorts.OwnBoard().Out(diagnostic, verdict);
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, ports: ports)
            : start.SubGraph(child, ports: ports);
        Graph parent = composite.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure),
                "The child's failure still fails the composite — ports add a channel, not a fault-model change.");
            Assert.That(board.Get(verdict), Is.EqualTo(13),
                "Diagnostics flow on the failure path: the output copied before the composite returned.");
        });
    }

    [Test]
    public async Task later_input_declarations_overwrite_earlier_ones([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<int> verdict, _) = ParentBoards();
        (Graph child, BlackboardKey<int> childIn, _, BlackboardKey<int> childOut) = AddingChild();

        SubGraphPorts ports = SubGraphPorts.OwnBoard()
            .In(1, childIn)
            .In(2, childIn) // declaration order is application order — the later write wins
            .Out(childOut, verdict);

        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, ports: ports)
            : start.SubGraph(child, ports: ports);
        Graph parent = composite.WithSchema(schema).Build();

        await RunAsync(parent, board, sync);

        Assert.That(board.Get(verdict), Is.EqualTo(2));
    }

    // ── Isolation ────────────────────────────────────────────────────────

    [Test]
    public async Task sequential_siblings_with_same_named_keys_do_not_interfere([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<int> verdict, BlackboardKey<int> verdictB) =
            ParentBoards();
        (Graph childA, BlackboardKey<int> inA, _, BlackboardKey<int> outA) = AddingChild();
        (Graph childB, BlackboardKey<int> inB, _, BlackboardKey<int> outB) = AddingChild();

        SubGraphPorts portsA = SubGraphPorts.OwnBoard().In(10, inA).Out(outA, verdict);
        SubGraphPorts portsB = SubGraphPorts.OwnBoard().In(30, inB).Out(outB, verdictB);

        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken first = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, childA, ports: portsA)
            : start.SubGraph(childA, ports: portsA);
        StateToken second = sync
            ? first.SubGraph(ParallelStepMode.RunToJoin, childB, ports: portsB)
            : first.SubGraph(childB, ports: portsB);
        Graph parent = second.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(10), "Sibling A computed on its own board.");
            Assert.That(board.Get(verdictB), Is.EqualTo(30), "Sibling B computed on its own board.");
        });
    }

    [Test]
    public async Task parallel_sibling_regions_each_get_their_own_board([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<int> verdict, BlackboardKey<int> verdictB) =
            ParentBoards();
        (Graph regionA, BlackboardKey<int> inA, _, BlackboardKey<int> outA) = AddingChild();
        (Graph regionB, BlackboardKey<int> inB, _, BlackboardKey<int> outB) = AddingChild();

        ParallelRegion[] regions =
        [
            new(regionA, SubGraphPorts.OwnBoard().In(10, inA).Out(outA, verdict)),
            new(regionB, SubGraphPorts.OwnBoard().In(30, inB).Out(outB, verdictB)),
        ];

        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken composite = sync
            ? start.Parallel(ParallelStepMode.RunToJoin, regions)
            : start.Parallel(regions);
        Graph parent = composite.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(10), "Region 0 computed on its own board.");
            Assert.That(board.Get(verdictB), Is.EqualTo(30), "Region 1 computed on its own board.");
        });
    }

    [Test]
    public async Task deselected_dynamic_region_applies_no_inputs_and_copies_no_outputs([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<int> verdict, BlackboardKey<int> verdictB) =
            ParentBoards();
        (Graph regionA, BlackboardKey<int> inA, _, BlackboardKey<int> outA) = AddingChild();
        (Graph regionB, BlackboardKey<int> inB, _, BlackboardKey<int> outB) = AddingChild();

        ParallelRegion[] regions =
        [
            new(regionA, SubGraphPorts.OwnBoard().In(10, inA).Out(outA, verdict)),
            new(regionB, SubGraphPorts.OwnBoard().In(30, inB).Out(outB, verdictB)),
        ];

        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken composite = sync
            ? start.Parallel(ParallelStepMode.RunToJoin, _ => RegionMask.Bit(0), regions)
            : start.Parallel(_ => RegionMask.Bit(0), regions);
        Graph parent = composite.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(10));
            Assert.That(board.Get(verdictB), Is.EqualTo(-1),
                "The deselected region never ran: no inputs applied, no outputs copied.");
        });
    }

    [Test]
    public async Task child_cannot_see_an_undeclared_parent_key([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> seed, _, _) = ParentBoards();

        BlackboardSchema childSchema = new("nosy-child", BlackboardScope.Graph);
        childSchema.Register("own", 0);
        Graph child = GraphBuilder
            .StartWith(bb => bb.Get(seed) >= 0 ? Result.Success : Result.Failure) // a parent-schema key
            .WithSchema(childSchema)
            .Build();

        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, ports: SubGraphPorts.OwnBoard())
            : start.SubGraph(child, ports: SubGraphPorts.OwnBoard());
        Graph parent = composite.WithSchema(schema).Build();

        InvalidOperationException? ex = sync
            ? Assert.Throws<InvalidOperationException>(() =>
            {
                StateMachine machine = parent.ToStateMachine();
                machine.SetStepMode(ParallelStepMode.RunToJoin);
                machine.SetBlackboard(board);
                machine.Execute();
            })
            : Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                AsyncStateMachine machine = parent.ToAsyncStateMachine();
                machine.SetBlackboard(board);
                await machine.ExecuteAsync();
            });

        Assert.That(ex!.Message, Does.Contain("belongs to schema"),
            "The child's board is its own: a parent-schema key is a foreign key, not a shared slot.");
    }

    [Test]
    public async Task a_board_only_declaration_isolates_without_any_ports([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> seed, _, _) = ParentBoards();

        BlackboardSchema childSchema = new("plain-child", BlackboardScope.Graph);
        BlackboardKey<int> own = childSchema.Register("own", 7);
        int seen = int.MinValue;
        Graph child = GraphBuilder
            .StartWith(bb =>
            {
                seen = bb.Get(own);
                return Result.Success;
            })
            .WithSchema(childSchema)
            .Build();

        StateToken start = GraphBuilder.StartWith(bb =>
        {
            bb.Set(seed, 99);
            return Result.Success;
        });
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, ports: SubGraphPorts.OwnBoard())
            : start.SubGraph(child, ports: SubGraphPorts.OwnBoard());
        Graph parent = composite.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(7), "The child read its own registered default — its own board.");
        });
    }

    // ── Fresh-start reset versus history ─────────────────────────────────

    [Test]
    public async Task every_fresh_start_resets_the_owned_board_before_inputs([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, _, BlackboardKey<int> verdict, _) = ParentBoards();

        BlackboardSchema childSchema = new("counting-child", BlackboardScope.Graph);
        BlackboardKey<int> counter = childSchema.Register("counter", 0);
        Graph child = GraphBuilder
            .StartWith(bb =>
            {
                bb.Set(counter, bb.Get(counter) + 1);
                return Result.Success;
            })
            .WithSchema(childSchema)
            .Build();

        SubGraphPorts ports = SubGraphPorts.OwnBoard().Out(counter, verdict);
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, ports: ports)
            : start.SubGraph(child, ports: ports);
        Graph parent = composite.WithSchema(schema).Build();

        // Two whole parent runs: the second visit is a fresh child start and must not
        // half-remember the first (the Node-scope "reset iff attempts reset" spirit, at the
        // visit level).
        await RunAsync(parent, board, sync);
        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(1),
                "The owned board reset to defaults before the second visit's inputs applied.");
        });
    }

    [Test]
    public async Task history_lift_back_reentry_keeps_the_board_and_applies_no_inputs([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<int> seed, BlackboardKey<int> verdict, _) =
            ParentBoards();

        BlackboardSchema childSchema = new("history-child", BlackboardScope.Graph);
        BlackboardKey<int> childSeed = childSchema.Register("childSeed", 0);
        BlackboardKey<int> visits = childSchema.Register("visits", 0);
        int seedSeenAtReentry = int.MinValue;
        Graph child = GraphBuilder
            .StartWith(bb =>
            {
                int v = bb.Get(visits) + 1;
                bb.Set(visits, v);
                if (v < 2)
                {
                    return Result.Failure; // first session ends failed at this node
                }

                seedSeenAtReentry = bb.Get(childSeed);
                return Result.Success;
            }).SetName("gate")
            .To(() => Result.Success).SetName("c-done")
            .WithSchema(childSchema)
            .Build();

        SubGraphPorts ports = SubGraphPorts.OwnBoard()
            .In(seed, childSeed)
            .Out(visits, verdict);

        StateToken start = GraphBuilder.StartWith(bb =>
        {
            bb.Set(seed, 5);
            return Result.Success;
        }).SetName("p-seed");
        StateToken composite = sync
            ? start.SubGraph(ParallelStepMode.RunToJoin, child, history: true, ports: ports)
            : start.SubGraph(child, history: true, ports: ports);
        StateToken work = composite.SetName("work");
        GraphBuilder builder = work.Builder;
        StateToken repair = builder.TokenFor(builder.AddNode(new RelayState(() =>
        {
            board.Set(seed, 9); // the parent-side source changes between the two visits
            return Result.Success;
        }))).SetName("repair");
        work.OnError(repair);
        repair.Goto("work");
        Graph parent = work.WithSchema(schema).Build();

        Result result = await RunAsync(parent, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seedSeenAtReentry, Is.EqualTo(5),
                "The lift-back re-entry applied nothing: the child kept the first session's input, " +
                "not the changed parent value.");
            Assert.That(board.Get(verdict), Is.EqualTo(2),
                "The board survived the re-entry (visits kept counting) and the resumed session's " +
                "terminal copied the outputs.");
        });
    }

    // ── Construction rejections ──────────────────────────────────────────

    [Test]
    public void a_child_with_no_graph_schema_rejects_the_declaration()
    {
        Graph child = GraphBuilder.StartWith(() => Result.Success).Build(); // no WithSchema
        SubGraphPorts ports = SubGraphPorts.OwnBoard();

        Assert.Multiple(() =>
        {
            Assert.That(() => _ = new AsyncStateMachine(child, ports: ports),
                Throws.ArgumentException.With.Message.Contains("no Graph-scoped"));
            Assert.That(() => _ = new StateMachine(child, ports: ports),
                Throws.ArgumentException.With.Message.Contains("no Graph-scoped"));
            Assert.That(() => _ = new AsyncHistoryState(child, ports: ports),
                Throws.ArgumentException.With.Message.Contains("no Graph-scoped"));
            Assert.That(() => _ = new HistoryState(child, ports: ports),
                Throws.ArgumentException.With.Message.Contains("no Graph-scoped"));
            Assert.That(() => _ = new AsyncParallelState([new ParallelRegion(child, ports)]),
                Throws.ArgumentException.With.Message.Contains("no Graph-scoped"));
            Assert.That(() => _ = GraphBuilder.StartWith(() => Result.Success).SubGraph(child, ports: ports),
                Throws.ArgumentException.With.Message.Contains("no Graph-scoped"));
        });
    }

    [Test]
    public void a_foreign_target_key_is_rejected_naming_the_key()
    {
        (BlackboardSchema parentSchema, _, BlackboardKey<int> seed, BlackboardKey<int> verdict, _) =
            ParentBoards();
        (Graph child, _, _, BlackboardKey<int> childOut) = AddingChild();
        _ = parentSchema;

        // The input target belongs to the parent's schema, not the child's.
        SubGraphPorts foreignIn = SubGraphPorts.OwnBoard().In(1, seed);
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new AsyncStateMachine(child, ports: foreignIn));
        Assert.That(ex!.Message, Does.Contain("'seed'").And.Contain("does not belong"));

        // The output source belongs to a different child recipe's schema.
        (Graph otherChild, _, _, _) = AddingChild();
        SubGraphPorts foreignOut = SubGraphPorts.OwnBoard().Out(childOut, verdict);
        ex = Assert.Throws<ArgumentException>(
            () => _ = new StateMachine(otherChild, ports: foreignOut));
        Assert.That(ex!.Message, Does.Contain("'childOut'").And.Contain("does not belong"));
    }

    [Test]
    public void node_scoped_parent_side_keys_are_rejected_naming_the_key()
    {
        (Graph child, BlackboardKey<int> childIn, _, BlackboardKey<int> childOut) = AddingChild();
        _ = child;
        BlackboardSchema scratch = new("port-scratch", BlackboardScope.Node);
        BlackboardKey<int> nodeKey = scratch.Register("nodeSlot", 0);

        // Output target: Node scope resets before the parent's next node runs.
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => SubGraphPorts.OwnBoard().Out(childOut, nodeKey));
        Assert.That(ex!.Message, Does.Contain("'nodeSlot'").And.Contain("Node-scoped"));

        // Input source: the parent's Node scratch never crosses the composite boundary.
        ex = Assert.Throws<ArgumentException>(
            () => SubGraphPorts.OwnBoard().In(nodeKey, childIn));
        Assert.That(ex!.Message, Does.Contain("'nodeSlot'").And.Contain("Node-scoped"));
    }

    [Test]
    public void default_constructed_port_keys_are_rejected()
    {
        (Graph child, BlackboardKey<int> childIn, _, BlackboardKey<int> childOut) = AddingChild();
        _ = child;

        Assert.Multiple(() =>
        {
            Assert.That(() => SubGraphPorts.OwnBoard().In(1, default(BlackboardKey<int>)),
                Throws.ArgumentException.With.Message.Contains("Invalid input port target"));
            Assert.That(() => SubGraphPorts.OwnBoard().Out(default, childOut),
                Throws.ArgumentException.With.Message.Contains("Invalid output port source"));
            Assert.That(() => SubGraphPorts.OwnBoard().Out(childIn, default),
                Throws.ArgumentException.With.Message.Contains("Invalid output port target"));
        });
    }

    // ── Owned-board enumeration + the durability artifact loop ───────────

    [Test]
    public void the_owned_board_walk_yields_node_index_paths_in_deterministic_order()
    {
        (BlackboardSchema schema, _, _, BlackboardKey<int> verdict, BlackboardKey<int> verdictB) =
            ParentBoards();
        (Graph inner, BlackboardKey<int> innerIn, _, BlackboardKey<int> innerOut) = AddingChild();

        // A middle graph that itself nests a ported composite at its node 0.
        BlackboardSchema middleSchema = new("middle", BlackboardScope.Graph);
        BlackboardKey<int> middleOut = middleSchema.Register("middleOut", 0);
        Graph middle = GraphBuilder
            .Start()
            .SubGraph(inner, ports: SubGraphPorts.OwnBoard().In(1, innerIn).Out(innerOut, middleOut))
            .WithSchema(middleSchema)
            .Build();

        (Graph regionGraph, BlackboardKey<int> regionIn, _, BlackboardKey<int> regionOut) = AddingChild();
        Graph parent = GraphBuilder
            .StartWith(() => Result.Success) // node 0: plain
            .SubGraph(middle, ports: SubGraphPorts.OwnBoard().Out(middleOut, verdict)) // node 1
            .Parallel([
                new ParallelRegion(regionGraph,
                    SubGraphPorts.OwnBoard().In(2, regionIn).Out(regionOut, verdictB)),
            ]) // node 2, region 0
            .WithSchema(schema)
            .Build();

        AsyncStateMachine machine = parent.ToAsyncStateMachine();
        List<OwnedBoardEntry> entries = machine.EnumerateOwnedBoards().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(static e => e.Path), Is.EqualTo(new[] { "1", "1/0", "2/0" }),
                "Node-index-path identity, in deterministic node order: the middle composite's own " +
                "board, the inner composite inside it, and the parallel's region 0.");
            Assert.That(entries.Select(static e => e.Board).Distinct().Count(), Is.EqualTo(3),
                "Three distinct boards.");
        });
    }

    private static T JsonRoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    [Test]
    public async Task owned_boards_survive_a_full_durability_artifact_loop([Values] bool sync)
    {
        (BlackboardSchema _, Blackboard _, _, _, _) = ParentBoards();

        // The graph recipe is rebuilt for the fresh machine — the standard resume contract
        // ("a machine over an equivalent graph"); the artifacts are the deep snapshot plus
        // one BlackboardSerializer payload per owned board from the enumeration walk, and the
        // parent board.
        (BlackboardSchema Schema, Blackboard Board, BlackboardKey<int> Seed, BlackboardKey<int> Verdict)
            MakeParentBoards()
        {
            BlackboardSchema schema = new("durable-parent", BlackboardScope.Graph);
            BlackboardKey<int> seed = schema.Register("seed", 0);
            BlackboardKey<int> verdict = schema.Register("verdict", -1);
            return (schema, new Blackboard(schema), seed, verdict);
        }

        Graph BuildParent(BlackboardSchema schema, BlackboardKey<int> seed, BlackboardKey<int> verdict)
        {
            BlackboardSchema childSchema = new("durable-child", BlackboardScope.Graph);
            BlackboardKey<int> childSeed = childSchema.Register("childSeed", 0);
            BlackboardKey<int> visits = childSchema.Register("visits", 0);
            Graph child = GraphBuilder
                .StartWith(bb =>
                {
                    int v = bb.Get(visits) + 1;
                    bb.Set(visits, v);
                    return v >= 2 ? Result.Success : Result.Failure; // fails its first session
                }).SetName("gate")
                .To(bb =>
                {
                    bb.Set(visits, bb.Get(visits) + bb.Get(childSeed));
                    return Result.Success;
                }).SetName("c-final")
                .WithSchema(childSchema)
                .Build();

            SubGraphPorts ports = SubGraphPorts.OwnBoard().In(seed, childSeed).Out(visits, verdict);
            StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("p-start");
            StateToken work = sync
                ? start.SubGraph(ParallelStepMode.RunToJoin, child, history: true, ports: ports)
                : start.SubGraph(child, history: true, ports: ports);
            work = work.SetName("work");
            GraphBuilder builder = work.Builder;
            StateToken repair = builder.TokenFor(builder.AddNode(new RelayState(() => Result.Success)))
                .SetName("repair");
            work.OnError(repair);
            repair.Goto("work");
            return work.WithSchema(schema).Build();
        }

        (BlackboardSchema parentSchema, Blackboard board, BlackboardKey<int> seed,
            BlackboardKey<int> verdict) = MakeParentBoards();
        board.Set(seed, 40);

        // First life: step until the child's first session has failed (its board holds
        // visits = 1 — mid-child state that only the owned board carries).
        Graph firstGraph = BuildParent(parentSchema, seed, verdict);
        StateMachineDeepSnapshot deep;
        List<(string Path, byte[] Payload)> boardPayloads = [];
        BlackboardSerializer boardSerializer = new();
        if (sync)
        {
            StateMachine first = firstGraph.ToStateMachine();
            first.SetBlackboard(board);
            first.Execute(); // p-start
            first.Execute(); // work: child session 1 fails; history keeps the position
            deep = JsonRoundTrip(first.SuspendDeep());
            foreach (OwnedBoardEntry entry in first.EnumerateOwnedBoards())
            {
                await using MemoryStream stream = new();
                await boardSerializer.ToBinaryAsync(entry.Board, stream);
                boardPayloads.Add((entry.Path, stream.ToArray()));
            }
        }
        else
        {
            AsyncStateMachine first = firstGraph.ToAsyncStateMachine();
            first.SetBlackboard(board);
            await first.StepAsync(); // p-start
            await first.StepAsync(); // work: child session 1 fails; history keeps the position
            deep = JsonRoundTrip(first.SuspendDeep());
            foreach (OwnedBoardEntry entry in first.EnumerateOwnedBoards())
            {
                await using MemoryStream stream = new();
                await boardSerializer.ToBinaryAsync(entry.Board, stream);
                boardPayloads.Add((entry.Path, stream.ToArray()));
            }
        }

        Assert.That(boardPayloads.Select(static p => p.Path), Is.EqualTo(new[] { "1" }),
            "Exactly one owned board, at the composite's node index.");

        // Second life: fresh machines over an equivalent graph; restore the owned boards
        // into the fresh enumeration by path, then ResumeDeep and run to completion.
        Graph secondGraph = BuildParent(parentSchema, seed, verdict);
        Result result;
        if (sync)
        {
            StateMachine second = secondGraph.ToStateMachine();
            second.SetBlackboard(board);
            foreach (OwnedBoardEntry entry in second.EnumerateOwnedBoards())
            {
                byte[] payload = boardPayloads.Single(p => p.Path == entry.Path).Payload;
                await using MemoryStream stream = new(payload);
                await boardSerializer.RestoreFromBinaryAsync(entry.Board, stream);
            }

            second.ResumeDeep(deep);
            result = Result.InProgress;
            for (int guard = 0; guard < 100 && result == Result.InProgress; guard++)
            {
                result = second.Execute();
            }
        }
        else
        {
            AsyncStateMachine second = secondGraph.ToAsyncStateMachine();
            second.SetBlackboard(board);
            foreach (OwnedBoardEntry entry in second.EnumerateOwnedBoards())
            {
                byte[] payload = boardPayloads.Single(p => p.Path == entry.Path).Payload;
                await using MemoryStream stream = new(payload);
                await boardSerializer.RestoreFromBinaryAsync(entry.Board, stream);
            }

            second.ResumeDeep(deep);
            result = Result.InProgress;
            for (int guard = 0; guard < 100 && result == Result.InProgress; guard++)
            {
                result = await second.StepAsync();
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(verdict), Is.EqualTo(42),
                "visits survived the boundary as 1, the lift-back re-entry made it 2 (no reset, no " +
                "re-applied inputs), and c-final added the surviving childSeed (40).");
        });
    }

    // ── Validator lints ──────────────────────────────────────────────────

    [Test]
    public void validator_reports_an_info_when_a_composite_owns_a_board()
    {
        (BlackboardSchema schema, _, _, BlackboardKey<int> verdict, _) = ParentBoards();
        (Graph child, BlackboardKey<int> childIn, _, BlackboardKey<int> childOut) = AddingChild();

        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(child, ports: SubGraphPorts.OwnBoard().In(1, childIn).Out(childOut, verdict))
            .WithSchema(schema)
            .Build();

        GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(static d =>
            d.Severity == Severity.Info && d.Message.Contains("own boards")), Is.True);
    }

    [Test]
    public void validator_warns_when_two_outputs_target_the_same_parent_key_across_regions()
    {
        (BlackboardSchema schema, _, _, BlackboardKey<int> verdict, _) = ParentBoards();
        (Graph regionA, _, _, BlackboardKey<int> outA) = AddingChild();
        (Graph regionB, _, _, BlackboardKey<int> outB) = AddingChild();

        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .Parallel([
                new ParallelRegion(regionA, SubGraphPorts.OwnBoard().Out(outA, verdict)),
                new ParallelRegion(regionB, SubGraphPorts.OwnBoard().Out(outB, verdict)),
            ])
            .WithSchema(schema)
            .Build();

        GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(static d =>
            d.Severity == Severity.Warning && d.Message.Contains("'verdict'") &&
            d.Message.Contains("same parent key")), Is.True);
    }

    [Test]
    public void validator_warns_on_an_input_the_child_never_reads_when_decidable()
    {
        (BlackboardSchema schema, _, _, _, _) = ParentBoards();

        // A fully data-built child: one switch reading 'route' with terminal arms — its
        // reads are statically known, so an input targeting an unread key is decidably dead.
        BlackboardSchema childSchema = new("switchy-child", BlackboardScope.Graph);
        BlackboardKey<int> route = childSchema.Register("route", 0);
        BlackboardKey<int> unread = childSchema.Register("unread", 0);
        Graph child = GraphBuilder
            .Start()
            .To(new SwitchState<int>(route, [new SwitchCase<int>(1, NodeId.Default)], NodeId.Default))
            .WithSchema(childSchema)
            .Build();

        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(child, ports: SubGraphPorts.OwnBoard().In(1, route).In(2, unread))
            .WithSchema(schema)
            .Build();

        GraphValidationResult result = parent.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Any(static d =>
                d.Severity == Severity.Warning && d.Message.Contains("'unread'") &&
                d.Message.Contains("never read")), Is.True);
            Assert.That(result.Diagnostics.Any(static d => d.Message.Contains("'route'") &&
                d.Message.Contains("never read")), Is.False);
        });
    }

    [Test]
    public void validator_skips_the_unread_input_check_for_opaque_children()
    {
        (BlackboardSchema schema, _, _, _, _) = ParentBoards();
        (Graph child, BlackboardKey<int> childIn, _, _) = AddingChild(); // relay lambdas = opaque

        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(child, ports: SubGraphPorts.OwnBoard().In(1, childIn))
            .WithSchema(schema)
            .Build();

        GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(static d => d.Message.Contains("never read")), Is.False,
            "Opaque node logic makes the question undecidable — the best-effort check skips.");
    }

    [Test]
    public void validator_no_longer_warns_about_a_child_schema_when_the_composite_owns_the_board()
    {
        (BlackboardSchema schema, _, _, _, _) = ParentBoards();
        (Graph child, _, _, _) = AddingChild(); // declares its own Graph schema

        Graph withPorts = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(child, ports: SubGraphPorts.OwnBoard())
            .WithSchema(schema)
            .Build();

        (Graph child2, _, _, _) = AddingChild();
        Graph withoutPorts = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(child2)
            .WithSchema(schema)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(withPorts.Validate().Diagnostics.Any(static d =>
                    d.Message.Contains("different Graph-scoped blackboard schema")), Is.False,
                "Owning the board is exactly the sanctioned shape for a child with its own schema.");
            Assert.That(withoutPorts.Validate().Diagnostics.Any(static d =>
                    d.Message.Contains("different Graph-scoped blackboard schema")), Is.True,
                "Without ports the conflicting-schema warning still fires.");
        });
    }
}
