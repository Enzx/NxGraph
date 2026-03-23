using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="IAsyncStateMachineObserver"/>.
/// Every callback is <c>void</c> – no allocations, no async machinery.
/// All methods have default no-op implementations so consumers only override what they need.
/// </summary>
public interface IStateMachineObserver
{
    void OnStateEntered(NodeId id) { }

    void OnStateExited(NodeId id) { }

    void OnTransition(NodeId from, NodeId to) { }

    void OnStateFailed(NodeId id, Exception ex) { }

    //--- State Machine Lifecycle Events ---

    void OnStateMachineReset(NodeId graphId) { }

    void OnStateMachineStarted(NodeId graphId) { }

    void OnStateMachineCompleted(NodeId graphId, Result result) { }

    void StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next) { }

    void OnLogReport(NodeId nodeId, string message) { }
}

