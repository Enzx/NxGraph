using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
public class TimeoutWrapperTests
{
    [Test]
    [CancelAfter(10_000)]
    public async Task returns_failure_when_inner_exceeds_timeout_by_default(CancellationToken ct)
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(100.Milliseconds(), new AsyncRelayState(
                    async innerCt =>
                    {
                        await Task.Delay(1000, innerCt);
                        return Result.Success;
                    }), TimeoutBehavior.Fail
            )
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync(ct);
        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    [CancelAfter(10_000)]
    public void throws_timeoutexception_when_behavior_is_throw(CancellationToken ct)
    {
        AsyncStateMachine throwing = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(100), new AsyncRelayState(async innerCt =>
                {
                    await Task.Delay(1000, innerCt);
                    return Result.Success;
                }),
                TimeoutBehavior.Throw)
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<TimeoutException>(async () => await throwing.ExecuteAsync(ct));
    }

    [Test]
    [CancelAfter(10_000)]
    public void external_cancellation_wins_over_timeout()
    {
        using CancellationTokenSource cts = new(200); // cancel sooner than timeout

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(2000.Milliseconds(), async ct =>
            {
                await Task.Delay(5000, ct);
                return Result.Success;
            }, TimeoutBehavior.Fail) // timeout later than external cancel
            .ToAsyncStateMachine();

        // The timed cts does the cancelling mid-run; every await in here is bounded, so a
        // cancellation regression turns into a bounded assertion failure, never a hang.
        Assert.ThrowsAsync<TaskCanceledException>(async () => await fsm.ExecuteAsync(cts.Token));
    }

    [Test]
    public void rejects_non_positive_timeout_values()
    {
        // Using StartWithTimeout should throw when timeout <= 0
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            StateToken _ = Dsl.StartWithTimeoutAsync(TimeSpan.Zero, _ => ResultHelpers.Success);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            StateToken _ =
                Dsl.StartWithTimeoutAsync(TimeSpan.FromMilliseconds(-1), _ => ResultHelpers.Success);
        });
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task completes_successfully_if_inner_finishes_before_timeout(CancellationToken ct)
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(5.Seconds(), async innerCt =>
            {
                await Task.Delay(50, innerCt); // finishes well before the generous timeout
                return Result.Success;
            }, TimeoutBehavior.Fail)
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync(ct);
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}
