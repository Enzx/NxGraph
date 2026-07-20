using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class SuspendResumeTests
{
    private static Graph CountingChain(List<int> executed)
    {
        return GraphBuilder
            .StartWithAsync(_ =>
            {
                executed.Add(0);
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                executed.Add(1);
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                executed.Add(2);
                return ResultHelpers.Success;
            })
            .Build();
    }

    [Test]
    public async Task suspend_mid_run_and_resume_on_a_fresh_machine_completes_the_flow()
    {
        List<int> executed = [];
        Graph graph = CountingChain(executed);

        AsyncStateMachine first = graph.ToAsyncStateMachine();
        Result step = await first.StepAsync();
        Assert.That(step, Is.EqualTo(Result.InProgress));

        StateMachineSnapshot snapshot = first.Suspend();

        // Serialize/deserialize the snapshot to prove it survives a process boundary.
        string json = JsonSerializer.Serialize(snapshot);
        StateMachineSnapshot restored = JsonSerializer.Deserialize<StateMachineSnapshot>(json)!;

        AsyncStateMachine second = graph.ToAsyncStateMachine();
        second.Resume(restored);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await second.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }),
                "Node 0 ran on the first machine; the resumed machine continued at node 1.");
        });
    }

    [Test]
    public async Task snapshot_preserves_retry_progress()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ =>
            {
                executions++;
                return ResultHelpers.Failure;
            })
            .Retry(maxAttempts: 3)
            .Build();

        AsyncStateMachine first = graph.ToAsyncStateMachine();
        await first.StepAsync(); // attempt 1 fails; retry pending
        StateMachineSnapshot snapshot = first.Suspend();
        Assert.That(snapshot.Attempts, Is.EqualTo(1));

        AsyncStateMachine second = graph.ToAsyncStateMachine();
        second.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await second.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(executions, Is.EqualTo(3),
                "The resumed machine had 2 attempts left, not a fresh budget of 3.");
        });
    }

    [Test]
    public async Task resumed_machine_reports_status_running_mid_run()
    {
        Graph graph = CountingChain([]);
        AsyncStateMachine first = graph.ToAsyncStateMachine();
        await first.StepAsync();

        AsyncStateMachine second = graph.ToAsyncStateMachine();
        second.Resume(first.Suspend());

        Assert.That(second.Status, Is.EqualTo(ExecutionStatus.Running));
    }

    [Test]
    public void resume_rejects_out_of_range_node_index()
    {
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        StateMachineSnapshot bogus = new(42, ExecutionStatus.Running, 0, false, true, 0);
        Assert.Throws<InvalidOperationException>(() => machine.Resume(bogus));
    }

    [Test]
    public void resume_rejects_undefined_status_value()
    {
        // A corrupted or hand-crafted snapshot must not write an undefined value into the
        // machine's status field — every later status switch would misbehave silently.
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        StateMachineSnapshot bogus = new(0, (ExecutionStatus)99, 0, false, false, 0);
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => machine.Resume(bogus));
        Assert.That(ex!.Message, Does.Contain("Snapshot status value 99 is not a defined ExecutionStatus"));
    }

    [Test]
    public async Task suspend_of_an_idle_machine_roundtrips_through_execute()
    {
        List<int> executed = [];
        Graph graph = CountingChain(executed);

        AsyncStateMachine first = graph.ToAsyncStateMachine();
        StateMachineSnapshot snapshot = first.Suspend();

        AsyncStateMachine second = graph.ToAsyncStateMachine();
        second.Resume(snapshot);

        Result result = await second.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public async Task resumed_failed_snapshot_under_ignore_policy_reports_failure()
    {
        // Regression: _terminalResult was not reconstructed on Resume, so a machine restored
        // from a Failed snapshot under RestartPolicy.Ignore returned the field initializer's
        // Success — contradicting its own restored status.
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Failure).Build();

        AsyncStateMachine first = graph.ToAsyncStateMachine();
        first.SetRestartPolicy(RestartPolicy.Manual); // keep the terminal status for Suspend
        Result firstRun = await first.ExecuteAsync();
        Assert.That(firstRun, Is.EqualTo(Result.Failure));

        StateMachineSnapshot snapshot = first.Suspend();
        Assert.That(snapshot.Status, Is.EqualTo(ExecutionStatus.Failed));

        AsyncStateMachine second = graph.ToAsyncStateMachine();
        second.SetRestartPolicy(RestartPolicy.Ignore);
        second.Resume(snapshot);

        Result ignored = await second.ExecuteAsync();
        Assert.That(ignored, Is.EqualTo(Result.Failure),
            "Ignore policy must report the restored run's true terminal result.");
    }
}
