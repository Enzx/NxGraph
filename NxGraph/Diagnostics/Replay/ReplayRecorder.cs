using System.Runtime.CompilerServices;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Replay;

public sealed class ReplayRecorder(int capacity = 256) : IAsyncStateMachineObserver, IStateMachineObserver
{
    // ReSharper disable UnusedMember.Global
    // ReSharper disable once MemberCanBePrivate.Global
    public int Count { get; private set; }
    public int Capacity => _events.Length;

    /// <summary>
    /// Number of events the ring buffer has dropped due to capacity overflow. Increments
    /// every time Record overwrites an existing slot. Useful for callers that need to
    /// distinguish "complete capture" from "lossy capture" without scanning the buffer.
    /// </summary>
    public long DroppedCount { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        Count = 0;
        _head = 0;
        _tail = 0;
        DroppedCount = 0;
    }


    public ReplayEvent this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int phys = (_head + index) % _events.Length;
            return _events[phys];
        }
    }
    // ReSharper restore UnusedMember.Global
    // ReSharper restore once MemberCanBePrivate.Global

    // Ring buffer storage
    private readonly ReplayEvent[] _events = new ReplayEvent[Math.Max(1, capacity)];

    // Index of the oldest element
    private int _head;

    // Index where the next write will occur
    private int _tail;

    // Returns a copy of the events in chronological order.
    public ReadOnlyMemory<ReplayEvent> GetEvents()
    {
        if (Count == 0) return ReadOnlyMemory<ReplayEvent>.Empty;

        ReplayEvent[] result = new ReplayEvent[Count]; //okay for now, could be optimized with ArrayPool if needed

        if (_head < _tail)
        {
            Array.Copy(_events, _head, result, 0, Count);
        }
        else
        {
            // Wrapped: two ranges
            int first = _events.Length - _head;
            Array.Copy(_events, _head, result, 0, first);
            Array.Copy(_events, 0, result, first, _tail);
        }

        return result.AsMemory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Record(ReplayEvent evt)
    {
        _events[_tail] = evt;

        if (Count == _events.Length)
        {
            _head = (_head + 1) % _events.Length;
            DroppedCount++;
        }
        else
        {
            Count++;
        }

        _tail = (_tail + 1) % _events.Length;
    }

    private static long CurrentTimestamp => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateEntered, id, timestamp: CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateExited, id, timestamp: CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.Transition, from, to, timestamp: CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnStateFailed(NodeId id, Exception ex, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateFailed, id, null, ex.ToString(), CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnStateMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineReset, graphId, timestamp: CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineStarted, graphId, timestamp: CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineCompleted, graphId, null, result.ToString(), CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnStateMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StateMachineCancelled, graphId, timestamp: CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        Record(new ReplayEvent(EventType.StatusChanged, graphId, null, $"{prev}->{next}", CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct)
    {
        Record(new ReplayEvent(EventType.Log, nodeId, null, message, CurrentTimestamp));
        return ResultHelpers.CompletedTask;
    }

    // ── Synchronous observer parity ─────────────────────────────────────
    // Mirrors the async callbacks above. Sync StateMachine instances previously emitted no
    // replay events because ReplayRecorder only implemented the async observer interface.

    void IStateMachineObserver.OnStateEntered(NodeId id)
        => Record(new ReplayEvent(EventType.StateEntered, id, timestamp: CurrentTimestamp));

    void IStateMachineObserver.OnStateExited(NodeId id)
        => Record(new ReplayEvent(EventType.StateExited, id, timestamp: CurrentTimestamp));

    void IStateMachineObserver.OnTransition(NodeId from, NodeId to)
        => Record(new ReplayEvent(EventType.Transition, from, to, timestamp: CurrentTimestamp));

    void IStateMachineObserver.OnStateFailed(NodeId id, Exception ex)
        => Record(new ReplayEvent(EventType.StateFailed, id, null, ex.ToString(), CurrentTimestamp));

    void IStateMachineObserver.OnStateMachineReset(NodeId graphId)
        => Record(new ReplayEvent(EventType.StateMachineReset, graphId, timestamp: CurrentTimestamp));

    void IStateMachineObserver.OnStateMachineStarted(NodeId graphId)
        => Record(new ReplayEvent(EventType.StateMachineStarted, graphId, timestamp: CurrentTimestamp));

    void IStateMachineObserver.OnStateMachineCompleted(NodeId graphId, Result result)
        => Record(new ReplayEvent(EventType.StateMachineCompleted, graphId, null, result.ToString(), CurrentTimestamp));

    void IStateMachineObserver.StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next)
        => Record(new ReplayEvent(EventType.StatusChanged, graphId, null, $"{prev}->{next}", CurrentTimestamp));

    void IStateMachineObserver.OnLogReport(NodeId nodeId, string message)
        => Record(new ReplayEvent(EventType.Log, nodeId, null, message, CurrentTimestamp));
}