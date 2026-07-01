using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class EntryExitActionTests
{
    [Test]
    public async Task entry_and_exit_fire_in_order_around_each_node()
    {
        List<string> log = [];
        Graph graph = GraphBuilder
            .StartWithAsync(_ =>
            {
                log.Add("run:A");
                return ResultHelpers.Success;
            })
            .OnEnter(() => log.Add("enter:A"))
            .OnExit(() => log.Add("exit:A"))
            .ToAsync(_ =>
            {
                log.Add("run:B");
                return ResultHelpers.Success;
            })
            .OnEnter(() => log.Add("enter:B"))
            .OnExit(() => log.Add("exit:B"))
            .Build();

        await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(log, Is.EqualTo(new[]
        {
            "enter:A", "run:A", "exit:A",
            "enter:B", "run:B", "exit:B",
        }));
    }

    [Test]
    public void sync_entry_and_exit_fire_in_order_around_each_node()
    {
        List<string> log = [];
        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                log.Add("run:A");
                return Result.Success;
            })
            .OnEnter(() => log.Add("enter:A"))
            .OnExit(() => log.Add("exit:A"))
            .To(() =>
            {
                log.Add("run:B");
                return Result.Success;
            })
            .OnEnter(() => log.Add("enter:B"))
            .OnExit(() => log.Add("exit:B"))
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.That(log, Is.EqualTo(new[]
        {
            "enter:A", "run:A", "exit:A",
            "enter:B", "run:B", "exit:B",
        }));
    }

    [Test]
    public async Task retries_do_not_refire_the_entry_action()
    {
        int enters = 0;
        int exits = 0;
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++executions < 3 ? ResultHelpers.Failure : ResultHelpers.Success)
            .OnEnter(() => enters++)
            .OnExit(() => exits++)
            .Retry(maxAttempts: 3)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(3));
            Assert.That(enters, Is.EqualTo(1), "Entry fires once per visit, not per attempt.");
            Assert.That(exits, Is.EqualTo(1), "Exit fires once per visit, after the final attempt.");
        });
    }

    [Test]
    public async Task exit_fires_even_when_the_node_fails_terminally()
    {
        bool exited = false;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .OnExit(() => exited = true)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(exited, Is.True);
        });
    }

    [Test]
    public async Task entry_fires_again_on_revisits_via_goto()
    {
        int enters = 0;
        int laps = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Top")
            .OnEnter(() => enters++)
            .ToAsync(_ => ++laps < 3 ? ResultHelpers.Success : ResultHelpers.Failure)
            .Goto("Top")
            .Build();

        await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(enters, Is.EqualTo(3), "Each loop lap is a fresh visit.");
    }

    [Test]
    public void action_on_unknown_node_throws()
    {
        GraphBuilder builder = new();
        builder.AddNode(new RelayState(() => Result.Success), isStart: true);

        Assert.Throws<InvalidOperationException>(() => builder.SetEnterAction(new NodeId(9), () => { }));
    }
}
