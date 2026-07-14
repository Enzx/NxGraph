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
    private static readonly Func<CancellationToken, ValueTask<Result>> HangForever =
        async ct =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return Result.Success;
        };

    [Test]
    public async Task timeout_routes_through_the_failure_edge()
    {
        bool cleanupRan = false;
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(30), HangForever, TimeoutBehavior.Fail)
            .OnErrorAsync(_ =>
            {
                cleanupRan = true;
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(cleanupRan, Is.True);
        });
    }

    [Test]
    public async Task timeout_is_retried_by_the_node_retry_policy()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(30), async ct =>
            {
                executions++;
                if (executions < 2)
                {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                }

                return Result.Success;
            }, TimeoutBehavior.Fail)
            .Retry(maxAttempts: 2)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(2), "The timed-out first attempt should be retried.");
        });
    }

    [Test]
    public async Task timeout_without_error_edge_still_fails_the_machine()
    {
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(TimeSpan.FromMilliseconds(30), HangForever, TimeoutBehavior.Fail)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    public async Task timeout_failure_carries_a_diagnostic_message()
    {
        AsyncTimeoutState state = new(
            new AsyncRelayState(HangForever), TimeSpan.FromMilliseconds(30));

        Result result = await state.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(result.Message, Does.Contain("timed out"));
        });
    }
}
