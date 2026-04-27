using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("observer_exceptions")]
public class ObserverExceptionTests
{
    private sealed class ThrowingObserver(
        bool onEntered = false,
        bool onExited = false,
        bool onTransition = false,
        bool onCompleted = false) : IAsyncStateMachineObserver
    {
        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            if (onEntered) throw new InvalidOperationException("observer entered boom");
            return default;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            if (onExited) throw new InvalidOperationException("observer exited boom");
            return default;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            if (onTransition) throw new InvalidOperationException("observer transition boom");
            return default;
        }

        public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
        {
            if (onCompleted) throw new InvalidOperationException("observer completed boom");
            return default;
        }
    }

    [Test]
    public void observer_exception_in_on_state_entered_should_bubble_to_caller()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(new ThrowingObserver(onEntered: true));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void observer_exception_in_on_state_exited_should_bubble_to_caller()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(new ThrowingObserver(onExited: true));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void observer_exception_in_on_transition_should_bubble_to_caller()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(new ThrowingObserver(onTransition: true));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }

    [Test]
    public void observer_exception_in_on_state_machine_completed_should_bubble_to_caller()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(new ThrowingObserver(onCompleted: true));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());
    }
}
