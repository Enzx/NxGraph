using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("state_observation")]
public class StateObservationTests
{
    private class FsmObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> ObservedStates = [];

        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            ObservedStates.Add($"Entered: {id}");
            return default;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            ObservedStates.Add($"Exited: {id}");
            return default;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            ObservedStates.Add($"Transitioned from {from} to {to}");
            return default;
        }

        public ValueTask OnStateMachineReset(NodeId graphId, CancellationToken ct = default)
        {
            ObservedStates.Add("FSM:Reset");
            return default;
        }

        public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
        {
            ObservedStates.Add("FSM:Started");
            return default;
        }

        public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
        {
            ObservedStates.Add($"FSM:Completed:result: {result}");
            return default;
        }

        public ValueTask OnStateMachineCancelled(NodeId graphId, CancellationToken ct = default)
        {
            ObservedStates.Add("FSM:Cancelled");
            return default;
        }

        public ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
            CancellationToken ct = default)
        {
            ObservedStates.Add($"FSM:StatusChanged:{prev}>{next}");
            return default;
        }
    }

    private static readonly string[] Expected =
    [
        "FSM:StatusChanged:Created>Starting",
        "FSM:Started",
        "Entered: (0)",
        "FSM:StatusChanged:Starting>Running",
        "Exited: (0)",
        "FSM:StatusChanged:Running>Transitioning",
        "Transitioned from (0) to (1)",
        "Entered: (1)",
        "FSM:StatusChanged:Transitioning>Running",
        "Exited: (1)",
        "FSM:StatusChanged:Running>Completed",
        "FSM:Completed:result: Success",
        "FSM:StatusChanged:Completed>Resetting",
        "FSM:Reset",
        "FSM:StatusChanged:Resetting>Ready"
    ];

    [Test]
    public async Task should_observe_state_execution()
    {
        FsmObserver observer = new();
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.That(observer.ObservedStates, Is.EquivalentTo(Expected));
    }

    [Test]
    public async Task should_observe_cancellation()
    {
        FsmObserver observer = new();
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(observer);

        CancellationTokenSource cts = new();
        await cts.CancelAsync();
        Assert.That(async () => await fsm.ExecuteAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());

        Assert.That(observer.ObservedStates, Does.Contain("FSM:Cancelled"),
            "Observer should have recorded cancellation event.");
    }

    [Test]
    public async Task should_observe_reset_on_manual_reset()
    {
        FsmObserver observer = new();
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(observer);
        fsm.SetAutoReset(false);
        await fsm.ExecuteAsync();

        await fsm.Reset();


        Assert.That(observer.ObservedStates, Does.Contain("FSM:Reset"), "Observer should have recorded reset event.");
    }
}