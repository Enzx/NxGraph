using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Asserting allocation gate for the two run loops. Unlike the BenchmarkDotNet report
/// (which is human-read), these tests fail CI when a change allocates on the hot path.
/// Every hot-path feature should add a case here exercising it end to end.
/// <para>
/// Measurements use <see cref="GC.GetAllocatedBytesForCurrentThread"/>; all node logic
/// completes synchronously, so awaits never leave the measuring thread. The gate only
/// holds for Release builds (Debug async methods heap-allocate their state machines),
/// so the fixture self-ignores in Debug. That means the 0 B guarantee rides entirely on
/// the CI Release leg: NxGraph.Build's <c>ci</c> target defaults CONFIGURATION=Release,
/// so the gate does run in CI today — keep that true if the CI configuration ever changes.
/// </para>
/// <para>
/// JIT behavior is pinned for this test project (<c>TieredCompilation=false</c> and
/// <c>TieredPgo=false</c> in NxGraph.Tests.csproj), so every method compiles straight to
/// its final optimized form and no background re-tier / OSR transition can land inside a
/// measurement window. Pinning was chosen over raising the warmup count because tier-up
/// completes asynchronously — no fixed warmup count can guarantee it has finished on a
/// loaded runner, which is exactly the nondeterminism this gate must not have.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class AllocationGateTests
{
    private const int WarmupRuns = 50;
    private const int Iterations = 200;

    private sealed class NoopAsyncObserver : IAsyncStateMachineObserver
    {
        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    private sealed class NoopSyncObserver : IStateMachineObserver;

    [SetUp]
    public void RequireReleaseBuild()
    {
#if DEBUG
        Assert.Ignore("The allocation gate is only meaningful for Release builds.");
#endif
    }

    // All cases funnel through the two delegate cores below so the warmup/iterate/measure
    // protocol exists exactly once; per-shape entry points only adapt the run action.

    private static Task AssertZeroAllocAsync(AsyncStateMachine machine)
    {
        return AssertZeroAllocAsync(async () => { await machine.ExecuteAsync(); }, "Async hot path");
    }

    private static async Task AssertZeroAllocAsync(Func<ValueTask> runOnce, string label)
    {
        for (int i = 0; i < WarmupRuns; i++)
        {
            await runOnce();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            await runOnce();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            $"{label} allocated {allocated} B over {Iterations} runs.");
    }

    private static void AssertZeroAlloc(StateMachine machine)
    {
        AssertZeroAlloc(() => RunToCompletion(machine), "Sync hot path");
    }

    private static void AssertZeroAlloc(Action runOnce, string label)
    {
        for (int i = 0; i < WarmupRuns; i++)
        {
            runOnce();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            runOnce();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            $"{label} allocated {allocated} B over {Iterations} runs.");
    }

    private static void RunToCompletion(StateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }
    }

    private static StateToken AsyncChain(int length)
    {
        StateToken token = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success);
        for (int i = 1; i < length; i++)
        {
            token = token.ToAsync(_ => ResultHelpers.Success);
        }

        return token;
    }

    [Test]
    public async Task async_single_node_is_allocation_free()
    {
        await AssertZeroAllocAsync(AsyncChain(1).ToAsyncStateMachine());
    }

    [Test]
    public async Task async_chain_of_10_is_allocation_free()
    {
        await AssertZeroAllocAsync(AsyncChain(10).ToAsyncStateMachine());
    }

    [Test]
    public async Task async_chain_with_observer_is_allocation_free()
    {
        await AssertZeroAllocAsync(AsyncChain(10).ToAsyncStateMachine(new NoopAsyncObserver()));
    }

    [Test]
    public async Task async_goto_loop_is_allocation_free()
    {
        int laps = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Top")
            .ToAsync(_ => ++laps % 3 != 0 ? ResultHelpers.Success : ResultHelpers.Failure)
            .Goto("Top")
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_failure_routing_is_allocation_free()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .OnErrorAsync(_ => ResultHelpers.Success)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_no_backoff_retry_is_allocation_free()
    {
        int calls = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++calls % 3 == 0 ? ResultHelpers.Success : ResultHelpers.Failure)
            .Retry(maxAttempts: 3)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_run_over_uid_bearing_graph_is_allocation_free()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).WithUid(Guid.NewGuid())
            .ToAsync(_ => ResultHelpers.Success).WithUid(Guid.NewGuid())
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public void sync_run_over_uid_bearing_graph_is_allocation_free()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).WithUid(Guid.NewGuid())
            .To(() => Result.Success).WithUid(Guid.NewGuid())
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public async Task async_stepped_execution_is_allocation_free()
    {
        AsyncStateMachine machine = AsyncChain(10).ToAsyncStateMachine();

        await AssertZeroAllocAsync(async () =>
        {
            while (await machine.StepAsync() == Result.InProgress)
            {
            }
        }, "Stepped execution");
    }

    [Test]
    public async Task async_entry_exit_actions_are_allocation_free()
    {
        int counter = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .OnEnter(() => counter++)
            .OnExit(() => counter++)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_subgraph_composite_is_allocation_free()
    {
        Graph child = AsyncChain(3).Build();
        Graph parent = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .SubGraph(child)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        await AssertZeroAllocAsync(parent.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_history_composite_happy_path_is_allocation_free()
    {
        Graph child = AsyncChain(3).Build();
        Graph parent = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .SubGraph(child, history: true)
            .Build();

        await AssertZeroAllocAsync(parent.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_parallel_regions_are_allocation_free()
    {
        Graph parent = GraphBuilder
            .Start()
            .Parallel(AsyncChain(3).Build(), AsyncChain(2).Build())
            .Build();

        await AssertZeroAllocAsync(parent.ToAsyncStateMachine());
    }

    private static StateToken SyncChain(int length)
    {
        StateToken token = GraphBuilder.StartWith(() => Result.Success);
        for (int i = 1; i < length; i++)
        {
            token = token.To(() => Result.Success);
        }

        return token;
    }

    [Test]
    public void sync_parallel_regions_run_to_join_are_allocation_free()
    {
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, SyncChain(3).Build(), SyncChain(2).Build())
            .Build();

        AssertZeroAlloc(parent.ToStateMachine());
    }

    [Test]
    public void sync_parallel_regions_round_per_tick_are_allocation_free()
    {
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, SyncChain(3).Build(), SyncChain(2).Build())
            .Build();

        AssertZeroAlloc(parent.ToStateMachine());
    }

    [Test]
    public async Task async_dynamic_parallel_regions_are_allocation_free()
    {
        (Blackboard world, _, BlackboardKey<bool> alarm, _, _) = DualScopeBoards();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(
                bb => bb.Get(alarm)
                    ? RegionMask.Bit(0) | RegionMask.Bit(1)
                    : RegionMask.Bit(0) | RegionMask.Bit(2),
                AsyncChain(3).Build(), AsyncChain(2).Build(), AsyncChain(2).Build())
            .Build();

        await AssertZeroAllocAsync(parent.ToAsyncStateMachine().WithBlackboard(world));
    }

    [Test]
    public void sync_dynamic_parallel_run_to_join_is_allocation_free()
    {
        (Blackboard world, _, BlackboardKey<bool> alarm, _, _) = DualScopeBoards();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin,
                bb => bb.Get(alarm)
                    ? RegionMask.Bit(0) | RegionMask.Bit(1)
                    : RegionMask.Bit(0) | RegionMask.Bit(2),
                SyncChain(3).Build(), SyncChain(2).Build(), SyncChain(2).Build())
            .Build();

        AssertZeroAlloc(parent.ToStateMachine().WithBlackboard(world));
    }

    [Test]
    public void sync_dynamic_parallel_round_per_tick_is_allocation_free()
    {
        (Blackboard world, _, BlackboardKey<bool> alarm, _, _) = DualScopeBoards();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick,
                bb => bb.Get(alarm)
                    ? RegionMask.Bit(0) | RegionMask.Bit(1)
                    : RegionMask.Bit(0) | RegionMask.Bit(2),
                SyncChain(3).Build(), SyncChain(2).Build(), SyncChain(2).Build())
            .Build();

        AssertZeroAlloc(parent.ToStateMachine().WithBlackboard(world));
    }

    // ── Blackboards: dual-scope routing on the hot path ─────────────────

    private sealed class GateAgent
    {
        public int Runs;
    }

    private static (Blackboard world, Blackboard local,
        BlackboardKey<bool> alarm, BlackboardKey<int> sightings, BlackboardKey<int> speed)
        DualScopeBoards()
    {
        BlackboardSchema worldSchema = new("world", BlackboardScope.Global);
        BlackboardKey<bool> alarm = worldSchema.Register<bool>("alarm");
        BlackboardKey<int> sightings = worldSchema.Register<int>("sightings");

        BlackboardSchema enemySchema = new("enemy");
        BlackboardKey<int> speed = enemySchema.Register<int>("speed", 1);

        return (new Blackboard(worldSchema), new Blackboard(enemySchema), alarm, sightings, speed);
    }

    [Test]
    public async Task async_dual_scope_blackboard_produce_consume_is_allocation_free()
    {
        (Blackboard world, Blackboard local,
            BlackboardKey<bool> alarm, BlackboardKey<int> sightings, BlackboardKey<int> speed) = DualScopeBoards();

        Graph graph = GraphBuilder
            .StartWithAsync(new AsyncRelayState<GateAgent>((agent, bb, _) =>
            {
                agent.Runs++;
                bb.Set(speed, bb.Get(alarm) ? 5 : 2); // global read → graph write
                return ResultHelpers.Success;
            }))
            .ToAsync<GateAgent>((agent, bb, _) =>
            {
                if (bb.Get(speed) > agent.Runs)
                {
                    bb.GetRef(sightings)++; // graph read → global write
                }

                return ResultHelpers.Success;
            })
            .Build();

        AsyncStateMachine<GateAgent> machine = graph.ToAsyncStateMachine<GateAgent>()
            .WithBlackboard(world)
            .WithBlackboard(local)
            .WithAgent(new GateAgent());

        await AssertZeroAllocAsync(machine);
    }

    [Test]
    public void sync_dual_scope_blackboard_produce_consume_is_allocation_free()
    {
        (Blackboard world, Blackboard local,
            BlackboardKey<bool> alarm, BlackboardKey<int> sightings, BlackboardKey<int> speed) = DualScopeBoards();

        Graph graph = GraphBuilder
            .StartWith(new RelayState<GateAgent>((agent, bb) =>
            {
                agent.Runs++;
                bb.Set(speed, bb.Get(alarm) ? 5 : 2);
                return Result.Success;
            }))
            .To<GateAgent>((agent, bb) =>
            {
                if (bb.Get(speed) > agent.Runs)
                {
                    bb.GetRef(sightings)++;
                }

                return Result.Success;
            })
            .Build();

        StateMachine<GateAgent> machine = graph.ToStateMachine<GateAgent>()
            .WithBlackboard(world)
            .WithBlackboard(local)
            .WithAgent(new GateAgent());

        AssertZeroAlloc(machine);
    }

    // ── Step I/O ports: produce → pipe → consume over one Graph board ───

    private static (Blackboard io, BlackboardKey<int> raw, BlackboardKey<int> doubled) PortBoards()
    {
        BlackboardSchema schema = new("flow-io");
        BlackboardKey<int> raw = schema.Register<int>("raw");
        BlackboardKey<int> doubled = schema.Register<int>("doubled");
        return (new Blackboard(schema), raw, doubled);
    }

    [Test]
    public async Task async_port_produce_pipe_consume_is_allocation_free()
    {
        (Blackboard io, BlackboardKey<int> raw, BlackboardKey<int> doubled) = PortBoards();

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(bb.Get(raw) + 1))
            .ToAsync(raw, doubled, (value, _, _) => new ValueTask<int>(value * 2))
            .ToAsync(doubled, (value, _, _) => value > 0 ? ResultHelpers.Success : ResultHelpers.Failure)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine().WithBlackboard(io));
    }

    [Test]
    public void sync_port_produce_pipe_consume_is_allocation_free()
    {
        (Blackboard io, BlackboardKey<int> raw, BlackboardKey<int> doubled) = PortBoards();

        Graph graph = GraphBuilder
            .Start()
            .To(raw, bb => bb.Get(raw) + 1)
            .To(raw, doubled, (value, _) => value * 2)
            .To(doubled, (value, _) => value > 0 ? Result.Success : Result.Failure)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine().WithBlackboard(io));
    }

    [Test]
    public async Task async_blackboard_if_branch_is_allocation_free()
    {
        (Blackboard world, Blackboard local,
            BlackboardKey<bool> alarm, BlackboardKey<int> _, BlackboardKey<int> speed) = DualScopeBoards();

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(alarm, !bb.Get(alarm)); // exercise both branches over the run set
                return ResultHelpers.Success;
            })
            .If(bb => bb.Get(alarm))
            .ThenAsync((bb, _) =>
            {
                bb.Set(speed, 5);
                return ResultHelpers.Success;
            })
            .ElseAsync((bb, _) =>
            {
                bb.Set(speed, 2);
                return ResultHelpers.Success;
            })
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine()
            .WithBlackboard(world)
            .WithBlackboard(local);

        await AssertZeroAllocAsync(machine);
    }

    [Test]
    public void sync_blackboard_if_branch_is_allocation_free()
    {
        (Blackboard world, Blackboard local,
            BlackboardKey<bool> alarm, BlackboardKey<int> _, BlackboardKey<int> speed) = DualScopeBoards();

        Graph graph = GraphBuilder
            .StartWith(bb =>
            {
                bb.Set(alarm, !bb.Get(alarm));
                return Result.Success;
            })
            .If(bb => bb.Get(alarm))
            .Then(bb =>
            {
                bb.Set(speed, 5);
                return Result.Success;
            })
            .Else(bb =>
            {
                bb.Set(speed, 2);
                return Result.Success;
            })
            .To(bb => Result.Success)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine()
            .WithBlackboard(world)
            .WithBlackboard(local));
    }

    // ── Directors: Switch dispatch and non-blackboard Choice ────────────

    [Test]
    public async Task async_switch_dispatch_is_allocation_free()
    {
        int lap = 0;
        Graph graph = GraphBuilder
            .Start()
            .Switch(() => ++lap % 3) // rotate: case 1, case 2, default
            .CaseAsync(1, _ => ResultHelpers.Success)
            .CaseAsync(2, _ => ResultHelpers.Success)
            .DefaultAsync(_ => ResultHelpers.Success)
            .End()
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public void sync_switch_dispatch_is_allocation_free()
    {
        int lap = 0;
        Graph graph = GraphBuilder
            .Start()
            .Switch(() => ++lap % 3) // rotate: case 1, case 2, default
            .Case(1, () => Result.Success)
            .Case(2, () => Result.Success)
            .Default(() => Result.Success)
            .End()
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public async Task async_choice_without_blackboard_is_allocation_free()
    {
        bool flag = false;
        Graph graph = GraphBuilder
            .Start()
            .If(() => flag = !flag) // exercise both branches over the run set
            .ThenAsync(_ => ResultHelpers.Success)
            .ElseAsync(_ => ResultHelpers.Success)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public void sync_choice_without_blackboard_is_allocation_free()
    {
        bool flag = false;
        Graph graph = GraphBuilder
            .Start()
            .If(() => flag = !flag) // exercise both branches over the run set
            .Then(() => Result.Success)
            .Else(() => Result.Success)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    // ── Blackboards: Node scope (per-transition reset included) ─────────

    private static Graph NodeScopeChain(bool sync)
    {
        BlackboardSchema schema = new("scratch", BlackboardScope.Node);
        BlackboardKey<int> value = schema.Register<int>("value");

        if (sync)
        {
            return GraphBuilder
                .StartWith(bb =>
                {
                    bb.Set(value, bb.Get(value) + 3); // always starts from the default
                    return Result.Success;
                })
                .To(bb => bb.Get(value) == 0 ? Result.Success : Result.Success) // reset consumed
                .WithSchema(schema)
                .Build();
        }

        return GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(value, bb.Get(value) + 3);
                return ResultHelpers.Success;
            })
            .ToAsync((bb, _) => bb.Get(value) == 0 ? ResultHelpers.Success : ResultHelpers.Success)
            .WithSchema(schema)
            .Build();
    }

    [Test]
    public async Task async_node_scope_produce_consume_is_allocation_free()
    {
        await AssertZeroAllocAsync(NodeScopeChain(sync: false).ToAsyncStateMachine());
    }

    [Test]
    public void sync_node_scope_produce_consume_is_allocation_free()
    {
        AssertZeroAlloc(NodeScopeChain(sync: true).ToStateMachine());
    }

    // ── Sync twins of the async gate cases ──────────────────────────────

    [Test]
    public void sync_failure_routing_is_allocation_free()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Failure)
            .OnError(() => Result.Success)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public void sync_no_backoff_retry_is_allocation_free()
    {
        int calls = 0;
        Graph graph = GraphBuilder
            .StartWith(() => ++calls % 3 == 0 ? Result.Success : Result.Failure)
            .Retry(maxAttempts: 3)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public void sync_goto_loop_is_allocation_free()
    {
        int laps = 0;
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("Top")
            .To(() => ++laps % 3 != 0 ? Result.Success : Result.Failure)
            .Goto("Top")
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public void sync_entry_exit_actions_are_allocation_free()
    {
        int counter = 0;
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .OnEnter(() => counter++)
            .OnExit(() => counter++)
            .To(() => Result.Success)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public void sync_subgraph_run_to_join_is_allocation_free()
    {
        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(ParallelStepMode.RunToJoin, SyncChain(3).Build())
            .To(() => Result.Success)
            .Build();

        AssertZeroAlloc(parent.ToStateMachine());
    }

    [Test]
    public void sync_subgraph_round_per_tick_is_allocation_free()
    {
        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(ParallelStepMode.RoundPerTick, SyncChain(3).Build())
            .To(() => Result.Success)
            .Build();

        AssertZeroAlloc(parent.ToStateMachine());
    }

    [Test]
    public void sync_history_composite_happy_path_is_allocation_free()
    {
        Graph parent = GraphBuilder
            .StartWith(() => Result.Success)
            .SubGraph(ParallelStepMode.RunToJoin, SyncChain(3).Build(), history: true)
            .Build();

        AssertZeroAlloc(parent.ToStateMachine());
    }

    [Test]
    public void sync_wait_for_ticking_is_allocation_free()
    {
        // Deterministic fake clock: every read advances one second, so a 1.5 s wait costs
        // exactly two ticks per run — the gate covers the record/check path both ways.
        long now = 0;
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .To(new WaitState(TimeSpan.FromSeconds(1.5),
                () => now += System.Diagnostics.Stopwatch.Frequency))
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public void sync_to_with_timeout_happy_path_is_allocation_free()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .ToWithTimeout(TimeSpan.FromSeconds(5), () => Result.Success)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public async Task async_to_with_timeout_happy_path_is_allocation_free()
    {
        // Inner completes before the deadline; the wrapper's pooled CTS (rent → CancelAfter →
        // TryReset → return) must keep the happy path at 0 B steady-state.
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromSeconds(5), _ => ResultHelpers.Success)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public void sync_to_all_join_is_allocation_free()
    {
        // The async AsyncAllState allocates per execution by design (task materialization
        // dwarfed by the I/O it overlaps) — exemption recorded in spec 012, no async case.
        Graph graph = GraphBuilder
            .Start()
            .ToAll(
                _ => Result.Success,
                _ => Result.Success,
                _ => Result.Success)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine());
    }

    [Test]
    public void sync_chain_of_10_is_allocation_free()
    {
        StateToken token = GraphBuilder.StartWith(() => Result.Success);
        for (int i = 0; i < 9; i++)
        {
            token = token.To(() => Result.Success);
        }

        AssertZeroAlloc(token.ToStateMachine());
    }

    [Test]
    public void sync_chain_with_observer_is_allocation_free()
    {
        StateToken token = GraphBuilder.StartWith(() => Result.Success);
        for (int i = 0; i < 9; i++)
        {
            token = token.To(() => Result.Success);
        }

        AssertZeroAlloc(token.ToStateMachine(new NoopSyncObserver()));
    }

    // ── Cross-runtime log-report bridge: State.Log under the async machine ──

    /// <summary>A sync state that calls Log every run — the bridge's hot path.</summary>
    private sealed class GateLoggingState : State
    {
        protected override Result OnRun()
        {
            Log("gate-log");
            return Result.Success;
        }
    }

    [Test]
    public async Task async_sync_state_log_without_observer_is_allocation_free()
    {
        // Observer-less async machine: both report slots are wired null, so Log must cost
        // exactly its two null checks.
        Graph graph = GraphBuilder
            .StartWith(new GateLoggingState())
            .To(() => Result.Success)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine());
    }

    [Test]
    public async Task async_sync_state_log_with_observer_is_allocation_free()
    {
        // Bridged delivery: Log falls back to the machine-wired async callback and waits it
        // out on the completed-successfully fast path — no task materialization.
        Graph graph = GraphBuilder
            .StartWith(new GateLoggingState())
            .To(() => Result.Success)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine(new NoopAsyncObserver()));
    }

    // ── Event entry points (spec 013): typed raise + run ────────────────

    private readonly record struct GatePing(int Value);

    private static (Graph graph, Blackboard board, BlackboardKey<GatePing> ping) EventGraph(bool sync)
    {
        BlackboardSchema schema = new("gate-events");
        BlackboardKey<GatePing> ping = schema.Register<GatePing>("ping");

        Graph graph = sync
            ? GraphBuilder.StartWithEvents()
                .On(ping, e => e
                    .To(bb => bb.Get(ping).Value >= 0 ? Result.Success : Result.Failure)
                    .To(ping, (evt, _) => evt.Value >= 0 ? Result.Success : Result.Failure))
                .WithSchema(schema)
                .Build()
            : GraphBuilder.StartWithEvents()
                .On(ping, e => e
                    .ToAsync((bb, _) => bb.Get(ping).Value >= 0 ? ResultHelpers.Success : ResultHelpers.Failure)
                    .ToAsync(ping, (evt, _, _) => evt.Value >= 0 ? ResultHelpers.Success : ResultHelpers.Failure))
                .WithSchema(schema)
                .Build();

        return (graph, new Blackboard(schema), ping);
    }

    [Test]
    public async Task async_typed_event_raise_and_run_is_allocation_free()
    {
        (Graph graph, Blackboard board, BlackboardKey<GatePing> _) = EventGraph(sync: false);
        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(board);

        int payload = 0;
        await AssertZeroAllocAsync(async () => { await machine.ExecuteAsync(new GatePing(payload++)); },
            "Typed event raise");
    }

    [Test]
    public void sync_typed_event_raise_and_run_is_allocation_free()
    {
        (Graph graph, Blackboard board, BlackboardKey<GatePing> _) = EventGraph(sync: true);
        StateMachine machine = graph.ToStateMachine().WithBlackboard(board);

        int payload = 0;
        AssertZeroAlloc(() => RaiseToCompletion(machine, new GatePing(payload++)),
            "Sync typed event raise");
    }

    private static void RaiseToCompletion<TEvent>(StateMachine machine, TEvent evt)
    {
        Result result = machine.Execute(evt);
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }
    }

    // ── Token runtime (spec 007): pooled tokens, fork/join, per-token scratch ──

    private static void AssertZeroAllocTokens(NxGraph.Tokens.TokenMachine machine)
    {
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        AssertZeroAlloc(() => machine.Execute(), "Sync token hot path");
    }

    private static Task AssertZeroAllocTokensAsync(NxGraph.Tokens.AsyncTokenMachine machine)
    {
        return AssertZeroAllocAsync(async () => { await machine.ExecuteAsync(); }, "Async token hot path");
    }

    /// <summary>load → fork(a, b) → join(All 2) → finish, sync logic throughout.</summary>
    private static Graph TokenDiamond()
    {
        NxGraph.Tokens.JoinState join = new(NxGraph.Tokens.JoinPolicy.All(2));
        return GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(join).To(() => Result.Success),
                b => b.To(() => Result.Success).To(join))
            .Build();
    }

    [Test]
    public void sync_token_fork_join_diamond_is_allocation_free()
    {
        AssertZeroAllocTokens(TokenDiamond().ToTokenMachine());
    }

    [Test]
    public async Task async_token_fork_join_diamond_is_allocation_free()
    {
        await AssertZeroAllocTokensAsync(TokenDiamond().ToAsyncTokenMachine());
    }

    [Test]
    public void sync_token_any_merge_is_allocation_free()
    {
        AssertZeroAllocTokens(TokenAnyMerge().ToTokenMachine());
    }

    [Test]
    public async Task async_token_any_merge_is_allocation_free()
    {
        await AssertZeroAllocTokensAsync(TokenAnyMerge().ToAsyncTokenMachine());
    }

    /// <summary>load → fork(a, b) → merge(Any) with a shared tail, sync logic throughout.</summary>
    private static Graph TokenAnyMerge()
    {
        NxGraph.Tokens.JoinState merge = new(NxGraph.Tokens.JoinPolicy.Any);
        return GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(merge).To(() => Result.Success),
                b => b.To(() => Result.Success).To(merge))
            .Build();
    }

    [Test]
    public void sync_token_retry_without_backoff_is_allocation_free()
    {
        int calls = 0;
        AssertZeroAllocTokens(TokenRetryDiamond(() => ++calls % 2 == 0).ToTokenMachine());
    }

    [Test]
    public async Task async_token_retry_without_backoff_is_allocation_free()
    {
        // No backoff on purpose: a zero backoff never reaches Task.Delay, so the async
        // per-token retry loop must stay 0 B like the sync one.
        int calls = 0;
        await AssertZeroAllocTokensAsync(TokenRetryDiamond(() => ++calls % 2 == 0).ToAsyncTokenMachine());
    }

    /// <summary>Fork with one flaky branch retried in place (no backoff) joining an All(2).</summary>
    private static Graph TokenRetryDiamond(Func<bool> succeeds)
    {
        NxGraph.Tokens.JoinState join = new(NxGraph.Tokens.JoinPolicy.All(2));
        return GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => succeeds() ? Result.Success : Result.Failure)
                    .Retry(2)
                    .To(join),
                b => b.To(() => Result.Success).To(join))
            .Build();
    }

    [Test]
    public void sync_token_node_scope_scratch_is_allocation_free()
    {
        AssertZeroAllocTokens(TokenNodeScopeScratch().ToTokenMachine());
    }

    [Test]
    public async Task async_token_node_scope_scratch_is_allocation_free()
    {
        await AssertZeroAllocTokensAsync(TokenNodeScopeScratch().ToAsyncTokenMachine());
    }

    /// <summary>Fork whose branches write per-token Node-scoped scratch before an All(2) join.</summary>
    private static Graph TokenNodeScopeScratch()
    {
        BlackboardSchema nodeSchema = new("token-scratch", BlackboardScope.Node);
        BlackboardKey<int> counter = nodeSchema.Register("counter", 0);

        NxGraph.Tokens.JoinState join = new(NxGraph.Tokens.JoinPolicy.All(2));
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        return start.ForkTo(
                b => b.To(bb =>
                {
                    bb.Set(counter, bb.Get(counter) + 1);
                    return Result.Success;
                }).To(join).To(() => Result.Success),
                b => b.To(bb =>
                {
                    bb.Set(counter, bb.Get(counter) + 1);
                    return Result.Success;
                }).To(join))
            .Builder.WithSchema(nodeSchema).Build();
    }

    [Test]
    public async Task async_token_with_observer_is_allocation_free()
    {
        await AssertZeroAllocTokensAsync(TokenDiamond().ToAsyncTokenMachine(new NoopAsyncTokenObserver()));
    }

    [Test]
    public void sync_token_with_observer_is_allocation_free()
    {
        AssertZeroAllocTokens(TokenDiamond().ToTokenMachine(new NoopSyncTokenObserver()));
    }

    // ── Behaviors: sequence run loop + binding resolution (spec 014) ────
    //
    // A behavior node with a key-bound Log (observer-less, so no string is ever formatted)
    // and a SetValue must execute at 0 B steady-state: the run loop is an array walk over
    // one stack context, and binding resolution is a branch plus a typed Get.

    private static (Behaviors.Log log, Behaviors.SetValue<int> set, Blackboard board) BehaviorFixture()
    {
        BlackboardSchema schema = new("behaviors");
        BlackboardKey<string> message = schema.Register("message", "steady");
        BlackboardKey<int> source = schema.Register("source", 7);
        BlackboardKey<int> target = schema.Register<int>("target");

        Behaviors.Log log = new(Behaviors.LogSeverity.Info, message);
        Behaviors.SetValue<int> set = new(target, source);
        return (log, set, new Blackboard(schema));
    }

    [Test]
    public async Task async_behavior_node_is_allocation_free()
    {
        (Behaviors.Log log, Behaviors.SetValue<int> set, Blackboard board) = BehaviorFixture();
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(log, set)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine().WithBlackboard(board));
    }

    [Test]
    public void sync_behavior_node_is_allocation_free()
    {
        (Behaviors.Log log, Behaviors.SetValue<int> set, Blackboard board) = BehaviorFixture();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(log, set)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine().WithBlackboard(board));
    }

    // ── Repeat: bounded sub-node iteration (spec 015) ───────────────────
    //
    // A Repeat with a key-bound count and an index key over a SetValue body must execute at
    // 0 B steady-state: count resolution is a branch plus a typed Get (once at entry), the
    // index write is a typed Set, and iteration is an array walk.

    private static (Behaviors.Repeat repeat, Blackboard board) RepeatFixture()
    {
        BlackboardSchema schema = new("repeat");
        BlackboardKey<int> trips = schema.Register("trips", 3);
        BlackboardKey<int> index = schema.Register("i", 0);
        BlackboardKey<int> source = schema.Register("source", 7);
        BlackboardKey<int> target = schema.Register<int>("target");

        Behaviors.Repeat repeat = new(trips, index, new Behaviors.SetValue<int>(target, source));
        return (repeat, new Blackboard(schema));
    }

    [Test]
    public async Task async_repeat_behavior_node_is_allocation_free()
    {
        (Behaviors.Repeat repeat, Blackboard board) = RepeatFixture();
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(repeat)
            .Build();

        await AssertZeroAllocAsync(graph.ToAsyncStateMachine().WithBlackboard(board));
    }

    [Test]
    public void sync_repeat_behavior_node_is_allocation_free()
    {
        (Behaviors.Repeat repeat, Blackboard board) = RepeatFixture();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(repeat)
            .Build();

        AssertZeroAlloc(graph.ToStateMachine().WithBlackboard(board));
    }

    private sealed class NoopSyncTokenObserver : NxGraph.Tokens.ITokenMachineObserver;

    private sealed class NoopAsyncTokenObserver : NxGraph.Tokens.IAsyncTokenMachineObserver
    {
        public ValueTask OnStateEntered(int tokenId, NodeId id, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask OnStateExited(int tokenId, NodeId id, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask OnTransition(int tokenId, NodeId from, NodeId to, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask OnTokenSpawned(int tokenId, int parentTokenId, NodeId at, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask OnTokenRetired(int tokenId, NodeId at, NxGraph.Tokens.TokenRetireReason reason,
            CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask OnJoinFired(NodeId joinNode, int survivingTokenId, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }
}
