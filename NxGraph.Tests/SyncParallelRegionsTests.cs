using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Sync twin of <see cref="ParallelRegionsTests"/> (runtime parity): same join semantics,
/// plus the <see cref="ParallelStepMode"/> split the sync runtime adds — whole join in one
/// tick (<see cref="ParallelStepMode.RunToJoin"/>) or one round per tick
/// (<see cref="ParallelStepMode.RoundPerTick"/>).
/// </summary>
[TestFixture]
public class SyncParallelRegionsTests
{
    private static Graph LoggingChain(List<string> log, string prefix, int length)
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

    [Test]
    public void run_to_join_completes_in_one_tick_with_interleaved_order()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, LoggingChain(log, "a", 3), LoggingChain(log, "b", 2))
            .Build();

        Result result = parent.ToStateMachine().Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success), "The whole join costs a single tick.");
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1", "a2" }),
                "One node per region per round; the shorter region joins early.");
        });
    }

    [Test]
    public void round_per_tick_advances_one_round_per_execute()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, LoggingChain(log, "a", 3), LoggingChain(log, "b", 2))
            .Build();

        StateMachine machine = parent.ToStateMachine();

        Result tick1 = machine.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(tick1, Is.EqualTo(Result.InProgress));
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0" }), "First tick = first round only.");
        });

        Result tick2 = machine.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(tick2, Is.EqualTo(Result.InProgress));
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1" }), "Second round; region b joins.");
        });

        Result tick3 = machine.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(tick3, Is.EqualTo(Result.Success), "Join result lands on the final round's tick.");
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1", "a2" }));
        });
    }

    [Test]
    public void join_fails_when_any_region_fails_but_others_still_finish()
    {
        List<string> log = [];
        Graph failing = GraphBuilder
            .StartWith(() =>
            {
                log.Add("f0");
                return Result.Failure;
            })
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, LoggingChain(log, "a", 3), failing)
            .Build();

        Result result = parent.ToStateMachine().Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(log, Does.Contain("a2"), "The healthy region ran to completion before the join.");
        });
    }

    [Test]
    public void failed_join_routes_through_the_parent_failure_edge()
    {
        bool recovered = false;
        Graph failing = GraphBuilder.StartWith(() => Result.Failure).Build();
        Graph healthy = GraphBuilder.StartWith(() => Result.Success).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, healthy, failing)
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

    [TestCase(ParallelStepMode.RunToJoin)]
    [TestCase(ParallelStepMode.RoundPerTick)]
    public void composite_can_run_repeatedly(ParallelStepMode mode)
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(mode, LoggingChain(log, "a", 2), LoggingChain(log, "b", 2))
            .Build();

        StateMachine machine = parent.ToStateMachine();
        for (int run = 0; run < 2; run++)
        {
            Result result = machine.Execute();
            while (result == Result.InProgress)
            {
                result = machine.Execute();
            }
        }

        Assert.That(log, Has.Count.EqualTo(8), "Both regions restart cleanly on the next run.");
    }

    [Test]
    public void empty_region_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _ = new ParallelState(ParallelStepMode.RunToJoin));
    }

    [Test]
    public void agent_injection_reaches_region_graphs()
    {
        List<string> log = [];
        Graph region = GraphBuilder
            .StartWith(new RelayState<List<string>>((agent, _) =>
            {
                agent.Add("region-saw-agent");
                return Result.Success;
            }))
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, region)
            .Build();

        StateMachine<List<string>> machine = parent.ToStateMachine<List<string>>();
        machine.SetAgent(log);
        machine.Execute();

        Assert.That(log, Is.EqualTo(new[] { "region-saw-agent" }));
    }

    [Test]
    public void blackboard_stamping_reaches_region_graphs()
    {
        BlackboardSchema schema = new("parallel-test");
        BlackboardKey<int> counter = schema.Register<int>("counter");
        Blackboard board = new(schema);

        Graph regionA = GraphBuilder.StartWith(bb =>
        {
            bb.GetRef(counter)++;
            return Result.Success;
        }).Build();

        Graph regionB = GraphBuilder.StartWith(bb =>
        {
            bb.GetRef(counter)++;
            return Result.Success;
        }).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, regionA, regionB)
            .Build();

        StateMachine machine = parent.ToStateMachine().WithBlackboard(board);
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.That(board.Get(counter), Is.EqualTo(2), "Both region nodes saw the machine-bound board.");
    }

    [Test]
    public async Task run_to_join_composite_works_under_the_async_runtime()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, LoggingChain(log, "a", 2), LoggingChain(log, "b", 2))
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success),
                "RunToJoin is a plain sync node, so the async machine runs it via the adapter.");
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1" }));
        });
    }

    [Test]
    public void round_per_tick_is_rejected_by_the_async_runtime()
    {
        Graph parent = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, LoggingChain([], "a", 2))
            .Build();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await parent.ToAsyncStateMachine().ExecuteAsync(),
            "Node-level InProgress is reserved in the async runtime — RoundPerTick is sync-only.");
    }
}
