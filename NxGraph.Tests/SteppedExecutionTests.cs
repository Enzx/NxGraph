using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("stepped_execution")]
public class SteppedExecutionTests
{
    // ── Single-node graph completes in one Tick() call ───────────────

    [Test]
    public void single_node_should_complete_in_one_tick()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();
        fsm.SetAutoReset(false);

        Result result = fsm.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Completed));
        }
    }

    [Test]
    public void single_node_failure_should_complete_in_one_tick()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Failure)
            .ToStateMachine();
        fsm.SetAutoReset(false);

        Result result = fsm.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
        }
    }
    
    // ── Exception in node is handled correctly ──────────────────────────

    [Test]
    public void exception_in_node_should_transition_to_failed()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(() => throw new ApplicationException("boom"))
            .ToStateMachine();
        fsm.SetAutoReset(false);

        Assert.Throws<ApplicationException>(() => fsm.Execute());
        Assert.That(fsm.Status, Is.EqualTo(ExecutionStatus.Failed));
    }

    // ── Result struct properties ────────────────────────────────────────

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
