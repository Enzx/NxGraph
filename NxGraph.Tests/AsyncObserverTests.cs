using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_observer")]
public class AsyncObserverTests
{
    // ── Failure result ─────────────────────────────────────────────────

    [Test]
    public async Task observer_should_receive_failed_result()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Failure));
    }

    [Test]
    public async Task observer_should_receive_success_result()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Success));
    }

    // ── OnStateFailed ──────────────────────────────────────────────────

    [Test]
    public void observer_should_receive_on_state_failed_on_exception()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ => throw new ApplicationException("boom")))
            .ToAsyncStateMachine(observer);
        fsm.SetAutoReset(false);

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());

        Assert.Multiple(() =>
        {
            Assert.That(observer.FailedExceptions, Has.Count.EqualTo(1));
            Assert.That(observer.FailedExceptions[0], Is.TypeOf<ApplicationException>());
        });
    }

    // ── Log reports ────────────────────────────────────────────────────

    [Test]
    public async Task observer_should_receive_log_reports()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncLoggingState("hello-async"))
            .ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.LogMessages, Has.Count.EqualTo(1));
            Assert.That(observer.LogMessages[0], Is.EqualTo("hello-async"));
        });
    }

    [Test]
    public async Task observer_should_receive_multiple_log_reports_across_states()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncLoggingState("msg-1"))
            .ToAsync(new AsyncLoggingState("msg-2"))
            .ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.LogMessages, Has.Count.EqualTo(2));
            Assert.That(observer.LogMessages[0], Is.EqualTo("msg-1"));
            Assert.That(observer.LogMessages[1], Is.EqualTo("msg-2"));
        });
    }

    // ── Transition counting ────────────────────────────────────────────

    [Test]
    public async Task observer_should_receive_correct_counts_for_three_states()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.Transitions, Has.Count.EqualTo(2));
            Assert.That(observer.EnteredIds, Has.Count.EqualTo(3));
            Assert.That(observer.ExitedIds, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task observer_should_receive_started_and_completed_for_single_state()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.Started, Is.True);
            Assert.That(observer.CompletedResult, Is.EqualTo(Result.Success));
        });
    }

    // ── Status change ordering (two states, no auto-reset) ─────────────

    [Test]
    public async Task observer_should_receive_status_changes_in_order_for_two_states()
    {
        RecordingAsyncObserver observer = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(observer);
        fsm.SetAutoReset(false);

        await fsm.ExecuteAsync();

        // Created→Starting→Running→Transitioning→Running→Completed
        (ExecutionStatus, ExecutionStatus)[] expected =
        [
            (ExecutionStatus.Created, ExecutionStatus.Starting),
            (ExecutionStatus.Starting, ExecutionStatus.Running),
            (ExecutionStatus.Running, ExecutionStatus.Transitioning),
            (ExecutionStatus.Transitioning, ExecutionStatus.Running),
            (ExecutionStatus.Running, ExecutionStatus.Completed),
        ];
        Assert.That(observer.StatusChanges, Is.EqualTo(expected));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class RecordingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<NodeId> EnteredIds = [];
        public readonly List<NodeId> ExitedIds = [];
        public readonly List<(NodeId From, NodeId To)> Transitions = [];
        public readonly List<(ExecutionStatus Prev, ExecutionStatus Next)> StatusChanges = [];
        public readonly List<Exception> FailedExceptions = [];
        public readonly List<string> LogMessages = [];
        public bool Started;
        public Result? CompletedResult;

        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            EnteredIds.Add(id);
            return default;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            ExitedIds.Add(id);
            return default;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            Transitions.Add((from, to));
            return default;
        }

        public ValueTask OnStateFailed(NodeId id, Exception ex, CancellationToken ct = default)
        {
            FailedExceptions.Add(ex);
            return default;
        }

        public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
        {
            Started = true;
            return default;
        }

        public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
        {
            CompletedResult = result;
            return default;
        }

        public ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
            CancellationToken ct = default)
        {
            StatusChanges.Add((prev, next));
            return default;
        }

        public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct)
        {
            LogMessages.Add(message);
            return default;
        }
    }

    /// <summary>An async state that emits a log message.</summary>
    private sealed class AsyncLoggingState(string message) : AsyncState
    {
        protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            await LogAsync(message, ct);
            return Result.Success;
        }
    }
}

