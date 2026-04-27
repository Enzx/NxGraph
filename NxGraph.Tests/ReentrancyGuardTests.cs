using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
public class ReentrancyGuardTests
{
    [Test]
    public void second_execute_while_running_should_throw()
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start().WaitForAsync(1.Seconds()).ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);
        ValueTask<Result> first = fsm.ExecuteAsync();

        SpinWait.SpinUntil(() => fsm.Status == ExecutionStatus.Running, 1.Seconds());

        Assert.That(async () => await fsm.ExecuteAsync(), Throws.InvalidOperationException);

        Assert.DoesNotThrowAsync(async () => await first);
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }
}
