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

    private sealed class ThrowOnceOnStartedObserver : IAsyncStateMachineObserver
    {
        private bool _thrown;

        public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("observer started boom");
            }

            return default;
        }
    }

    [Test]
    public async Task observer_exception_at_run_start_does_not_permanently_lock_the_machine()
    {
        // Regression: an observer throwing during ExecuteAsync's run-start sequence escaped
        // with the execute gate still held, so every later call threw "already executing"
        // forever with no recovery API.
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(new ThrowOnceOnStartedObserver());

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success),
            "The machine must stay usable after an observer throw at run start.");
    }

    private sealed class CompletionRecordingThrowingObserver : IAsyncStateMachineObserver
    {
        public readonly List<Result> CompletedResults = [];

        public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
        {
            CompletedResults.Add(result);
            throw new InvalidOperationException("observer completed boom");
        }
    }

    [Test]
    public void observer_exception_at_completion_does_not_refire_completed_or_flip_the_result()
    {
        // Regression: an observer throwing inside the Completed notification fell into the
        // generic failure handler, which re-transitioned Completed -> Failed and fired a
        // second OnStateMachineCompleted(Failure) for a run that actually succeeded.
        CompletionRecordingThrowingObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(observer);
        fsm.SetRestartPolicy(RestartPolicy.Manual);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fsm.ExecuteAsync());

        Assert.Multiple(() =>
        {
            Assert.That(observer.CompletedResults, Is.EqualTo(new[] { Result.Success }),
                "OnStateMachineCompleted must fire exactly once, with the run's true result.");
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed),
                "A successful run must not be flipped to Failed by an observer throw.");
        });
    }
}
