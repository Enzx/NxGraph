using System.Diagnostics;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncTimeoutState"/>: wraps an
/// <see cref="ILogic"/> with a deadline. The first <see cref="Execute"/> of a visit records a
/// <see cref="Stopwatch"/> timestamp; each tick runs the inner logic once. A terminal inner
/// result passes through and ends the visit. When the inner logic returns
/// <see cref="Result.InProgress"/> past the deadline, the wrapper produces the timeout
/// outcome per <see cref="TimeoutBehavior"/>: <see cref="TimeoutBehavior.Fail"/> returns
/// <see cref="Result.Failure"/> into the unified fault model (failure edges, retries — a
/// retry starts a fresh deadline); <see cref="TimeoutBehavior.Throw"/> throws
/// <see cref="TimeoutException"/>.
/// <para>
/// Mechanics differ from the async twin by design: the sync runtime has no cancellation, so
/// the deadline cannot interrupt a node mid-execution — it is detected <b>between</b> ticks,
/// after the inner logic returns. No CTS, no timers, no allocation on the happy path.
/// </para>
/// </summary>
public sealed class TimeoutState : ILogic
{
    private readonly ILogic _inner;
    private readonly TimeoutBehavior _behavior;
    private readonly long _timeoutTimestampTicks;
    private readonly Func<long> _clock;
    private readonly string _timeoutMessage;
    private long _startedAt;
    private bool _inFlight;

    public TimeoutState(ILogic inner, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
        : this(inner, timeout, behavior, static () => Stopwatch.GetTimestamp())
    {
    }

    /// <summary>
    /// Creates a timeout wrapper over a custom time source. <paramref name="clock"/> must
    /// return monotonic timestamps in <see cref="Stopwatch.Frequency"/> units — inject scaled
    /// or paused game time, or a fake clock for deterministic tests.
    /// </summary>
    public TimeoutState(ILogic inner, TimeSpan timeout, TimeoutBehavior behavior, Func<long> clock)
    {
        _inner = Guard.NotNull(inner, nameof(inner));
        Guard.NotNull(clock, nameof(clock));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be > 0.");
        }

        _behavior = behavior;
        _timeoutTimestampTicks = (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        _clock = clock;
        _timeoutMessage = $"Node timed out after {timeout}.";
    }

    public Result Execute()
    {
        if (!_inFlight)
        {
            _startedAt = _clock();
            _inFlight = true;
        }

        Result result = _inner.Execute();
        if (result.IsCompleted)
        {
            _inFlight = false;
            return result;
        }

        if (_clock() - _startedAt < _timeoutTimestampTicks)
        {
            return Result.InProgress;
        }

        _inFlight = false;
        if (_behavior == TimeoutBehavior.Throw)
        {
            throw new TimeoutException(_timeoutMessage);
        }

        // A timeout is an ordinary node failure: it participates in the unified fault model —
        // per-node retry policies and failure edges — exactly like a node returning Failure.
        return Result.Fail(_timeoutMessage);
    }
}
