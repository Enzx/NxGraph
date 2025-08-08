using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("state_observation")]
public class StateObservationTests
{
    private class FsmObserver : IAsyncStateObserver
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
    }

    private static readonly string[] Expected =
    [
        "Entered: (1)", 
        "Exited: (1)", 
        "Transitioned from (1) to (2)", 
        "Entered: (2)", 
        "Exited: (2)"
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
}