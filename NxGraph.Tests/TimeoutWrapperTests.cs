using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
public class TimeoutWrapperTests
{
    [Test]
    [Timeout(5000)]
    public async Task returns_failure_when_inner_exceeds_timeout_by_default()
    {
        StateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeout(new RelayState(
                    async ct =>
                    {
                        await Task.Delay(1000, ct);
                        return Result.Success;
                    }), 100.Milliseconds(), TimeoutBehavior.Fail
            )
            .ToStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    [Timeout(5000)]
    public void throws_timeoutexception_when_behavior_is_throw()
    {
        StateMachine throwing = GraphBuilder
            .Start()
            .ToWithTimeout(new RelayState(async ct =>
                {
                    await Task.Delay(1000, ct);
                    return Result.Success;
                }),
                TimeSpan.FromMilliseconds(100), TimeoutBehavior.Throw)
            .ToStateMachine();

        Assert.ThrowsAsync<TimeoutException>(async () => await throwing.ExecuteAsync());
    }

    [Test]
    [Timeout(5000)]
    public void external_cancellation_wins_over_timeout()
    {
        using CancellationTokenSource cts = new(50); // cancel sooner than timeout

        StateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeout(async ct =>
            {
                await Task.Delay(1000, ct);
                return Result.Success;
            }, 500.Milliseconds(), TimeoutBehavior.Fail) // timeout later than external cancel
            .ToStateMachine();

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
            StateToken _ = Dsl.StartWithTimeout(_ => ResultHelpers.Success, TimeSpan.Zero);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            StateToken _ =
                Dsl.StartWithTimeout(_ => ResultHelpers.Success, TimeSpan.FromMilliseconds(-1));
        });
    }

    [Test]
    [Timeout(5000)]
    public async Task completes_successfully_if_inner_finishes_before_timeout()
    {
        StateMachine fsm = GraphBuilder
            .Start()
            .ToWithTimeout(async ct =>
            {
                await Task.Delay(50, ct); // finishes well before timeout
                return Result.Success;
            }, 1.Seconds(), TimeoutBehavior.Fail)
            .ToStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}