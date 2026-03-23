using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("sync_state_machine")]
public class SyncStateMachineTests
{
    // ── Core execution ──────────────────────────────────────────────────

    [Test]
    public void should_return_success_when_single_state_succeeds()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void should_return_failure_when_single_state_fails()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToSyncStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    // ── Transition flow ─────────────────────────────────────────────────

    [Test]
    public void should_traverse_two_states_and_succeed()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToSyncStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void should_stop_on_failure_of_second_state()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Failure)
            .ToSyncStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    public void should_traverse_three_states()
    {
        int counter = 0;
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .ToSyncStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(3));
    }

    // ── Status transitions ──────────────────────────────────────────────

    [Test]
    public void status_should_be_created_before_execution()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
    }

    [Test]
    public void status_should_be_ready_after_successful_execution_with_auto_reset()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine();
        fsm.SetAutoReset(true);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public void status_should_be_completed_after_execution_without_auto_reset()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine();
        fsm.SetAutoReset(false);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    [Test]
    public void status_should_be_failed_after_failure_without_auto_reset()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToSyncStateMachine();
        fsm.SetAutoReset(false);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    // ── Reset behaviour ─────────────────────────────────────────────────

    [Test]
    public void reset_from_completed_should_move_to_ready()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine();
        fsm.SetAutoReset(false);
        fsm.Execute();

        Result resetResult = fsm.Reset();

        Assert.That(resetResult, Is.EqualTo(Result.Success));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public void reset_from_created_should_succeed_immediately()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine();

        Result resetResult = fsm.Reset();

        Assert.That(resetResult, Is.EqualTo(Result.Success));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
    }

    [Test]
    public void can_execute_again_after_reset()
    {
        int counter = 0;
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToSyncStateMachine();
        fsm.SetAutoReset(false);

        fsm.Execute();
        Assert.That(counter, Is.EqualTo(1));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        fsm.Reset();
        fsm.Execute();
        Assert.That(counter, Is.EqualTo(2));
    }

    // ── Re-entrance guard ───────────────────────────────────────────────

    [Test]
    public void should_throw_on_re_entrance()
    {
        SyncStateMachine? fsm = null;
        fsm = GraphBuilder
            .StartWith(() =>
            {
                // Attempt re-entrant execution
                Assert.Throws<InvalidOperationException>(() => fsm!.Execute());
                return Result.Success;
            })
            .ToSyncStateMachine();

        fsm.Execute();
    }

    // ── Observer callbacks ──────────────────────────────────────────────

    [Test]
    public void observer_should_receive_state_entered_and_exited()
    {
        var observer = new RecordingObserver();

        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToSyncStateMachine(observer);

        fsm.Execute();

        // Two states entered (state 0 and state 1)
        Assert.That(observer.EnteredIds, Has.Count.EqualTo(2));
        // Both states exited
        Assert.That(observer.ExitedIds, Has.Count.EqualTo(2));
    }

    [Test]
    public void observer_should_receive_transitions()
    {
        var observer = new RecordingObserver();

        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToSyncStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.Transitions, Has.Count.EqualTo(1));
    }

    [Test]
    public void observer_should_receive_started_and_completed()
    {
        var observer = new RecordingObserver();

        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.Started, Is.True);
        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Success));
    }

    [Test]
    public void observer_should_receive_failed_result()
    {
        var observer = new RecordingObserver();

        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToSyncStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Failure));
    }

    [Test]
    public void observer_should_receive_status_changes_in_order()
    {
        var observer = new RecordingObserver();

        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToSyncStateMachine(observer);
        fsm.SetAutoReset(false);

        fsm.Execute();

        // Created -> Starting -> Running -> Completed
        Assert.That(observer.StatusChanges, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(observer.StatusChanges[0], Is.EqualTo((ExecutionStatus.Created, ExecutionStatus.Starting)));
        Assert.That(observer.StatusChanges[1], Is.EqualTo((ExecutionStatus.Starting, ExecutionStatus.Running)));
        Assert.That(observer.StatusChanges[2], Is.EqualTo((ExecutionStatus.Running, ExecutionStatus.Completed)));
    }

    [Test]
    public void observer_should_receive_log_reports()
    {
        var observer = new RecordingObserver();

        SyncStateMachine fsm = GraphBuilder
            .StartWith(new LoggingSyncState("hello"))
            .ToSyncStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.LogMessages, Has.Count.EqualTo(1));
        Assert.That(observer.LogMessages[0], Is.EqualTo("hello"));
    }

    // ── If/Else branching ───────────────────────────────────────────────

    [Test]
    public void should_take_then_branch_when_predicate_is_true()
    {
        int thenCalled = 0, elseCalled = 0;

        SyncStateMachine fsm = GraphBuilder.Start()
            .If(() => true)
            .Then(() => { thenCalled++; return Result.Success; })
            .Else(() => { elseCalled++; return Result.Success; })
            .ToSyncStateMachine();

        fsm.Execute();

        Assert.That(thenCalled, Is.EqualTo(1));
        Assert.That(elseCalled, Is.EqualTo(0));
    }

    [Test]
    public void should_take_else_branch_when_predicate_is_false()
    {
        int thenCalled = 0, elseCalled = 0;

        SyncStateMachine fsm = GraphBuilder.Start()
            .If(() => false)
            .Then(() => { thenCalled++; return Result.Success; })
            .Else(() => { elseCalled++; return Result.Success; })
            .ToSyncStateMachine();

        fsm.Execute();

        Assert.That(thenCalled, Is.EqualTo(0));
        Assert.That(elseCalled, Is.EqualTo(1));
    }

    // ── Switch branching ────────────────────────────────────────────────

    [Test]
    public void switch_should_execute_matching_case()
    {
        int aCalled = 0, bCalled = 0;

        SyncStateMachine fsm = GraphBuilder.Start()
            .Switch(() => "B")
            .Case("A", () => { aCalled++; return Result.Success; })
            .Case("B", () => { bCalled++; return Result.Success; })
            .End()
            .ToSyncStateMachine();

        fsm.Execute();

        Assert.That(aCalled, Is.EqualTo(0));
        Assert.That(bCalled, Is.EqualTo(1));
    }

    [Test]
    public void switch_should_execute_default_when_no_case_matches()
    {
        int aCalled = 0, defaultCalled = 0;

        SyncStateMachine fsm = GraphBuilder.Start()
            .Switch(() => "X")
            .Case("A", () => { aCalled++; return Result.Success; })
            .Default(() => { defaultCalled++; return Result.Success; })
            .End()
            .ToSyncStateMachine();

        fsm.Execute();

        Assert.That(aCalled, Is.EqualTo(0));
        Assert.That(defaultCalled, Is.EqualTo(1));
    }

    // ── Typed agent ─────────────────────────────────────────────────────

    [Test]
    public void should_propagate_agent_to_typed_states()
    {
        string? captured = null;
        SyncStateMachine<string> fsm = GraphBuilder
            .StartWith(new AgentCapturingSyncState(v => captured = v))
            .ToSyncStateMachine<string>()
            .WithAgent("TestAgent");

        fsm.Execute();

        Assert.That(captured, Is.EqualTo("TestAgent"));
    }

    // ── Exception propagation ───────────────────────────────────────────

    [Test]
    public void should_propagate_exception_from_state()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToSyncStateMachine();
        fsm.SetAutoReset(false);

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    [Test]
    public void should_auto_reset_after_exception()
    {
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToSyncStateMachine();
        fsm.SetAutoReset(true);

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    // ── Mixed sync + ILogic nodes ───────────────────────────────────────

    [Test]
    public void should_throw_when_node_does_not_implement_ISyncLogic()
    {
        // RelayState (async only) does not implement ISyncLogic.
        SyncStateMachine fsm = GraphBuilder
            .StartWith(new RelayState(_ => ResultHelpers.Success))
            .ToSyncStateMachine();

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    // ── Multiple runs ───────────────────────────────────────────────────

    [Test]
    public void should_support_multiple_sequential_runs()
    {
        int counter = 0;
        SyncStateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToSyncStateMachine();
        fsm.SetAutoReset(true);

        for (int i = 0; i < 100; i++)
        {
            Result r = fsm.Execute();
            Assert.That(r, Is.EqualTo(Result.Success));
        }

        Assert.That(counter, Is.EqualTo(100));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class RecordingObserver : ISyncStateMachineObserver
    {
        public readonly List<NodeId> EnteredIds = new();
        public readonly List<NodeId> ExitedIds = new();
        public readonly List<(NodeId From, NodeId To)> Transitions = new();
        public readonly List<(ExecutionStatus Prev, ExecutionStatus Next)> StatusChanges = new();
        public readonly List<string> LogMessages = new();
        public bool Started;
        public Result? CompletedResult;

        public void OnStateEntered(NodeId id) => EnteredIds.Add(id);
        public void OnStateExited(NodeId id) => ExitedIds.Add(id);
        public void OnTransition(NodeId from, NodeId to) => Transitions.Add((from, to));

        public void OnStateMachineStarted(NodeId graphId) => Started = true;

        public void OnStateMachineCompleted(NodeId graphId, Result result) => CompletedResult = result;

        public void StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next)
            => StatusChanges.Add((prev, next));

        public void OnLogReport(NodeId nodeId, string message) => LogMessages.Add(message);
    }

    /// <summary>A sync state that emits a log message.</summary>
    private sealed class LoggingSyncState(string message) : SyncState
    {
        protected override Result OnRun()
        {
            Log(message);
            return Result.Success;
        }
    }

    /// <summary>A typed sync state that captures the agent value.</summary>
    private sealed class AgentCapturingSyncState(Action<string> capture) : SyncState<string>
    {
        protected override Result OnRun()
        {
            capture(Agent);
            return Result.Success;
        }
    }
}

