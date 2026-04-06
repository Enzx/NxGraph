using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("sync_state_machine")]
public class StateMachineTests
{
    // ── Core execution ──────────────────────────────────────────────────

    [Test]
    public void should_return_success_when_single_state_succeeds()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void should_return_failure_when_single_state_fails()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    // ── Transition flow (Execute should full-run) ───────────────────────

    [Test]
    public void execute_should_traverse_two_states_and_succeed()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .ToStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(2));
    }

    [Test]
    public void execute_should_stop_on_failure_of_second_state()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Failure; })
            .To(() => { counter++; return Result.Success; })
            .ToStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Failure));
        // third state should never run
        Assert.That(counter, Is.EqualTo(2));
    }

    [Test]
    public void execute_should_traverse_three_states_and_succeed()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .ToStateMachine();

        Result result = fsm.Execute();

        Assert.That(result, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(3));
    }

    // ── Status transitions ──────────────────────────────────────────────

    [Test]
    public void status_should_be_created_before_execution()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
    }


    [Test]
    public void status_should_be_ready_after_successful_execution_with_auto_reset()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Auto);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public void status_should_be_completed_after_execution_without_auto_reset()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    [Test]
    public void status_should_be_failed_after_failure_without_auto_reset()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    // ── Reset behaviour ─────────────────────────────────────────────────

    [Test]
    public void reset_from_completed_should_move_to_ready()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);
        fsm.Execute();

        Result resetResult = fsm.Reset();

        Assert.That(resetResult, Is.EqualTo(Result.Success));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public void reset_from_created_should_succeed_immediately()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();

        Result resetResult = fsm.Reset();

        Assert.That(resetResult, Is.EqualTo(Result.Success));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
    }

    [Test]
    public void can_execute_again_after_reset()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();
        Assert.That(counter, Is.EqualTo(1));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        fsm.Reset();
        fsm.Execute();
        Assert.That(counter, Is.EqualTo(2));
    }

    [Test]
    public void execute_without_reset_should_throw_when_completed()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    [Test]
    public void execute_without_reset_should_throw_when_failed()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    [Test]
    public void execute_should_be_silently_ignored_after_completed_when_reset_policy_is_ignore()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() =>
            {
                counter++;
                return Result.Success;
            })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Ignore);

        Result first = fsm.Execute();
        Assert.That(first, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(1));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));

        Result second = fsm.Execute();
        Assert.That(second, Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(1), "Ignore policy must not re-run node logic");
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    [Test]
    public void execute_should_run_again_after_manual_reset_when_reset_policy_is_ignore()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() =>
            {
                counter++;
                return Result.Success;
            })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Ignore);

        fsm.Execute();
        Assert.That(counter, Is.EqualTo(1));

        fsm.Reset();

        fsm.Execute();
        Assert.That(counter, Is.EqualTo(2));
    }

    // ── Re-entrance guard ───────────────────────────────────────────────

    [Test]
    public void should_throw_on_re_entrance()
    {
        StateMachine? fsm = null;
        fsm = GraphBuilder
            .StartWith(() =>
            {
                // Attempt re-entrant execution
                Assert.Throws<InvalidOperationException>(() => fsm!.Execute());
                return Result.Success;
            })
            .ToStateMachine();

        fsm.Execute();
    }

    [Test]
    public void observer_should_receive_transitions()
    {
        var observer = new RecordingObserver();

        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.Transitions, Has.Count.EqualTo(1));
    }

    [Test]
    public void observer_should_receive_started_and_completed()
    {
        var observer = new RecordingObserver();

        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.Started, Is.True);
        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Success));
    }

    [Test]
    public void observer_should_receive_failed_result()
    {
        var observer = new RecordingObserver();

        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Failure));
    }

    [Test]
    public void observer_should_receive_status_changes_in_order()
    {
        var observer = new RecordingObserver();

        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(observer);
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

        StateMachine fsm = GraphBuilder
            .StartWith(new LoggingState("hello"))
            .ToStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.LogMessages, Has.Count.EqualTo(1));
        Assert.That(observer.LogMessages[0], Is.EqualTo("hello"));
    }
 


    // ── Typed agent ─────────────────────────────────────────────────────

    [Test]
    public void should_propagate_agent_to_typed_states()
    {
        string? captured = null;
        StateMachine<string> fsm = GraphBuilder
            .StartWith(new AgentCapturingSyncState(v => captured = v))
            .ToStateMachine<string>()
            .WithAgent("TestAgent");

        fsm.Execute();

        Assert.That(captured, Is.EqualTo("TestAgent"));
    }

    // ── Exception propagation ───────────────────────────────────────────

    [Test]
    public void should_propagate_exception_from_state()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    [Test]
    public void should_auto_reset_after_exception()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Auto);

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    // ── Relay state lifecycle ──────────────────────────────────────────

    [Test]
    public void relay_state_should_call_on_enter_and_on_exit()
    {
        bool entered = false, exited = false;
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => Result.Success,
                onEnter: () => entered = true,
                onExit: () => exited = true))
            .ToStateMachine();

        fsm.Execute();

        Assert.That(entered, Is.True);
        Assert.That(exited, Is.True);
    }

    [Test]
    public void relay_state_on_exit_should_run_even_on_failure()
    {
        bool exited = false;
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => Result.Failure,
                onExit: () => exited = true))
            .ToStateMachine();

        fsm.Execute();

        Assert.That(exited, Is.True);
    }

    [Test]
    public void relay_state_on_exit_should_run_even_on_exception()
    {
        bool exited = false;
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => throw new ApplicationException("boom"),
                onExit: () => exited = true))
            .ToStateMachine();

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(exited, Is.True);
    }

    [Test]
    public void relay_state_lifecycle_order_should_be_enter_run_exit()
    {
        List<string> log = [];
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => { log.Add("run"); return Result.Success; },
                onEnter: () => log.Add("enter"),
                onExit: () => log.Add("exit")))
            .ToStateMachine();

        fsm.Execute();

        Assert.That(log, Is.EqualTo(["enter", "run", "exit"]));
    }

    [Test]
    public void should_propagate_exception_from_on_enter_sync()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => Result.Success,
                onEnter: () => throw new InvalidOperationException("enter boom")))
            .ToStateMachine();

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    [Test]
    public void should_propagate_exception_from_on_exit_sync()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => Result.Success,
                onExit: () => throw new NotSupportedException("exit boom")))
            .ToStateMachine();

        Assert.Throws<NotSupportedException>(() => fsm.Execute());
    }

    // ── Observer: OnStateFailed ────────────────────────────────────────

    [Test]
    public void observer_should_receive_on_state_failed_on_exception()
    {
        var observer = new FailRecordingObserver();
        StateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToStateMachine(observer);
        fsm.SetAutoReset(false);

        Assert.Throws<ApplicationException>(() => fsm.Execute());

        Assert.Multiple(() =>
        {
            Assert.That(observer.FailedExceptions, Has.Count.EqualTo(1));
            Assert.That(observer.FailedExceptions[0], Is.TypeOf<ApplicationException>());
        });
    }

    // ── Observer: exception bubbling ───────────────────────────────────

    [Test]
    public void observer_exception_in_on_entered_should_bubble_to_caller()
    {
        var observer = new ExplosiveSyncObserver();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(observer);

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    // ── Reset from failed ──────────────────────────────────────────────

    [Test]
    public void reset_from_failed_should_move_to_ready()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();
        fsm.SetAutoReset(false);
        fsm.Execute();

        Result resetResult = fsm.Reset();

        Assert.That(resetResult, Is.EqualTo(Result.Success));
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    [Test]
    public void should_auto_reset_to_ready_after_failure()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();
        fsm.SetAutoReset(true);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }
    

    // ── Mixed sync + ILogic nodes ───────────────────────────────────────

    [Test]
    public void should_throw_when_node_does_not_implement_ISyncLogic()
    {
        // AsyncRelayState (async only) does not implement ILogic.
        StateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(_ => ResultHelpers.Success))
            .ToStateMachine();

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    // ── Multiple runs ───────────────────────────────────────────────────

    [Test]
    public void should_support_multiple_sequential_runs()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetAutoReset(true);

        for (int i = 0; i < 100; i++)
        {
            Result r = fsm.Execute();
            Assert.That(r, Is.EqualTo(Result.Success));
        }

        Assert.That(counter, Is.EqualTo(100));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class RecordingObserver : IStateMachineObserver
    {
        public readonly List<NodeId> EnteredIds = [];
        public readonly List<NodeId> ExitedIds = [];
        public readonly List<(NodeId From, NodeId To)> Transitions = [];
        public readonly List<(ExecutionStatus Prev, ExecutionStatus Next)> StatusChanges = [];
        public readonly List<string> LogMessages = [];
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
    private sealed class LoggingState(string message) : State
    {
        protected override Result OnRun()
        {
            Log(message);
            return Result.Success;
        }
    }

    /// <summary>A typed sync state that captures the agent value.</summary>
    private sealed class AgentCapturingSyncState(Action<string> capture) : State<string>
    {
        protected override Result OnRun()
        {
            capture(Agent);
            return Result.Success;
        }
    }

    private sealed class FailRecordingObserver : IStateMachineObserver
    {
        public readonly List<Exception> FailedExceptions = [];
        public void OnStateFailed(NodeId id, Exception ex) => FailedExceptions.Add(ex);
    }

    private sealed class ExplosiveSyncObserver : IStateMachineObserver
    {
        public void OnStateEntered(NodeId id) =>
            throw new InvalidOperationException("sync observer boom");
    }
}

