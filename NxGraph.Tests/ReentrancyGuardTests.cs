using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests
{
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

            ValueTask<Result> first = fsm.ExecuteAsync();

            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Running));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());

            blockTcs.SetResult();
            Assert.DoesNotThrowAsync(async () => await first);
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
        }
    }
}