using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
public class TimeoutWrapperTests
{
    [Test]
    [Timeout(5000)]
    public async Task returns_failure_when_inner_exceeds_timeout_by_default()
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(100.Milliseconds(), new AsyncRelayState(
                    async ct =>
                    {
                        await Task.Delay(1000, ct);
                        return Result.Success;
                    }), TimeoutBehavior.Fail
            )
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    [Timeout(5000)]
    public void throws_timeoutexception_when_behavior_is_throw()
    {
        AsyncStateMachine throwing = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(100), new AsyncRelayState(async ct =>
                {
                    await Task.Delay(1000, ct);
                    return Result.Success;
                }),
                TimeoutBehavior.Throw)
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<TimeoutException>(async () => await throwing.ExecuteAsync());
    }

    [Test]
    [Timeout(5000)]
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

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await fsm.ExecuteAsync(cts.Token);
            await cts.CancelAsync();
        });
    }

    [Test]
    [Timeout(5000)]
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
    [Timeout(5000)]
    public async Task completes_successfully_if_inner_finishes_before_timeout()
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(1.Seconds(), async ct =>
            {
                await Task.Delay(50, ct); // finishes well before timeout
                return Result.Success;
            }, TimeoutBehavior.Fail)
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}
