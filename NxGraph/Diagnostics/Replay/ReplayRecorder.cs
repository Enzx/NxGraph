using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Replay;

public sealed class ReplayRecorder(int initialCapacity = 256) : IAsyncStateMachineObserver
{
    // ReSharper disable once MemberCanBePrivate.Global
    public int Count { get; private set; }
    // ReSharper disable once UnusedMember.Global
    public void Clear() => Count = 0;
    // ReSharper disable once UnusedMember.Global
    public ReplayEvent this[int index] => _events[index];
    
    private ReplayEvent[] _events = new ReplayEvent[initialCapacity];

    public ReadOnlyMemory<ReplayEvent> GetEvents() => _events.AsMemory(0, Count);

    private void Record(ReplayEvent evt)
    {
        if (Count == _events.Length)
        {
            Array.Resize(ref _events, _events.Length * 2);
        }

        _events[Count++] = evt;
    }
    
    private static long CurrentTimestamp => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateEntered, id, timestamp: CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateExited, id, timestamp: CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.Transition, from, to, timestamp: CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateFailed(NodeId id, Exception ex, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateFailed, id, null, ex.ToString(), CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineReset, graphId, timestamp: CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineStarted, graphId, timestamp: CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineCompleted, graphId, null, result.ToString(), CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineCancelled, graphId, timestamp: CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StatusChanged, graphId, null, $"{prev}->{next}", CurrentTimestamp));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct)
    {
        Record(new ReplayEvent(EventType.Log, nodeId, null, message, CurrentTimestamp));
        return ValueTask.CompletedTask;
    }
}