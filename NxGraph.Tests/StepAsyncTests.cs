using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class StepAsyncTests
{
    private sealed class RecordingObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Events = [];

        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            Events.Add($"entered:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            Events.Add($"exited:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default)
        {
            Events.Add($"failed:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            Events.Add($"transition:{from.Index}->{to.Index}");
            return ValueTask.CompletedTask;
        }
    }

    private static Graph ThreeNodeChain()
    {
        return GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();
    }

    [Test]
    public async Task stepping_a_three_node_graph_yields_continue_continue_success()
    {
        AsyncStateMachine machine = ThreeNodeChain().ToAsyncStateMachine();

        Result first = await machine.StepAsync();
        Result second = await machine.StepAsync();
        Result third = await machine.StepAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Result.InProgress));
            Assert.That(second, Is.EqualTo(Result.InProgress));
            Assert.That(third, Is.EqualTo(Result.Success));
        });
    }

    [Test]
    public async Task machine_stays_running_between_steps()
    {
        AsyncStateMachine machine = ThreeNodeChain().ToAsyncStateMachine();

        await machine.StepAsync();
        Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Running));

        await machine.StepAsync();
        Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Running));

        await machine.StepAsync();
        Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Ready),
            "Auto restart policy resets the machine to Ready after the terminal step.");
    }

    [Test]
    public async Task stepped_run_produces_the_same_observer_events_as_a_full_run()
    {
        RecordingObserver full = new();
        RecordingObserver stepped = new();

        await ThreeNodeChain().ToAsyncStateMachine(full).ExecuteAsync();

        AsyncStateMachine machine = ThreeNodeChain().ToAsyncStateMachine(stepped);
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await machine.StepAsync();
        }

        Assert.That(stepped.Events, Is.EqualTo(full.Events));
    }

    [Test]
    public async Task execute_during_a_stepped_run_throws()
    {
        AsyncStateMachine machine = ThreeNodeChain().ToAsyncStateMachine();
        await machine.StepAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await machine.ExecuteAsync());
    }

    [Test]
    public async Task stepping_after_completion_starts_a_fresh_run_under_auto_policy()
    {
        AsyncStateMachine machine = ThreeNodeChain().ToAsyncStateMachine();

        for (int i = 0; i < 3; i++)
        {
            await machine.StepAsync();
        }

        Result restarted = await machine.StepAsync();
        Assert.That(restarted, Is.EqualTo(Result.InProgress), "A new run should have begun at the start node.");
    }

    [Test]
    public async Task stepping_routes_failures_through_error_edges()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .OnErrorAsync(_ => ResultHelpers.Success)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        Result first = await machine.StepAsync();
        Result second = await machine.StepAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Result.InProgress), "The failure reroute counts as one step.");
            Assert.That(second, Is.EqualTo(Result.Success));
        });
    }

    [Test]
    public async Task full_run_behavior_is_unchanged()
    {
        Result result = await ThreeNodeChain().ToAsyncStateMachine().ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}
