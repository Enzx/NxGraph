using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class ParallelRegionsTests
{
    private static Graph LoggingChain(List<string> log, string prefix, int length)
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

    [Test]
    public async Task regions_progress_in_interleaved_round_robin_order()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(LoggingChain(log, "a", 3), LoggingChain(log, "b", 2))
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1", "a2" }),
                "One node per region per round; the shorter region joins early.");
        });
    }

    [Test]
    public async Task join_fails_when_any_region_fails_but_others_still_finish()
    {
        List<string> log = [];
        Graph failing = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("f0");
                return ResultHelpers.Failure;
            })
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(LoggingChain(log, "a", 3), failing)
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(log, Does.Contain("a2"), "The healthy region ran to completion before the join.");
        });
    }

    [Test]
    public async Task failed_join_routes_through_the_parent_failure_edge()
    {
        bool recovered = false;
        Graph failing = GraphBuilder.StartWithAsync(_ => ResultHelpers.Failure).Build();
        Graph healthy = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();

        Graph parent = GraphBuilder
            .Start()
            .Parallel(healthy, failing)
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
    public async Task composite_can_run_repeatedly()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .Parallel(LoggingChain(log, "a", 2), LoggingChain(log, "b", 2))
            .Build();

        AsyncStateMachine machine = parent.ToAsyncStateMachine();
        await machine.ExecuteAsync();
        await machine.ExecuteAsync();

        Assert.That(log, Has.Count.EqualTo(8), "Both regions restart cleanly on the next run.");
    }

    [Test]
    public void empty_region_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _ = new AsyncParallelState());
    }

    [Test]
    public async Task agent_injection_reaches_region_graphs()
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
            .Parallel(region)
            .Build();

        AsyncStateMachine<List<string>> machine = parent.ToAsyncStateMachine<List<string>>();
        machine.SetAgent(log);
        await machine.ExecuteAsync();

        Assert.That(log, Is.EqualTo(new[] { "region-saw-agent" }));
    }
}
