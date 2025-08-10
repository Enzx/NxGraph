using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxGraph.Graphs;

// ReSharper disable ConvertToAutoPropertyWithPrivateSetter
// ReSharper disable once ClassNeverInstantiated.Global

namespace NxGraph.Fsm;

/// <summary>
/// Allocation-free, array-backed per-node status tracker for a single FSM run.
/// One instance is intended per run; reuse via <see cref="Reset"/> if you execute the same graph repeatedly.
/// </summary>
public sealed class NodeStatusTracker
{
    private byte[] _statuses = [];
    private long[] _enterTicks = [];
    private long[] _exitTicks = [];

    private int _length;
    private bool _initialized;

    public bool IsInitialized => _initialized;
    public int Length => _length;

    /// <summary>
    /// Prepare the tracker for a specific graph. Sizes arrays exactly to Graph.NodeCount.
    /// </summary>
    public void Initialize(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        int n = graph.NodeCount;
        if (_statuses.Length != n)
        {
            _statuses = new byte[n];
            _enterTicks = new long[n];
            _exitTicks = new long[n];
        }
        else
        {
            Array.Clear(_statuses);
            Array.Clear(_enterTicks);
            Array.Clear(_exitTicks);
        }

        _length = n;
        _initialized = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkEntered(NodeId id)
    {
        int idx = id.Index;
        if ((uint)idx >= (uint)_length)
        {
            return;
        }

        Volatile.Write(ref _statuses[idx], (byte)ExecutionStatus.Running);
        _enterTicks[idx] = Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkExited(NodeId id, bool success)
    {
        int idx = id.Index;
        if ((uint)idx >= (uint)_length)
        {
            return;
        }

        Volatile.Write(ref _statuses[idx], success ? (byte)ExecutionStatus.Completed : (byte)ExecutionStatus.Failed);
        _exitTicks[idx] = Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkCancelled(NodeId id)
    {
        int idx = id.Index;
        if ((uint)idx >= (uint)_length)
        {
            return;
        }

        Volatile.Write(ref _statuses[idx], (byte)ExecutionStatus.Cancelled);
        _exitTicks[idx] = Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkTransitioned(NodeId from, NodeId to)
    {
        int fromIdx = from.Index;
        int toIdx = to.Index;

        if ((uint)fromIdx >= (uint)_length || (uint)toIdx >= (uint)_length)
        {
            return;
        }

        Volatile.Write(ref _statuses[fromIdx], (byte)ExecutionStatus.Transitioning);
        _exitTicks[fromIdx] = Stopwatch.GetTimestamp();

        Volatile.Write(ref _statuses[toIdx], (byte)ExecutionStatus.Running);
        _enterTicks[toIdx] = Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // ReSharper disable once UnusedMember.Global
    public ExecutionStatus Get(NodeId id)
    {
        int idx = id.Index;
        return (uint)idx >= (uint)_length
            ? ExecutionStatus.Created
            : (ExecutionStatus)Volatile.Read(ref _statuses[idx]);
    }

    public TimeSpan GetDuration(NodeId id)
    {
        int idx = id.Index;
        if ((uint)idx >= (uint)_length)
        {
            return TimeSpan.Zero;
        }

        long start = Volatile.Read(ref _enterTicks[idx]);
        long end = Volatile.Read(ref _exitTicks[idx]);
        if (start == 0 || end == 0 || end < start)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds((end - start) / (double)Stopwatch.Frequency);
    }

    /// <summary>
    /// Reset statuses to Created for reuse on the same graph shape.
    /// </summary>
    public void Reset()
    {
        if (!_initialized)
        {
            return;
        }

        Array.Clear(_statuses);
        Array.Clear(_enterTicks);
        Array.Clear(_exitTicks);
    }
}

/// <summary>
/// Observer that mirrors node lifecycle events into a <see cref="NodeStatusTracker"/>.
/// Plug this into your StateMachine to get live status without touching the FSM loop.
/// </summary>
public sealed class NodeStatusTrackingObserver(NodeStatusTracker tracker) : IAsyncStateObserver
{
    private readonly NodeStatusTracker _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        _tracker.MarkEntered(id);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        _tracker.MarkExited(id, true);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default)
    {
        if (ex is OperationCanceledException or TaskCanceledException)
        {
            _tracker.MarkCancelled(id);
            return ValueTask.CompletedTask;
        }

        _tracker.MarkExited(id, false);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        _tracker.MarkTransitioned(from, to);
        return ValueTask.CompletedTask;
    }
}