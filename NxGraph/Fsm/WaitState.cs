using System.Diagnostics;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncWait"/>: a multi-tick wait for the
/// frame-stepped runtime. The first <see cref="Execute"/> of a visit records a
/// <see cref="Stopwatch"/> timestamp; every tick until the duration elapses returns
/// <see cref="Result.InProgress"/> ("re-run me next tick"), then <see cref="Result.Success"/>.
/// Zero or negative durations complete immediately. Completing the wait clears the recorded
/// timestamp, so re-entering the state starts a fresh wait.
/// <para>
/// Sync runtime only: returning node-level <see cref="Result.InProgress"/> across ticks is
/// the sync machine's multi-frame contract; the async loop rejects it (use
/// <c>.WaitForAsync(...)</c> there). No timers, no allocation — the wait is a timestamp
/// comparison per tick.
/// </para>
/// </summary>
public sealed class WaitState : ILogic
{
    private readonly long _durationTimestampTicks;
    private readonly Func<long> _clock;
    private long _startedAt;
    private bool _inFlight;

    /// <summary>Creates a state that waits for <paramref name="duration"/> before succeeding.</summary>
    public WaitState(TimeSpan duration)
        : this(duration, static () => Stopwatch.GetTimestamp())
    {
    }

    /// <summary>
    /// Creates a wait over a custom time source. <paramref name="clock"/> must return
    /// monotonic timestamps in <see cref="Stopwatch.Frequency"/> units — inject scaled or
    /// paused game time, or a fake clock for deterministic tests.
    /// </summary>
    public WaitState(TimeSpan duration, Func<long> clock)
    {
        _durationTimestampTicks = duration <= TimeSpan.Zero
            ? 0
            : (long)(duration.TotalSeconds * Stopwatch.Frequency);
        _clock = Guard.NotNull(clock, nameof(clock));
    }

    public Result Execute()
    {
        if (_durationTimestampTicks == 0)
        {
            return Result.Success;
        }

        if (!_inFlight)
        {
            _startedAt = _clock();
            _inFlight = true;
        }

        if (_clock() - _startedAt < _durationTimestampTicks)
        {
            return Result.InProgress;
        }

        _inFlight = false;
        return Result.Success;
    }
}
