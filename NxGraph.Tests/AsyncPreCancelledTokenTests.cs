using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_pre_cancelled")]
public class AsyncPreCancelledTokenTests
{
    [Test]
    public async Task should_throw_immediately_with_pre_cancelled_token()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Assert.That(async () => await fsm.ExecuteAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task pre_cancelled_token_should_set_status_to_cancelled_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Assert.That(async () => await fsm.ExecuteAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Cancelled));
    }

    [Test]
    public async Task pre_cancelled_token_should_auto_reset_to_ready()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Assert.That(async () => await fsm.ExecuteAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }
}

