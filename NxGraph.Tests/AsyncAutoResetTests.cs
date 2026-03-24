using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_auto_reset")]
public class AsyncAutoResetTests
{
    // ── Success paths ──────────────────────────────────────────────────

    [Test]
    public async Task status_should_be_ready_after_success_with_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public async Task status_should_be_completed_after_success_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    // ── Failure paths ──────────────────────────────────────────────────

    [Test]
    public async Task status_should_be_failed_after_failure_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    [Test]
    public async Task should_auto_reset_to_ready_after_failure()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    // ── Exception paths ────────────────────────────────────────────────

    [Test]
    public async Task should_auto_reset_to_ready_after_exception()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(_ => throw new ApplicationException("boom")))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public async Task status_should_be_failed_after_exception_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(_ => throw new ApplicationException("boom")))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    // ── Cancellation paths ─────────────────────────────────────────────

    [Test]
    public async Task should_auto_reset_to_ready_after_cancellation()
    {
        using CancellationTokenSource cts = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        ValueTask<Result> task = fsm.ExecuteAsync(cts.Token);
        await cts.CancelAsync();

        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public async Task status_should_be_cancelled_without_auto_reset()
    {
        using CancellationTokenSource cts = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        ValueTask<Result> task = fsm.ExecuteAsync(cts.Token);
        await cts.CancelAsync();

        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Cancelled));
    }
}

