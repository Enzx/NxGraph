using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
public class ReentrancyGuardTests
{
    [Test]
    public void second_execute_while_running_should_throw()
    {
        TaskCompletionSource blockTcs = new();
        StateMachine fsm = GraphBuilder
            .Start().WaitFor(1.Seconds()).To(_ => ResultHelpers.Success)
            .ToStateMachine();
        fsm.SetAutoReset(false);
        ValueTask<Result> first = fsm.ExecuteAsync();

        SpinWait.SpinUntil(() => fsm.Status == ExecutionStatus.Running, 1.Seconds());

        Assert.That(async () => await fsm.ExecuteAsync(), Throws.InvalidOperationException);

        blockTcs.SetResult();
        Assert.DoesNotThrowAsync(async () => await first);
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }
}