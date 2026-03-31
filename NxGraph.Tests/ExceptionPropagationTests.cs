using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("exceptions")]
public class ExceptionPropagationTests
{
    [Test]
    public void should_bubble_exception_from_on_enter()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                _ => ResultHelpers.Success,
                _ => throw new InvalidOperationException("enter boom")))
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void should_bubble_exception_from_on_run()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                _ => throw new ApplicationException("run boom")))
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void should_bubble_exception_from_on_exit()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                _ => ResultHelpers.Success,
                onExit: _ => throw new NotSupportedException("exit boom")))
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<NotSupportedException>(async () => await fsm.ExecuteAsync());
    }
}