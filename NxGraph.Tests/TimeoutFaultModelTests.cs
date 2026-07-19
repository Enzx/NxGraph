using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Timeouts participate in the unified fault model: a timed-out node is an ordinary
/// node failure, so failure edges and retry policies apply to it like any other failure.
/// </summary>
[TestFixture]
public class TimeoutFaultModelTests
{
    // Hangs until cancelled, but bounded at 30 s rather than Timeout.InfiniteTimeSpan: if the
    // timeout wrapper (or the token plumbing it relies on) regresses, the run goes red in
    // bounded time instead of hanging the whole suite. The [CancelAfter] guards below (whose
    // token is threaded into the machine) report the same regression much earlier.
    private static readonly Func<CancellationToken, ValueTask<Result>> HangUntilCancelled =
        async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Result.Success;
        };

    [Test]
    [CancelAfter(10_000)]
    public async Task timeout_routes_through_the_failure_edge(CancellationToken ct)
    {
        bool cleanupRan = false;
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(30), HangUntilCancelled, TimeoutBehavior.Fail)
            .OnErrorAsync(_ =>
            {
                cleanupRan = true;
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync(ct);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(cleanupRan, Is.True);
        });
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task timeout_is_retried_by_the_node_retry_policy(CancellationToken ct)
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(30), async innerCt =>
            {
                executions++;
                if (executions < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), innerCt); // bounded hang, see above
                }

                return Result.Success;
            }, TimeoutBehavior.Fail)
            .Retry(maxAttempts: 2)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync(ct);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(2), "The timed-out first attempt should be retried.");
        });
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task timeout_without_error_edge_still_fails_the_machine(CancellationToken ct)
    {
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(30), HangUntilCancelled, TimeoutBehavior.Fail)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync(ct);
        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task timeout_failure_carries_a_diagnostic_message(CancellationToken ct)
    {
        AsyncTimeoutState state = new(
            new AsyncRelayState(HangUntilCancelled), TimeSpan.FromMilliseconds(30));

        Result result = await state.ExecuteAsync(ct);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(result.Message, Does.Contain("timed out"));
        });
    }
}
