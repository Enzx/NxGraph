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
/// so the fixture self-ignores in Debug.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class AllocationGateTests
{
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

    private static async Task AssertZeroAllocAsync(AsyncStateMachine machine)
    {
        for (int i = 0; i < 50; i++)
        {
            await machine.ExecuteAsync();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            await machine.ExecuteAsync();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            $"Async hot path allocated {allocated} B over {Iterations} runs.");
    }

    private static void AssertZeroAlloc(StateMachine machine)
    {
        for (int i = 0; i < 50; i++)
        {
            RunToCompletion(machine);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            RunToCompletion(machine);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            $"Sync hot path allocated {allocated} B over {Iterations} runs.");
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

        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        for (int i = 0; i < 50; i++)
        {
            await machine.ExecuteAsync();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            await machine.ExecuteAsync();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Goto loop allocated {allocated} B over {Iterations} runs.");
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

        for (int i = 0; i < 50; i++)
        {
            while ((await machine.StepAsync()) == Result.InProgress)
            {
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            while ((await machine.StepAsync()) == Result.InProgress)
            {
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Stepped execution allocated {allocated} B over {Iterations} runs.");
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

    // ── Token runtime (spec 007): pooled tokens, fork/join, per-token scratch ──

    private static void AssertZeroAllocTokens(NxGraph.Tokens.TokenMachine machine)
    {
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        for (int i = 0; i < 50; i++)
        {
            machine.Execute();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            machine.Execute();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            $"Sync token hot path allocated {allocated} B over {Iterations} runs.");
    }

    private static async Task AssertZeroAllocTokensAsync(NxGraph.Tokens.AsyncTokenMachine machine)
    {
        for (int i = 0; i < 50; i++)
        {
            await machine.ExecuteAsync();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            await machine.ExecuteAsync();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            $"Async token hot path allocated {allocated} B over {Iterations} runs.");
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
        NxGraph.Tokens.JoinState merge = new(NxGraph.Tokens.JoinPolicy.Any);
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(merge).To(() => Result.Success),
                b => b.To(() => Result.Success).To(merge))
            .Build();

        AssertZeroAllocTokens(graph.ToTokenMachine());
    }

    [Test]
    public void sync_token_retry_without_backoff_is_allocation_free()
    {
        int calls = 0;
        NxGraph.Tokens.JoinState join = new(NxGraph.Tokens.JoinPolicy.All(2));
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => ++calls % 2 == 0 ? Result.Success : Result.Failure)
                    .Retry(2)
                    .To(join),
                b => b.To(() => Result.Success).To(join))
            .Build();

        AssertZeroAllocTokens(graph.ToTokenMachine());
    }

    [Test]
    public void sync_token_node_scope_scratch_is_allocation_free()
    {
        BlackboardSchema nodeSchema = new("token-scratch", BlackboardScope.Node);
        BlackboardKey<int> counter = nodeSchema.Register("counter", 0);

        NxGraph.Tokens.JoinState join = new(NxGraph.Tokens.JoinPolicy.All(2));
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        Graph graph = start.ForkTo(
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

        AssertZeroAllocTokens(graph.ToTokenMachine());
    }

    [Test]
    public async Task async_token_with_observer_is_allocation_free()
    {
        await AssertZeroAllocTokensAsync(TokenDiamond().ToAsyncTokenMachine(new NoopAsyncTokenObserver()));
    }

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
