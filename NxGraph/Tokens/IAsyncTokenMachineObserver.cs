using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tokens;

/// <summary>
/// Asynchronous observer for <see cref="AsyncTokenMachine"/> — the <c>ValueTask</c> twin of
/// <see cref="ITokenMachineObserver"/>. Every node-level event names the token it happened to.
/// All methods have default no-op implementations. Observer exceptions bubble by design.
/// </summary>
public interface IAsyncTokenMachineObserver
{
    /// <summary>A new token entered the run. The root token reports <paramref name="parentTokenId"/> -1.</summary>
    ValueTask OnTokenSpawned(int tokenId, int parentTokenId, NodeId at, CancellationToken ct = default)
    {
        return default;
    }

    /// <summary>A token left the run (terminal success/failure, join consumption, or starvation).</summary>
    ValueTask OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason, CancellationToken ct = default)
    {
        return default;
    }

    /// <summary>A join's policy was met; <paramref name="survivingTokenId"/> continues past it.</summary>
    ValueTask OnJoinFired(NodeId joinNode, int survivingTokenId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateEntered(int tokenId, NodeId id, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateExited(int tokenId, NodeId id, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnTransition(int tokenId, NodeId from, NodeId to, CancellationToken ct = default)
    {
        return default;
    }

    /// <summary>
    /// A node failed for the given token. <paramref name="ex"/> is <c>null</c> when the node
    /// returned <see cref="Result.Failure"/> without throwing (a result-based failure).
    /// </summary>
    ValueTask OnStateFailed(int tokenId, NodeId id, Exception? ex, CancellationToken ct = default)
    {
        return default;
    }

    /// <summary>A log report emitted by node logic, attributed to the token being stepped.</summary>
    ValueTask OnLogReport(int tokenId, NodeId nodeId, string message, CancellationToken ct = default)
    {
        return default;
    }

    //--- Machine lifecycle events (same shapes as the FSM observers) ---

    ValueTask OnTokenMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnTokenMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnTokenMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnTokenMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask TokenMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        return default;
    }
}
