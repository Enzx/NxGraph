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
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(async ct =>
            {
                await Task.Delay(1000, ct);
                return Result.Success;
            }))
            .ToStateMachine();

        Assert.ThrowsAsync<TaskCanceledException>(async () => await fsm.ExecuteAsync(cts.Token));
    }
}