using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests.Blackboards;

/// <summary>
/// The blackboard channel through the FSM runtimes: schema-on-graph declarations,
/// per-machine binding via <c>WithBlackboard</c> (routed by schema scope), context stamping
/// alongside the agent channel, and composite propagation.
/// </summary>
[TestFixture]
public class BlackboardFsmIntegrationTests
{
    private sealed class Enemy
    {
        public string Name = "";
        public int Health = 100;
    }

    private static BlackboardSchema NewWorldSchema(out BlackboardKey<bool> alarm, out BlackboardKey<int> sightings)
    {
        BlackboardSchema world = new("world", BlackboardScope.Global);
        alarm = world.Register<bool>("AlarmRaised");
        sightings = world.Register<int>("Sightings");
        return world;
    }

    private static BlackboardSchema NewEnemySchema(out BlackboardKey<int> target, out BlackboardKey<int> speed)
    {
        BlackboardSchema enemy = new("enemy");
        target = enemy.Register<int>("target", -1);
        speed = enemy.Register<int>("speed", 1);
        return enemy;
    }

    // ── Target API shape ─────────────────────────────────────────────────

    [Test]
    public async Task async_machine_binds_boards_and_agent_in_any_order()
    {
        BlackboardSchema world = NewWorldSchema(out BlackboardKey<bool> alarm, out _);
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph graph = GraphBuilder
            .StartWithAsync(new AsyncRelayState<Enemy>((enemy, bb, _) =>
            {
                bb.Set(alarm, true);
                bb.Set(target, enemy.Health);
                return ResultHelpers.Success;
            }))
            .WithSchema(enemySchema)
            .WithSchema(world)
            .Build();

        Blackboard worldBb = new(world);
        Blackboard enemyBb = new(enemySchema);
        Enemy agent = new() { Health = 73 };

        // The user-facing chain, verbatim.
        AsyncStateMachine<Enemy> fsm = graph.ToAsyncStateMachine<Enemy>()
            .WithBlackboard(worldBb)
            .WithBlackboard(enemyBb)
            .WithAgent(agent);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(worldBb.Get(alarm), Is.True);
            Assert.That(enemyBb.Get(target), Is.EqualTo(73));
        });

        // And the reverse binding order on a fresh machine.
        Blackboard enemyBb2 = new(enemySchema);
        AsyncStateMachine<Enemy> fsm2 = graph.ToAsyncStateMachine<Enemy>()
            .WithAgent(agent)
            .WithBlackboard(enemyBb2)
            .WithBlackboard(worldBb);

        await fsm2.ExecuteAsync();
        Assert.That(enemyBb2.Get(target), Is.EqualTo(73));
    }

    [Test]
    public void sync_machine_binds_boards_and_agent()
    {
        BlackboardSchema world = NewWorldSchema(out BlackboardKey<bool> alarm, out _);
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph graph = GraphBuilder
            .StartWith(new RelayState<Enemy>((enemy, bb) =>
            {
                bb.Set(alarm, true);
                bb.Set(target, enemy.Health);
                return Result.Success;
            }))
            .WithSchema(enemySchema)
            .WithSchema(world)
            .Build();

        Blackboard worldBb = new(world);
        Blackboard enemyBb = new(enemySchema);

        StateMachine<Enemy> fsm = graph.ToStateMachine<Enemy>()
            .WithBlackboard(worldBb)
            .WithBlackboard(enemyBb)
            .WithAgent(new Enemy { Health = 40 });

        RunToCompletion(fsm);

        Assert.Multiple(() =>
        {
            Assert.That(worldBb.Get(alarm), Is.True);
            Assert.That(enemyBb.Get(target), Is.EqualTo(40));
        });
    }

    // ── Global sharing, distinct graph boards ─────────────────────────────

    [Test]
    public async Task one_global_board_is_shared_across_machines_over_different_graphs()
    {
        BlackboardSchema world = NewWorldSchema(out BlackboardKey<bool> alarm, out BlackboardKey<int> sightings);

        Graph writerGraph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(alarm, true);
                bb.GetRef(sightings)++;
                return ResultHelpers.Success;
            })
            .Build();

        bool seen = false;
        Graph readerGraph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                seen = bb.Get(alarm);
                return ResultHelpers.Success;
            })
            .Build();

        Blackboard worldBb = new(world);
        AsyncStateMachine writer = writerGraph.ToAsyncStateMachine().WithBlackboard(worldBb);
        AsyncStateMachine reader = readerGraph.ToAsyncStateMachine().WithBlackboard(worldBb);

        await writer.ExecuteAsync();
        await reader.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.True, "A write through machine A's run must be visible in machine B's run.");
            Assert.That(worldBb.Get(sightings), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task machines_over_one_graph_hold_distinct_graph_boards_but_one_global()
    {
        BlackboardSchema world = NewWorldSchema(out _, out BlackboardKey<int> sightings);
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.GetRef(sightings)++;
                bb.Set(target, bb.Get(sightings));
                return ResultHelpers.Success;
            })
            .WithSchema(enemySchema)
            .WithSchema(world)
            .Build();

        Blackboard worldBb = new(world);
        Blackboard boardA = new(enemySchema);
        Blackboard boardB = new(enemySchema);

        AsyncStateMachine machineA = graph.ToAsyncStateMachine().WithBlackboard(worldBb).WithBlackboard(boardA);
        AsyncStateMachine machineB = graph.ToAsyncStateMachine().WithBlackboard(worldBb).WithBlackboard(boardB);

        await machineA.ExecuteAsync();
        await machineB.ExecuteAsync();
        await machineA.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(worldBb.Get(sightings), Is.EqualTo(3), "Global memory accumulates across all machines.");
            Assert.That(boardA.Get(target), Is.EqualTo(3), "Machine A's board reflects its own last run.");
            Assert.That(boardB.Get(target), Is.EqualTo(2), "Machine B's board is untouched by A's runs.");
        });
    }

    [Test]
    public async Task rebinding_a_scope_replaces_the_board_for_the_next_run()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(target, 42);
                return ResultHelpers.Success;
            })
            .Build();

        Blackboard first = new(enemySchema);
        Blackboard second = new(enemySchema);

        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(first);
        await machine.ExecuteAsync();

        machine.SetBlackboard(second); // pool-recycle: same machine, new entity board
        await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Get(target), Is.EqualTo(42));
            Assert.That(second.Get(target), Is.EqualTo(42));
        });
    }

    // ── Declarations and validation ───────────────────────────────────────

    [Test]
    public void schema_mismatch_fails_fast_at_bind_time()
    {
        BlackboardSchema declared = NewEnemySchema(out _, out _);
        BlackboardSchema other = NewEnemySchema(out _, out _);
        BlackboardSchema world = NewWorldSchema(out _, out _);
        BlackboardSchema otherWorld = NewWorldSchema(out _, out _);

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) => ResultHelpers.Success)
            .WithSchema(declared)
            .WithSchema(world)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        Assert.Multiple(() =>
        {
            Assert.That(() => machine.WithBlackboard(new Blackboard(other)),
                Throws.InvalidOperationException.With.Message.Contain("does not match"),
                "Graph-scope board over a different schema must be rejected.");
            Assert.That(() => machine.WithBlackboard(new Blackboard(otherWorld)),
                Throws.InvalidOperationException.With.Message.Contain("does not match"),
                "Global-scope board over a different schema must be rejected.");
            Assert.That(() => machine.WithBlackboard(new Blackboard(declared)), Throws.Nothing);
            Assert.That(() => machine.WithBlackboard(new Blackboard(world)), Throws.Nothing);
        });
    }

    [Test]
    public void graphs_without_declarations_bind_permissively()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out _, out _);
        BlackboardSchema world = NewWorldSchema(out _, out _);

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) => ResultHelpers.Success)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Schema, Is.Null);
            Assert.That(graph.GlobalSchema, Is.Null);
            Assert.That(() => graph.ToAsyncStateMachine()
                    .WithBlackboard(new Blackboard(enemySchema))
                    .WithBlackboard(new Blackboard(world)),
                Throws.Nothing);
        });
    }

    [Test]
    public void declaring_the_same_scope_twice_throws()
    {
        BlackboardSchema first = NewEnemySchema(out _, out _);
        BlackboardSchema second = NewEnemySchema(out _, out _);

        Assert.That(() => GraphBuilder
                .StartWithAsync((bb, _) => ResultHelpers.Success)
                .WithSchema(first)
                .WithSchema(second),
            Throws.InvalidOperationException.With.Message.Contain("already been declared"));
    }

    [Test]
    public async Task agentless_machine_uses_blackboards_only()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out BlackboardKey<int> speed);

        Graph graph = GraphBuilder
            .StartWith(bb =>
            {
                bb.Set(target, bb.Get(speed) * 3);
                return Result.Success;
            })
            .WithSchema(enemySchema)
            .Build();

        Blackboard board = new(enemySchema);
        board.Set(speed, 5);

        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(board);
        await machine.ExecuteAsync();

        Assert.That(board.Get(target), Is.EqualTo(15));
    }

    [Test]
    public void unbound_scope_key_access_throws_mid_run_with_guidance()
    {
        BlackboardSchema world = NewWorldSchema(out BlackboardKey<bool> alarm, out _);
        _ = world;

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(alarm, true); // global scope — never bound below
                return ResultHelpers.Success;
            })
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        InvalidOperationException? ex =
            Assert.ThrowsAsync<InvalidOperationException>(async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("no global blackboard bound").And.Contain("WithBlackboard"));
    }

    // ── Composite propagation ─────────────────────────────────────────────

    [Test]
    public async Task subgraph_nodes_receive_the_parent_context()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph child = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(target, 7);
                return ResultHelpers.Success;
            })
            .Build();

        Graph parent = GraphBuilder
            .StartWithAsync((_, _) => ResultHelpers.Success)
            .SubGraph(child)
            .Build();

        Blackboard board = new(enemySchema);
        AsyncStateMachine machine = parent.ToAsyncStateMachine().WithBlackboard(board);
        await machine.ExecuteAsync();

        Assert.That(board.Get(target), Is.EqualTo(7),
            "Graph.SetBlackboards must reach nodes inside nested machines.");
    }

    private sealed class CustomComposite(Graph child) : IAsyncLogic, ISubGraphProvider
    {
        private readonly AsyncStateMachine _machine = new(child);

        public IEnumerable<Graph> SubGraphs => [_machine.Graph];

        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => _machine.ExecuteAsync(ct);
    }

    [Test]
    public async Task user_defined_composite_receives_the_context_via_subgraph_provider()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph inner = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(target, 11);
                return ResultHelpers.Success;
            })
            .Build();

        Graph outer = GraphBuilder
            .StartWithAsync(new CustomComposite(inner))
            .Build();

        Blackboard board = new(enemySchema);
        await outer.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.That(board.Get(target), Is.EqualTo(11));
    }

    [Test]
    public async Task parallel_regions_share_the_parent_context()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out BlackboardKey<int> speed);

        Graph regionA = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(target, 1);
                return ResultHelpers.Success;
            })
            .Build();

        Graph regionB = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(speed, 2);
                return ResultHelpers.Success;
            })
            .Build();

        Graph parent = GraphBuilder.Start().Parallel(regionA, regionB).Build();

        Blackboard board = new(enemySchema);
        await parent.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(board.Get(target), Is.EqualTo(1));
            Assert.That(board.Get(speed), Is.EqualTo(2));
        });
    }

    [Test]
    public void nested_machine_with_conflicting_schema_fails_loudly_at_stamp_time()
    {
        BlackboardSchema parentSchema = NewEnemySchema(out _, out _);
        BlackboardSchema childSchema = NewEnemySchema(out _, out _);

        Graph child = GraphBuilder
            .StartWithAsync((bb, _) => ResultHelpers.Success)
            .WithSchema(childSchema)
            .Build();

        Graph parent = GraphBuilder
            .StartWithAsync((_, _) => ResultHelpers.Success)
            .SubGraph(child)
            .WithSchema(parentSchema)
            .Build();

        AsyncStateMachine machine = parent.ToAsyncStateMachine();

        Assert.That(() => machine.WithBlackboard(new Blackboard(parentSchema)),
            Throws.InvalidOperationException.With.Message.Contain("does not match"),
            "The nested machine must reject a context whose board conflicts with its own declaration.");
    }

    // ── Blackboard-driven directors ───────────────────────────────────────

    [Test]
    public async Task async_if_branches_on_the_routed_context()
    {
        BlackboardSchema world = NewWorldSchema(out BlackboardKey<bool> alarm, out _);
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out _);

        Graph graph = GraphBuilder
            .StartWithAsync((_, _) => ResultHelpers.Success)
            .If(bb => bb.Get(alarm))
            .ThenAsync((bb, _) =>
            {
                bb.Set(target, 100);
                return ResultHelpers.Success;
            })
            .ElseAsync((bb, _) =>
            {
                bb.Set(target, -100);
                return ResultHelpers.Success;
            })
            .Build();

        Blackboard worldBb = new(world);
        Blackboard board = new(enemySchema);
        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(worldBb).WithBlackboard(board);

        await machine.ExecuteAsync();
        Assert.That(board.Get(target), Is.EqualTo(-100), "Alarm off — else branch.");

        worldBb.Set(alarm, true);
        await machine.ExecuteAsync();
        Assert.That(board.Get(target), Is.EqualTo(100), "Alarm on — then branch.");
    }

    [Test]
    public void sync_switch_selects_on_the_routed_context()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out BlackboardKey<int> target, out BlackboardKey<int> speed);

        Graph graph = GraphBuilder
            .StartWith(bb => Result.Success)
            .Switch(bb => bb.Get(speed))
            .Case(1, bb =>
            {
                bb.Set(target, 10);
                return Result.Success;
            })
            .Case(2, bb =>
            {
                bb.Set(target, 20);
                return Result.Success;
            })
            .Default(bb =>
            {
                bb.Set(target, 0);
                return Result.Success;
            })
            .End()
            .Build();

        Blackboard board = new(enemySchema);
        board.Set(speed, 2);

        StateMachine machine = graph.ToStateMachine().WithBlackboard(board);
        RunToCompletion(machine);

        Assert.That(board.Get(target), Is.EqualTo(20));
    }

    // ── Validator diagnostics ─────────────────────────────────────────────

    [Test]
    public void validator_reports_declared_schema_with_no_settable_nodes()
    {
        BlackboardSchema enemySchema = NewEnemySchema(out _, out _);

        // Raw IAsyncLogic — not a State, accepts nothing.
        Graph graph = GraphBuilder
            .StartWithAsync(new RawLogic())
            .WithSchema(enemySchema)
            .Build();

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Info && d.Message.Contains("IBlackboardSettable")),
            Is.True, "Expected an Info diagnostic about unused schema declarations.");
    }

    [Test]
    public void validator_warns_on_conflicting_child_schema()
    {
        BlackboardSchema parentSchema = NewEnemySchema(out _, out _);
        BlackboardSchema childSchema = NewEnemySchema(out _, out _);

        Graph child = GraphBuilder
            .StartWithAsync((bb, _) => ResultHelpers.Success)
            .WithSchema(childSchema)
            .Build();

        Graph parent = GraphBuilder
            .StartWithAsync((_, _) => ResultHelpers.Success)
            .SubGraph(child)
            .WithSchema(parentSchema)
            .Build();

        GraphValidationResult result = parent.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Message.Contains("different Graph-scoped blackboard schema")),
            Is.True, "Expected a Warning about the conflicting child schema.");
    }

    private sealed class RawLogic : IAsyncLogic
    {
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => ResultHelpers.Success;
    }

    private static void RunToCompletion(StateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }
    }
}
