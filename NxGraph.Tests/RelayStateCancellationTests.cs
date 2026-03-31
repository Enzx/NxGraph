using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

using Authoring;
using Fsm;

[TestFixture]
[Category("relay_cancellation")]
public class RelayStateCancellationTests
{
    [Test]
    public void relay_run_should_observe_cancellation_token_and_throw()
    {
        CancellationTokenSource cts = new(10);
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(async ct =>
            {
                await Task.Delay(1000, ct);
                return Result.Success;
            }))
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<TaskCanceledException>(async () => await fsm.ExecuteAsync(cts.Token));
    }
}