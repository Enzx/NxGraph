using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class SyncSuspendResumeTests
{
    private static Graph CountingChain(List<int> executed)
    {
        return GraphBuilder
            .StartWith(() =>
            {
                executed.Add(0);
                return Result.Success;
            })
            .To(() =>
            {
                executed.Add(1);
                return Result.Success;
            })
            .To(() =>
            {
                executed.Add(2);
                return Result.Success;
            })
            .Build();
    }

    private static Result RunToCompletion(StateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    [Test]
    public void suspend_mid_run_and_resume_on_a_fresh_machine_completes_the_flow()
    {
        List<int> executed = [];
        Graph graph = CountingChain(executed);

        StateMachine first = graph.ToStateMachine();
        Result tick = first.Execute();
        Assert.That(tick, Is.EqualTo(Result.InProgress));

        StateMachineSnapshot snapshot = first.Suspend();

        // Serialize/deserialize the snapshot to prove it survives a process boundary.
        string json = JsonSerializer.Serialize(snapshot);
        StateMachineSnapshot restored = JsonSerializer.Deserialize<StateMachineSnapshot>(json)!;

        StateMachine second = graph.ToStateMachine();
        second.Resume(restored);

        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }),
                "Node 0 ran on the first machine; the resumed machine continued at node 1.");
        });
    }

    [Test]
    public void snapshot_preserves_retry_progress()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                executions++;
                return Result.Failure;
            })
            .Retry(maxAttempts: 3)
            .Build();

        StateMachine first = graph.ToStateMachine();
        first.Execute(); // attempt 1 fails; retry pending on the next tick
        StateMachineSnapshot snapshot = first.Suspend();
        Assert.That(snapshot.Attempts, Is.EqualTo(1));

        StateMachine second = graph.ToStateMachine();
        second.Resume(snapshot);

        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(executions, Is.EqualTo(3),
                "The resumed machine had 2 attempts left, not a fresh budget of 3.");
        });
    }

    [Test]
    public void resumed_machine_reports_status_running_mid_run()
    {
        Graph graph = CountingChain([]);
        StateMachine first = graph.ToStateMachine();
        first.Execute();

        StateMachine second = graph.ToStateMachine();
        second.Resume(first.Suspend());

        Assert.That(second.Status, Is.EqualTo(ExecutionStatus.Running));
    }

    [Test]
    public void resume_rejects_out_of_range_node_index()
    {
        Graph graph = GraphBuilder.StartWith(() => Result.Success).Build();
        StateMachine machine = graph.ToStateMachine();

        StateMachineSnapshot bogus = new(42, ExecutionStatus.Running, 0, false, true, 0);
        Assert.Throws<InvalidOperationException>(() => machine.Resume(bogus));
    }

    [Test]
    public void suspend_of_an_idle_machine_roundtrips_through_execute()
    {
        List<int> executed = [];
        Graph graph = CountingChain(executed);

        StateMachine first = graph.ToStateMachine();
        StateMachineSnapshot snapshot = first.Suspend();

        StateMachine second = graph.ToStateMachine();
        second.Resume(snapshot);

        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public void suspend_from_inside_node_logic_throws()
    {
        StateMachine? machine = null;
        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                Assert.Throws<InvalidOperationException>(() => machine!.Suspend());
                return Result.Success;
            })
            .Build();

        machine = graph.ToStateMachine();
        Result result = RunToCompletion(machine);
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void snapshot_taken_by_async_machine_resumes_on_sync_machine()
    {
        // Same graph shape, sync-executable nodes: the snapshot format is runtime-agnostic.
        List<int> executed = [];
        Graph graph = CountingChain(executed);

        StateMachine first = graph.ToStateMachine();
        first.Execute();
        StateMachineSnapshot snapshot = first.Suspend();

        // Round-trip through the async machine's Resume/Suspend and back to sync.
        Fsm.Async.AsyncStateMachine asyncMachine = graph.ToAsyncStateMachine();
        asyncMachine.Resume(snapshot);
        StateMachineSnapshot rebounced = asyncMachine.Suspend();

        StateMachine second = graph.ToStateMachine();
        second.Resume(rebounced);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }
}
