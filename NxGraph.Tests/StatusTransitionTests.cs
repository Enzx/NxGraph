using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
public class StatusTransitionTests
{
    [Test]
    public async Task status_flows_created_running_completed_on_success()
    {
        StateMachine fsm = GraphBuilder
            .Start().WaitFor(1.Seconds()).To(_ => ResultHelpers.Success)
            .ToStateMachine();
        fsm.SetAutoReset(false);
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
        ValueTask<Result> t = fsm.ExecuteAsync();
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Running));
        Result result = await t;
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
        });
    }

    [Test]
    public async Task status_sets_cancelled_on_cancellation()
    {
        CancellationTokenSource cts = new(10);
        StateMachine fsm = GraphBuilder
            .Start().WaitFor(1.Seconds()).To(_ => ResultHelpers.Success)
            .ToStateMachine();
        fsm.SetAutoReset(false);

        Assert.That(async () => await fsm.ExecuteAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());

        await cts.CancelAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Cancelled));
    }

    [Test]
    public void status_sets_failed_on_exception()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(_ => throw new InvalidOperationException("boom")))
            .ToStateMachine();
        fsm.SetAutoReset(false);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }
}