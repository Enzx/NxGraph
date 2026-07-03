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
}
