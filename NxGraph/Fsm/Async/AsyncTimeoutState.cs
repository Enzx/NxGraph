using System.Collections.Concurrent;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Wraps any <see cref="IAsyncLogic"/> and enforces a maximum execution time.
/// If the inner node does not complete within the timeout, the wrapper either
/// returns <see cref="Result.Failure"/> or throws <see cref="TimeoutException"/>,
/// depending on <see cref="TimeoutBehavior"/>.
/// </summary>
public class AsyncTimeoutState : IAsyncLogic
{
    private readonly IAsyncLogic _inner;
    private readonly TimeSpan _timeout;
    private readonly TimeoutBehavior _behavior;
    private readonly Action<object?> _cachedCancelCallback = CancelCallback;
    private readonly string _timeoutMessage;

    public AsyncTimeoutState(IAsyncLogic inner, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be > 0.");
        }

        _timeout = timeout;
        _behavior = behavior;
        _timeoutMessage = $"Node timed out after {timeout}.";
    }

    public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        CancellationTokenSource cts = CtsPool.Rent();

        CancellationTokenRegistration reg = default;
        if (ct.CanBeCanceled)
        {
#if NET6_0_OR_GREATER
            reg = ct.UnsafeRegister(_cachedCancelCallback, cts);
#else
            // netstandard2.1 has no UnsafeRegister; the Register(Action<object?>, object?) overload
            // takes the callback and the state directly. The previous version cast the state to
            // Action and invoked it, which threw InvalidCastException because the cached callback
            // is Action<object?>, not Action — and would also have lost the CTS state.
            reg = ct.Register(_cachedCancelCallback, cts);
#endif
        }

        try
        {
            cts.CancelAfter(_timeout);

            try
            {
                return await _inner.ExecuteAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && cts.IsCancellationRequested)
            {
                if (_behavior == TimeoutBehavior.Throw)
                {
                    throw new TimeoutException(_timeoutMessage);
                }

                // A timeout is an ordinary node failure: it participates in the unified
                // fault model — per-node retry policies and failure edges — exactly like
                // a node returning Failure itself.
                return Result.Fail(_timeoutMessage);
            }
        }
        finally
        {
            await reg.DisposeAsync().ConfigureAwait(false);
            CtsPool.Return(cts);
        }
    }

    private static void CancelCallback(object? s)
    {
        if (s is null)
        {
            return;
        }

        CancellationTokenSource source = (CancellationTokenSource)s;
        source.Cancel();
    }


    private static class CtsPool
    {
        private static readonly ConcurrentBag<CancellationTokenSource> Bag = [];

        public static CancellationTokenSource Rent()
        {
            if (!Bag.TryTake(out CancellationTokenSource? cts))
            {
                return new CancellationTokenSource();
            }
#if NET8_0_OR_GREATER
            if (cts.TryReset())
            {
                return cts;
            }
#else
            // No TryReset in .NET Standard 2.1: reuse the rented CTS when it has not been
            // cancelled (CancelAfter will reset its timer on next use). Previously this
            // branch allocated a *new* CTS when the rented one was not cancelled, leaking
            // the rented one and doubling allocations on every rent.
            if (!cts.IsCancellationRequested)
            {
                return cts;
            }
#endif

            // Could not reset safely; dispose and fall through to new
            cts.Dispose();

            return new CancellationTokenSource();
        }

        public static void Return(CancellationTokenSource cts)
        {
#if NET6_0_OR_GREATER
            if (cts.TryReset())
            {
                Bag.Add(cts);
            }
#else
            if (!cts.IsCancellationRequested)
            {
                // Disarm the previous CancelAfter timer before pooling. Without this, the
                // stale timer can fire right after the CTS is re-rented (before the next
                // CancelAfter re-arms it) and register as a spurious instant timeout.
                cts.CancelAfter(global::System.Threading.Timeout.InfiniteTimeSpan);
                if (!cts.IsCancellationRequested)
                {
                    Bag.Add(cts);
                    return;
                }

                cts.Dispose();
            }
#endif
            else
            {
                cts.Dispose();
            }
        }
    }
}

public static class Timeout
{
    /// <summary>Wrap an existing node with a timeout.</summary>
    public static IAsyncLogic Wrap(IAsyncLogic asyncLogic, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return new AsyncTimeoutState(asyncLogic, timeout, behavior);
    }

    /// <summary>Wrap a delegate as a node with a timeout (convenience overload).</summary>
    public static IAsyncLogic For(TimeSpan timeout, Func<CancellationToken, ValueTask<Result>> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return new AsyncTimeoutState(new AsyncRelayState(run), timeout, behavior);
    }
}