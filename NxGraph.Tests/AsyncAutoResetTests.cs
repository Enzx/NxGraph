using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_auto_reset")]
public class AsyncAutoResetTests
{
    // -- Success paths --------------------------------------------------

    [Test]
    public async Task status_should_be_ready_after_success_with_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Auto);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public async Task status_should_be_completed_after_success_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Manual);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    // -- Failure paths --------------------------------------------------

    [Test]
    public async Task status_should_be_failed_after_failure_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    [Test]
    public async Task should_auto_reset_to_ready_after_failure()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        await fsm.ExecuteAsync();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    // -- Exception paths ------------------------------------------------

    [Test]
    public async Task should_auto_reset_to_ready_after_exception()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ => throw new ApplicationException("boom")))
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Auto);

        await Assert.ThatAsync(async () => await fsm.ExecuteAsync(), Throws.InstanceOf<ApplicationException>());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public async Task status_should_be_failed_after_exception_without_auto_reset()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ => throw new ApplicationException("boom")))
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Manual);

        await Assert.ThatAsync(async () => await fsm.ExecuteAsync(), Throws.InstanceOf<ApplicationException>());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    // -- Cancellation paths ---------------------------------------------

    [Test]
    public async Task should_auto_reset_to_ready_after_cancellation()
    {
        using CancellationTokenSource cts = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Auto);

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
            .StartWithAsync(new AsyncRelayState(async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Manual);

        ValueTask<Result> task = fsm.ExecuteAsync(cts.Token);
        await cts.CancelAsync();

        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Cancelled));
    }

    [Test]
    public async Task execute_should_be_silently_ignored_after_completed_when_reset_policy_is_ignore()
    {
        int counter = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ =>
            {
                counter++;
                return new ValueTask<Result>(Result.Success);
            }))
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Ignore);

        Result first = await fsm.ExecuteAsync();
        Assert.That(first, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(1));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        Result second = await fsm.ExecuteAsync();
        Assert.That(second, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(1), "Ignore policy must not re-run node logic");
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    [Test]
    public async Task execute_should_run_again_after_manual_reset_when_reset_policy_is_ignore()
    {
        int counter = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ =>
            {
                counter++;
                return new ValueTask<Result>(Result.Success);
            }))
            .ToAsyncStateMachine();
        fsm.SetRestartPolicy(RestartPolicy.Ignore);

        await fsm.ExecuteAsync();
        Assert.That(counter, Is.EqualTo(1));

        await fsm.Reset();

        await fsm.ExecuteAsync();
        Assert.That(counter, Is.EqualTo(2));
    }
}


