using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("stepped_execution")]
public class SteppedExecutionTests
{
    // ── Single-node graphs ──────────────────────────────────────────────

    [Test]
    public void single_node_success_completes_in_one_tick()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Result result = fsm.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
        }
    }

    [Test]
    public void single_node_failure_completes_in_one_tick()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Result result = fsm.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
        }
    }

    // ── Multi-node traversal ────────────────────────────────────────────

    [Test]
    public void two_node_graph_first_tick_returns_continue()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine();

        Result first = fsm.Execute();

        Assert.That(first, Is.EqualTo(Result.Continue));
    }

    [Test]
    public void two_node_graph_second_tick_returns_success()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();
        Result second = fsm.Execute();

        Assert.That(second, Is.EqualTo(Result.Success));
    }

    [Test]
    public void three_node_graph_returns_continue_continue_success()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Assert.That(fsm.Execute(), Is.EqualTo(Result.Continue));
        Assert.That(counter, Is.EqualTo(1));

        Assert.That(fsm.Execute(), Is.EqualTo(Result.Continue));
        Assert.That(counter, Is.EqualTo(2));

        Assert.That(fsm.Execute(), Is.EqualTo(Result.Success));
        Assert.That(counter, Is.EqualTo(3));
    }

    [Test]
    public void failure_mid_graph_stops_traversal_immediately()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .To(() => { counter++; return Result.Failure; })
            .To(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute(); // node 1 → Continue
        Result second = fsm.Execute(); // node 2 → Failure (node 3 never runs)

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.EqualTo(Result.Failure));
            Assert.That(counter, Is.EqualTo(2));
        }
    }

    // ── Status tracking ─────────────────────────────────────────────────

    [Test]
    public void status_is_created_before_first_tick()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Created));
    }

    [Test]
    public void status_is_running_between_ticks()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine();

        fsm.Execute(); // transitions to node 2, returns Continue

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Running));
    }

    [Test]
    public void status_is_completed_after_terminal_tick_with_manual_policy()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    [Test]
    public void status_is_ready_after_terminal_tick_with_auto_policy()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Auto);

        fsm.Execute();

        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Ready));
    }

    // ── Exception handling ──────────────────────────────────────────────

    [Test]
    public void exception_in_node_transitions_to_failed()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    [Test]
    public void exception_bubbles_to_caller()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToStateMachine();

        Assert.Throws<ApplicationException>(() => fsm.Execute());
    }

    // ── Restart policies ────────────────────────────────────────────────

    [Test]
    public void tick_on_completed_machine_throws_with_manual_policy()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);
        fsm.Execute();

        Assert.Throws<InvalidOperationException>(() => fsm.Execute());
    }

    [Test]
    public void tick_on_completed_machine_returns_cached_result_with_ignore_policy()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Ignore);

        Result first = fsm.Execute();
        Result second = fsm.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(Result.Success));
            Assert.That(second, Is.EqualTo(Result.Success));
            Assert.That(counter, Is.EqualTo(1), "Node logic must not re-run under Ignore policy");
        }
    }

    [Test]
    public void tick_after_reset_restarts_from_initial_node()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();
        Assert.That(counter, Is.EqualTo(1));

        fsm.Reset();
        fsm.Execute();
        Assert.That(counter, Is.EqualTo(2));
    }

    [Test]
    public void auto_policy_allows_immediate_retick_without_explicit_reset()
    {
        int counter = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() => { counter++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Auto);

        for (int i = 0; i < 5; i++)
            fsm.Execute();

        Assert.That(counter, Is.EqualTo(5));
    }

    // ── Multi-frame node (node returns Continue) ─────────────────────────

    [Test]
    public void multi_frame_state_enter_called_once_exit_called_once()
    {
        int enterCount = 0, exitCount = 0, runCount = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(new RelayState(
                run: () => { runCount++; return runCount < 3 ? Result.Continue : Result.Success; },
                onEnter: () => enterCount++,
                onExit: () => exitCount++))
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute();
        fsm.Execute();
        fsm.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enterCount, Is.EqualTo(1), "OnEnter must fire exactly once");
            Assert.That(exitCount, Is.EqualTo(1), "OnExit must fire exactly once after terminal result");
            Assert.That(runCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void node_returning_continue_stays_on_same_node()
    {
        int callCount = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() =>
            {
                callCount++;
                return callCount < 3 ? Result.Continue : Result.Success;
            })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Result r1 = fsm.Execute(); // node called (count=1), returns Continue
        Result r2 = fsm.Execute(); // node called again (count=2), returns Continue
        Result r3 = fsm.Execute(); // node called again (count=3), returns Success

        using (Assert.EnterMultipleScope())
        {
            Assert.That(r1, Is.EqualTo(Result.Continue));
            Assert.That(r2, Is.EqualTo(Result.Continue));
            Assert.That(r3, Is.EqualTo(Result.Success));
            Assert.That(callCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void multi_frame_node_followed_by_next_node()
    {
        int nodeACount = 0, nodeBCount = 0;
        StateMachine fsm = GraphBuilder
            .StartWith(() =>
            {
                nodeACount++;
                return nodeACount < 2 ? Result.Continue : Result.Success;
            })
            .To(() => { nodeBCount++; return Result.Success; })
            .ToStateMachine();
        fsm.SetResetPolicy(RestartPolicy.Manual);

        fsm.Execute(); // nodeA (count=1), Continue
        fsm.Execute(); // nodeA (count=2), Success → transitions to nodeB, returns Continue
        fsm.Execute(); // nodeB (count=1), Success

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nodeACount, Is.EqualTo(2));
            Assert.That(nodeBCount, Is.EqualTo(1));
        }
    }

    // ── Re-entrance guard ───────────────────────────────────────────────

    [Test]
    public void re_entrance_is_blocked()
    {
        StateMachine? fsm = null;
        fsm = GraphBuilder
            .StartWith(() =>
            {
                Assert.Throws<InvalidOperationException>(() => fsm!.Execute());
                return Result.Success;
            })
            .ToStateMachine();

        fsm.Execute();
    }

    // ── Observer callbacks ──────────────────────────────────────────────

    [Test]
    public void observer_receives_started_on_first_tick()
    {
        var observer = new RecordingObserver();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.Started, Is.True);
    }

    [Test]
    public void observer_receives_transition_between_ticks()
    {
        var observer = new RecordingObserver();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine(observer);

        fsm.Execute(); // node 1 completes, transition fires

        Assert.That(observer.Transitions, Has.Count.EqualTo(1));
    }

    [Test]
    public void observer_receives_completed_on_terminal_tick()
    {
        var observer = new RecordingObserver();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(observer);

        fsm.Execute();

        Assert.That(observer.CompletedResult, Is.EqualTo(Result.Success));
    }

    [Test]
    public void observer_receives_status_changes_across_ticks()
    {
        var observer = new RecordingObserver();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine(observer);
        fsm.SetResetPolicy(RestartPolicy.Manual);

        // Tick 1: Created → Starting → Running (transition) → Running
        fsm.Execute();
        // Tick 2: Running → Completed
        fsm.Execute();

        Assert.That(observer.StatusChanges[0], Is.EqualTo((ExecutionStatus.Created, ExecutionStatus.Starting)));
        Assert.That(observer.StatusChanges[1], Is.EqualTo((ExecutionStatus.Starting, ExecutionStatus.Running)));
    }

    // ── Result struct ───────────────────────────────────────────────────

    [Test]
    public void result_struct_should_have_correct_properties()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Result.Success.IsSuccess, Is.True);
            Assert.That(Result.Success.IsFailure, Is.False);
            Assert.That(Result.Success.IsCompleted, Is.True);

            Assert.That(Result.Failure.IsSuccess, Is.False);
            Assert.That(Result.Failure.IsFailure, Is.True);
            Assert.That(Result.Failure.IsCompleted, Is.True);

            Assert.That(Result.Continue.IsSuccess, Is.False);
            Assert.That(Result.Continue.IsFailure, Is.False);
            Assert.That(Result.Continue.IsCompleted, Is.False);
        }
    }

    [Test]
    public void result_ok_and_fail_factories_should_carry_message()
    {
        Result ok = Result.Ok("all good");
        Result fail = Result.Fail("went wrong");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok.IsSuccess, Is.True);
            Assert.That(ok.Message, Is.EqualTo("all good"));
            Assert.That(ok, Is.EqualTo(Result.Success)); // equality by code

            Assert.That(fail.IsFailure, Is.True);
            Assert.That(fail.Message, Is.EqualTo("went wrong"));
            Assert.That(fail, Is.EqualTo(Result.Failure)); // equality by code
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class RecordingObserver : IStateMachineObserver
    {
        public readonly List<NodeId> EnteredIds = [];
        public readonly List<NodeId> ExitedIds = [];
        public readonly List<(NodeId From, NodeId To)> Transitions = [];
        public readonly List<(ExecutionStatus Prev, ExecutionStatus Next)> StatusChanges = [];
        public bool Started;
        public Result? CompletedResult;

        public void OnStateEntered(NodeId id) => EnteredIds.Add(id);
        public void OnStateExited(NodeId id) => ExitedIds.Add(id);
        public void OnTransition(NodeId from, NodeId to) => Transitions.Add((from, to));
        public void OnStateMachineStarted(NodeId graphId) => Started = true;
        public void OnStateMachineCompleted(NodeId graphId, Result result) => CompletedResult = result;
        public void StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next)
            => StatusChanges.Add((prev, next));
    }
}
