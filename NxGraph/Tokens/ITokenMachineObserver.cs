using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tokens;

/// <summary>
/// Synchronous observer for <see cref="TokenMachine"/>. The FSM observer surface gains a token
/// identity dimension: every node-level event names the token it happened to. All methods have
/// default no-op implementations so consumers only override what they need; callbacks are
/// <c>void</c> — no allocations, no async machinery. Observer exceptions bubble by design.
/// </summary>
public interface ITokenMachineObserver
{
    /// <summary>A new token entered the run. The root token reports <paramref name="parentTokenId"/> -1.</summary>
    void OnTokenSpawned(int tokenId, int parentTokenId, NodeId at) { }

    /// <summary>A token left the run (terminal success/failure, join consumption, or starvation).</summary>
    void OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason) { }

    /// <summary>A join's policy was met; <paramref name="survivingTokenId"/> continues past it.</summary>
    void OnJoinFired(NodeId joinNode, int survivingTokenId) { }

    void OnStateEntered(int tokenId, NodeId id) { }

    void OnStateExited(int tokenId, NodeId id) { }

    void OnTransition(int tokenId, NodeId from, NodeId to) { }

    /// <summary>
    /// A node failed for the given token. <paramref name="ex"/> is <c>null</c> when the node
    /// returned <see cref="Result.Failure"/> without throwing (a result-based failure).
    /// </summary>
    void OnStateFailed(int tokenId, NodeId id, Exception? ex) { }

    /// <summary>A log report emitted by node logic, attributed to the token being stepped.</summary>
    void OnLogReport(int tokenId, NodeId nodeId, string message) { }

    //--- Machine lifecycle events (same shapes as the FSM observers) ---

    void OnTokenMachineReset(NodeId graphId) { }

    void OnTokenMachineStarted(NodeId graphId) { }

    void OnTokenMachineCompleted(NodeId graphId, Result result) { }

    void TokenMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next) { }
}
