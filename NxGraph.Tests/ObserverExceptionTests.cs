using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

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

    // ── Sync mirrors of the run-start regression (spec: sync machine lifecycle hardening) ──

    private sealed class SyncThrowOnceOnStartedObserver : IStateMachineObserver
    {
        private bool _thrown;

        public void OnStateMachineStarted(NodeId graphId)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("observer started boom");
            }
        }
    }

    [Test]
    public void sync_observer_exception_at_run_start_does_not_permanently_lock_the_machine()
    {
        // Regression (sync mirror of the async fix): an observer throwing during Execute()'s
        // run-start sequence escaped with the execute gate still held and the status stuck at
        // Starting, so every later Execute()/Reset() threw forever with no recovery API.
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(new SyncThrowOnceOnStartedObserver());

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => fsm.Execute());
        Assert.That(ex!.Message, Does.Contain("observer started boom"),
            "The first Execute() must rethrow the observer exception itself.");

        Result result = fsm.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success),
                "The machine must stay usable after an observer throw at run start.");
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready),
                "RestartPolicy.Auto resets the machine to Ready after the successful run.");
        });
    }

    [Test]
    public void sync_reset_after_a_run_start_throw_returns_success_and_unlocks()
    {
        // After a run-start throw the status is repaired to Ready and the gate is released;
        // Reset() must take the Created/Ready early-return, report Success, and leave the
        // machine executable (that path also defensively clears the gate).
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(new SyncThrowOnceOnStartedObserver());

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());

        Assert.That(fsm.Reset(), Is.EqualTo(Result.Success),
            "Reset() after a repaired run-start throw reports Success.");
        Assert.That(fsm.Execute(), Is.EqualTo(Result.Success),
            "The gate must not be leaked: the next Execute() runs to completion.");
    }

    private sealed class ResetCountingThrowOnceObserver : IStateMachineObserver
    {
        public int ResetCount;
        private bool _thrown;

        public void OnStateMachineReset(NodeId graphId)
        {
            ResetCount++;
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("observer reset boom");
            }
        }
    }

    [Test]
    public void sync_reset_is_idempotent_after_an_observer_interrupted_a_previous_reset()
    {
        // Parity with AsyncStateMachine.ResetCore: Reset() finding the machine already at
        // Resetting (reachable only when an observer threw out of a previous reset's
        // OnStateMachineReset notification) returns Success without re-entering the reset.
        // Re-transitioning Resetting -> Resetting would trip TransitionTo's debug assert in
        // DEBUG builds and must never fire a duplicate OnStateMachineReset.
        ResetCountingThrowOnceObserver observer = new();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(observer);
        fsm.SetRestartPolicy(RestartPolicy.Manual);

        Assert.That(fsm.Execute(), Is.EqualTo(Result.Success));
        Assert.Throws<InvalidOperationException>(() => fsm.Reset());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Resetting),
            "The interrupted reset leaves the machine at Resetting.");

        Assert.Multiple(() =>
        {
            Assert.That(fsm.Reset(), Is.EqualTo(Result.Success),
                "A Reset() finding Resetting must be idempotent.");
            Assert.That(observer.ResetCount, Is.EqualTo(1),
                "OnStateMachineReset must not fire a second time.");
        });

        Assert.That(fsm.Execute(), Is.EqualTo(Result.Success),
            "The machine must remain runnable after the idempotent reset.");
    }

    // ── TokenMachine twins ──────────────────────────────────────────────

    private sealed class TokenThrowOnceAtRunStartObserver(bool onStarted) : ITokenMachineObserver
    {
        private bool _thrown;

        public void OnTokenMachineStarted(NodeId graphId)
        {
            if (onStarted && !_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("token observer started boom");
            }
        }

        public void OnTokenSpawned(int tokenId, int parentTokenId, NodeId at)
        {
            if (!onStarted && !_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("token observer spawned boom");
            }
        }
    }

    [TestCase(true, Description = "throw from OnTokenMachineStarted — before ClearRunState")]
    [TestCase(false, Description = "throw from OnTokenSpawned — after ClearRunState/Alloc")]
    public void token_machine_observer_exception_at_run_start_does_not_permanently_lock_the_machine(bool onStarted)
    {
        // Sync token twin of the run-start regression: a throw from the Starting notification
        // or the root-token spawn escaped OnEnter with the execute gate held and the status
        // stuck at Starting. Whatever ClearRunState/SpawnRootToken already did is benign —
        // the next run start clears the run state again.
        TokenMachine machine = GraphBuilder
            .StartWith(() => Result.Success)
            .Build()
            .ToTokenMachine(new TokenThrowOnceAtRunStartObserver(onStarted));

        Assert.Throws<InvalidOperationException>(() => machine.Execute());

        Result result = machine.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success),
                "The machine must stay usable after an observer throw at run start.");
            Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Ready),
                "RestartPolicy.Auto resets the machine to Ready after the successful run.");
        });
    }

    private sealed class TokenResetCountingThrowOnceObserver : ITokenMachineObserver
    {
        public int ResetCount;
        private bool _thrown;

        public void OnTokenMachineReset(NodeId graphId)
        {
            ResetCount++;
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("token observer reset boom");
            }
        }
    }

    [Test]
    public void token_machine_reset_is_idempotent_after_an_observer_interrupted_a_previous_reset()
    {
        // TokenMachine twin of the Resetting-idempotence convergence with the async machines.
        TokenResetCountingThrowOnceObserver observer = new();
        TokenMachine machine = GraphBuilder
            .StartWith(() => Result.Success)
            .Build()
            .ToTokenMachine(observer);
        machine.SetRestartPolicy(RestartPolicy.Manual);

        Assert.That(machine.Execute(), Is.EqualTo(Result.Success));
        Assert.Throws<InvalidOperationException>(() => machine.Reset());
        Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Resetting),
            "The interrupted reset leaves the machine at Resetting.");

        Assert.Multiple(() =>
        {
            Assert.That(machine.Reset(), Is.EqualTo(Result.Success),
                "A Reset() finding Resetting must be idempotent.");
            Assert.That(observer.ResetCount, Is.EqualTo(1),
                "OnTokenMachineReset must not fire a second time.");
        });

        Assert.That(machine.Execute(), Is.EqualTo(Result.Success),
            "The machine must remain runnable after the idempotent reset.");
    }
}
