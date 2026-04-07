using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_reset")]
public class AsyncResetTests
{
    [Test]
    public async Task reset_from_completed_should_move_to_ready()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();

        Result resetResult = await fsm.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(resetResult, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
        });
    }

    [Test]
    public async Task reset_from_failed_should_move_to_ready()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();

        Result resetResult = await fsm.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(resetResult, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
        });
    }

    [Test]
    public async Task reset_from_cancelled_should_move_to_ready()
    {
        using CancellationTokenSource cts = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        ValueTask<Result> task = fsm.ExecuteAsync(cts.Token);
        await cts.CancelAsync();
        try { await task; }
        catch (OperationCanceledException) { /* expected */ }

        Result resetResult = await fsm.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(resetResult, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
        });
    }

    [Test]
    public async Task reset_from_created_should_succeed_immediately()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        Result resetResult = await fsm.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(resetResult, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
        });
    }

    [Test]
    public async Task reset_while_running_should_throw()
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start().WaitForAsync(1.Seconds()).ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        ValueTask<Result> task = fsm.ExecuteAsync();
        SpinWait.SpinUntil(() => fsm.Status == ExecutionStatus.Running, 1.Seconds());

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.Reset());

        await task; // let it finish cleanly
    }

    [Test]
    public async Task can_execute_again_after_manual_reset()
    {
        int counter = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ =>
            {
                counter++;
                return ResultHelpers.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();
        Assert.That(counter, Is.EqualTo(1));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        await fsm.Reset();
        await fsm.ExecuteAsync();
        Assert.That(counter, Is.EqualTo(2));
    }

    [Test]
    public async Task execute_without_reset_should_throw_when_completed()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public async Task execute_without_reset_should_throw_when_failed()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public async Task execute_without_reset_should_throw_when_cancelled()
    {
        using CancellationTokenSource cts = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        ValueTask<Result> task = fsm.ExecuteAsync(cts.Token);
        await cts.CancelAsync();
        try { await task; }
        catch (OperationCanceledException) { /* expected */ }

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Cancelled));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }
}


