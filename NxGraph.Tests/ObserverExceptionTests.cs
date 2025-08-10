using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("observer_exceptions")]
public class ObserverExceptionTests
{
    private sealed class ExplosiveObserver : IAsyncStateObserver
    {
        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            throw new InvalidOperationException("observer entered boom");
        }
    }

    [Test]
    public void observer_exception_should_bubble_to_caller()
    {
        ExplosiveObserver? observer = new();
        StateMachine? fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToStateMachine(observer);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }
}