using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Dynamic (some-of-many) parallel composites, both runtimes: entry-time region selection via
/// a blackboard-driven <see cref="RegionMask"/> selector, vacuous empty join, loud invalid
/// masks, and the context-forwarding contract (the stamping walk stops at the composite, which
/// must hand the context to its region machines itself).
/// </summary>
[TestFixture]
public class DynamicParallelRegionsTests
{
    private static readonly BlackboardSchema Schema = new("dyn-parallel");
    private static readonly BlackboardKey<int> Pick = Schema.Register<int>("pick");
    private static readonly BlackboardKey<int> RegionReads = Schema.Register<int>("regionReads");

    private static Graph AsyncLoggingChain(List<string> log, string prefix, int length)
    {
        StateToken token = GraphBuilder.StartWithAsync(_ =>
        {
            log.Add($"{prefix}0");
            return ResultHelpers.Success;
        });

        for (int i = 1; i < length; i++)
        {
            int step = i;
            token = token.ToAsync(_ =>
            {
                log.Add($"{prefix}{step}");
                return ResultHelpers.Success;
            });
        }

        return token.Build();
    }

    private static Graph SyncLoggingChain(List<string> log, string prefix, int length)
    {
        StateToken token = GraphBuilder.StartWith(() =>
        {
            log.Add($"{prefix}0");
            return Result.Success;
        });

        for (int i = 1; i < length; i++)
        {
            int step = i;
            token = token.To(() =>
            {
                log.Add($"{prefix}{step}");
                return Result.Success;
            });
        }

        return token.Build();
    }

    // ── RegionMask ───────────────────────────────────────────────────────

    [Test]
    public void region_mask_basics()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RegionMask.None.IsEmpty, Is.True);
            Assert.That(RegionMask.None.Count, Is.Zero);
            Assert.That(RegionMask.Of(0, 2).Count, Is.EqualTo(2));
            Assert.That(RegionMask.Of(0, 2).Contains(0), Is.True);
            Assert.That(RegionMask.Of(0, 2).Contains(1), Is.False);
            Assert.That(RegionMask.Of(0, 2).Contains(2), Is.True);
            Assert.That(RegionMask.All(64).Count, Is.EqualTo(64));
            Assert.That(RegionMask.All(3), Is.EqualTo(RegionMask.Of(0, 1, 2)));
            Assert.That(RegionMask.Bit(1) | RegionMask.Bit(3), Is.EqualTo(RegionMask.Of(1, 3)));
            Assert.That(RegionMask.Bit(63).Contains(63), Is.True);
        });
    }

    [Test]
    public void region_mask_rejects_out_of_range_indices()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RegionMask.Bit(64));
            Assert.Throws<ArgumentOutOfRangeException>(() => RegionMask.Bit(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => RegionMask.Of(0, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() => RegionMask.All(65));
        });
    }

    // ── Async composite ──────────────────────────────────────────────────

    [Test]
    public async Task async_only_selected_regions_execute()
    {
        List<string> log = [];
        Blackboard board = new(Schema);
        board.Set(Pick, 0);

        Graph parent = GraphBuilder
            .Start()
            .Parallel(bb => RegionMask.Bit(bb.Get(Pick)),
                AsyncLoggingChain(log, "a", 2), AsyncLoggingChain(log, "b", 2))
            .Build();

        Result result = await parent.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "a0", "a1" }), "Region b was never stepped.");
        });
    }

    [Test]
    public async Task async_rerun_with_different_blackboard_value_selects_different_subset()
    {
        List<string> log = [];
        Blackboard board = new(Schema);

        Graph parent = GraphBuilder
            .Start()
            .Parallel(bb => RegionMask.Bit(bb.Get(Pick)),
                AsyncLoggingChain(log, "a", 2), AsyncLoggingChain(log, "b", 2))
            .Build();

        AsyncStateMachine machine = parent.ToAsyncStateMachine().WithBlackboard(board);

        board.Set(Pick, 0);
        await machine.ExecuteAsync();
        board.Set(Pick, 1);
        await machine.ExecuteAsync();
        board.Set(Pick, 0);
        await machine.ExecuteAsync();

        Assert.That(log, Is.EqualTo(new[] { "a0", "a1", "b0", "b1", "a0", "a1" }),
            "Selection follows the blackboard per run; re-selected regions restart cleanly.");
    }

    [Test]
    public async Task async_empty_mask_is_a_vacuous_success()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(_ => RegionMask.None, AsyncLoggingChain(log, "a", 2))
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.Empty, "Nothing was stepped.");
        });
    }

    [Test]
    public void async_out_of_range_mask_bits_fail_loudly()
    {
        Graph parent = GraphBuilder
            .Start()
            .Parallel(_ => RegionMask.Of(0, 5), AsyncLoggingChain([], "a", 2), AsyncLoggingChain([], "b", 2))
            .Build();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await parent.ToAsyncStateMachine().ExecuteAsync());
    }

    [Test]
    public async Task async_selected_region_failure_routes_through_the_parent_failure_edge()
    {
        bool recovered = false;
        Graph failing = GraphBuilder.StartWithAsync(_ => ResultHelpers.Failure).Build();
        Graph healthy = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(_ => RegionMask.Of(0, 1), healthy, failing)
            .OnErrorAsync(_ =>
            {
                recovered = true;
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(recovered, Is.True);
        });
    }

    [Test]
    public async Task async_unselected_failing_region_does_not_fail_the_join()
    {
        Graph failing = GraphBuilder.StartWithAsync(_ => ResultHelpers.Failure).Build();
        Graph healthy = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(_ => RegionMask.Bit(0), healthy, failing)
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success), "The failing region was not selected.");
    }

    [Test]
    public async Task context_is_forwarded_to_selector_and_region_nodes()
    {
        // The stamping walk stops at the composite (IBlackboardSettable); this fails if the
        // composite does not forward the context to its region machines.
        Blackboard board = new(Schema);
        board.Set(Pick, 1);

        Graph regionA = GraphBuilder.StartWithAsync((bb, _) =>
        {
            bb.GetRef(RegionReads)++;
            return ResultHelpers.Success;
        }).Build();

        Graph regionB = GraphBuilder.StartWithAsync((bb, _) =>
        {
            bb.GetRef(RegionReads)++;
            return ResultHelpers.Success;
        }).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(bb => RegionMask.Bit(bb.Get(Pick)), regionA, regionB) // selector reads...
            .Build();

        Result result = await parent.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(RegionReads), Is.EqualTo(1), "...and the selected region node writes.");
        });
    }

    [Test]
    public async Task async_agent_injection_reaches_region_graphs()
    {
        List<string> log = [];
        Graph region = GraphBuilder
            .StartWithAsync(new AsyncRelayState<List<string>>((agent, _) =>
            {
                agent.Add("region-saw-agent");
                return ResultHelpers.Success;
            }))
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(_ => RegionMask.Bit(0), region)
            .Build();

        AsyncStateMachine<List<string>> machine = parent.ToAsyncStateMachine<List<string>>();
        machine.SetAgent(log);
        await machine.ExecuteAsync();

        Assert.That(log, Is.EqualTo(new[] { "region-saw-agent" }));
    }

    [Test]
    public void more_than_64_regions_are_rejected()
    {
        Graph[] regions = new Graph[65];
        for (int i = 0; i < regions.Length; i++)
        {
            regions[i] = GraphBuilder.StartWith(() => Result.Success).Build();
        }

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new AsyncDynamicParallelState(_ => RegionMask.None, regions));
            Assert.Throws<ArgumentException>(() =>
                _ = new DynamicParallelState(ParallelStepMode.RunToJoin, _ => RegionMask.None, regions));
        });
    }

    // ── Sync composite ───────────────────────────────────────────────────

    [Test]
    public void sync_run_to_join_steps_only_the_selected_subset_in_one_tick()
    {
        List<string> log = [];
        Blackboard board = new(Schema);
        board.Set(Pick, 1);

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, bb => RegionMask.Bit(bb.Get(Pick)),
                SyncLoggingChain(log, "a", 2), SyncLoggingChain(log, "b", 2))
            .Build();

        Result result = parent.ToStateMachine().WithBlackboard(board).Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "b0", "b1" }));
        });
    }

    [Test]
    public void sync_round_per_tick_selection_is_fixed_at_visit_entry()
    {
        List<string> log = [];
        Blackboard board = new(Schema);
        board.Set(Pick, 0);

        // Selector picks {a, b} when Pick == 0, else {b} only.
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick,
                bb => bb.Get(Pick) == 0 ? RegionMask.Of(0, 1) : RegionMask.Bit(1),
                SyncLoggingChain(log, "a", 3), SyncLoggingChain(log, "b", 3))
            .Build();

        StateMachine machine = parent.ToStateMachine().WithBlackboard(board);

        Assert.That(machine.Execute(), Is.EqualTo(Result.InProgress));
        Assert.That(log, Is.EqualTo(new[] { "a0", "b0" }));

        // Mid-run change must NOT re-select: region a keeps stepping until the join.
        board.Set(Pick, 1);
        Assert.That(machine.Execute(), Is.EqualTo(Result.InProgress));
        Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1" }),
            "Selection is evaluated once per visit — a is still active after the mid-run change.");

        Assert.That(machine.Execute(), Is.EqualTo(Result.Success));
        Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1", "a2", "b2" }));

        // The next visit re-evaluates the selector and drops region a.
        Result rerun = machine.Execute();
        while (rerun == Result.InProgress)
        {
            rerun = machine.Execute();
        }

        Assert.That(log.Skip(6), Is.EqualTo(new[] { "b0", "b1", "b2" }),
            "The new visit selected only region b.");
    }

    [Test]
    public void sync_empty_mask_is_a_vacuous_success()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, _ => RegionMask.None, SyncLoggingChain(log, "a", 2))
            .Build();

        Result result = parent.ToStateMachine().Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.Empty);
        });
    }

    [Test]
    public void sync_out_of_range_mask_bits_fail_loudly()
    {
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, _ => RegionMask.Bit(2),
                SyncLoggingChain([], "a", 2), SyncLoggingChain([], "b", 2))
            .Build();

        Assert.Throws<InvalidOperationException>(() => parent.ToStateMachine().Execute());
    }

    [Test]
    public void sync_selected_region_failure_routes_through_the_parent_failure_edge()
    {
        bool recovered = false;
        Graph failing = GraphBuilder.StartWith(() => Result.Failure).Build();
        Graph healthy = GraphBuilder.StartWith(() => Result.Success).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, _ => RegionMask.Of(0, 1), healthy, failing)
            .OnError(() =>
            {
                recovered = true;
                return Result.Success;
            })
            .Build();

        StateMachine machine = parent.ToStateMachine();
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(recovered, Is.True);
        });
    }
}
