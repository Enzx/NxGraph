using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("exceptions")]
public class ExceptionPropagationTests
{
    [Test]
    public void should_bubble_exception_from_on_enter()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                _ => ResultHelpers.Success,
                _ => throw new InvalidOperationException("enter boom")))
            .ToStateMachine();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void should_bubble_exception_from_on_run()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                _ => throw new ApplicationException("run boom")))
            .ToStateMachine();

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void should_bubble_exception_from_on_exit()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                _ => ResultHelpers.Success,
                onExit: _ => throw new NotSupportedException("exit boom")))
            .ToStateMachine();

        Assert.ThrowsAsync<NotSupportedException>(async () => await fsm.ExecuteAsync());
    }
}