using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class SubGraphHistoryTests
{
    [Test]
    public async Task subgraph_runs_the_child_to_completion_within_the_parent()
    {
        List<string> log = [];
        Graph child = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("child:0");
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                log.Add("child:1");
                return ResultHelpers.Success;
            })
            .Build();

        Graph parent = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("parent:start");
                return ResultHelpers.Success;
            })
            .SubGraph(child)
            .ToAsync(_ =>
            {
                log.Add("parent:end");
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "parent:start", "child:0", "child:1", "parent:end" }));
        });
    }

    [Test]
    public async Task history_composite_resumes_at_the_failed_child_node_on_reentry()
    {
        List<string> log = [];
        bool repaired = false;

        Graph child = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("c0");
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                log.Add("c1");
                return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
            })
            .ToAsync(_ =>
            {
                log.Add("c2");
                return ResultHelpers.Success;
            })
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(child, history: true)
            .SetName("Sub");

        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
        {
            repaired = true;
            log.Add("repair");
            return ResultHelpers.Success;
        })));
        repair.Goto("Sub");

        Graph parent = sub.OnError(repair).Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "After the repair, the child resumed at the failed node c1 — c0 did not re-run.");
        });
    }

    [Test]
    public async Task without_history_reentry_restarts_the_child_from_its_start()
    {
        List<string> log = [];
        bool repaired = false;

        Graph child = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("c0");
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                log.Add("c1");
                return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
            })
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(child)
            .SetName("Sub");

        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
        {
            repaired = true;
            return ResultHelpers.Success;
        })));
        repair.Goto("Sub");

        Graph parent = sub.OnError(repair).Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "c0", "c1" }),
                "Without history the child restarts from c0 on re-entry.");
        });
    }

    [Test]
    public async Task deep_history_resumes_nested_composites_at_their_own_position()
    {
        List<string> log = [];
        bool repaired = false;

        Graph inner = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("i0");
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                log.Add("i1");
                return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
            })
            .Build();

        Graph middle = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("m0");
                return ResultHelpers.Success;
            })
            .SubGraph(inner, history: true)
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(middle, history: true)
            .SetName("Sub");

        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
        {
            repaired = true;
            log.Add("repair");
            return ResultHelpers.Success;
        })));
        repair.Goto("Sub");

        Graph parent = sub.OnError(repair).Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "m0", "i0", "i1", "repair", "i1" }),
                "Both levels resumed their positions: neither m0 nor i0 re-ran.");
        });
    }

    [Test]
    public async Task completed_history_composite_restarts_from_the_top_on_reentry()
    {
        List<string> log = [];
        int laps = 0;

        Graph child = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("c0");
                return ResultHelpers.Success;
            })
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(child, history: true).SetName("Sub")
            .ToAsync(_ => ++laps < 2 ? ResultHelpers.Success : ResultHelpers.Failure)
            .Goto("Sub")
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c0" }),
                "A completed child restarts from its start node on the next visit.");
        });
    }
}
