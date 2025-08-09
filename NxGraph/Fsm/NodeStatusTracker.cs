// NxGraph/Fsm/NodeStatusTracker.cs
using System.Collections.Concurrent;
using NxGraph.Graphs;

namespace NxGraph.Fsm
{
    /// <summary>
    /// Tracks per-node execution status for a single run.
    /// </summary>
    internal sealed class NodeStatusTracker
    {
        private readonly ConcurrentDictionary<NodeId, ExecutionStatus> _map = new();
        internal ExecutionStatus Get(NodeId id) => _map.GetValueOrDefault(id, ExecutionStatus.Created);
        internal void SetRunning(NodeId id) => _map[id] = ExecutionStatus.Running;
        internal void SetCompleted(NodeId id) => _map[id] = ExecutionStatus.Completed;
        internal void SetFailed(NodeId id) => _map[id] = ExecutionStatus.Failed;
        internal void SetCancelled(NodeId id) => _map[id] = ExecutionStatus.Cancelled;
    }
}
