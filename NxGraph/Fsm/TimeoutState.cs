using System.Collections.Concurrent;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Wraps any <see cref="ILogic"/> and enforces a maximum execution time.
/// If the inner node does not complete within the timeout, the wrapper either
/// returns <see cref="Result.Failure"/> or throws <see cref="TimeoutException"/>,
/// depending on <see cref="TimeoutBehavior"/>.
/// </summary>
public class TimeoutState : ILogic
{
    private readonly ILogic _inner;
    private readonly TimeSpan _timeout;
    private readonly TimeoutBehavior _behavior;
    private readonly Action<object?> _cachedCancelCallback = CancelCallback;

    public TimeoutState(ILogic inner, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be > 0.");
        }

        _timeout = timeout;
        _behavior = behavior;
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
            reg = ct.Register(static s => ((Action)s!).Invoke(), _cachedCancelCallback);
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
                    throw new TimeoutException($"Node timed out after {_timeout}.");
                }

                return Result.Failure;
            }
        }
        finally
        {
            await reg.DisposeAsync();
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
#if NET6_0_OR_GREATER
            if (cts.TryReset())
            {
                return cts;
            }
#else
            // No TryReset in .NET Standard 2.1, so just dispose and create new.
            if (!cts.IsCancellationRequested)
            {
                cts = new CancellationTokenSource(); 
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
#else
            if (!cts.IsCancellationRequested)
#endif
            {
                Bag.Add(cts);
            }
            else
            {
                cts.Dispose();
            }

            {
                Bag.Add(cts);
            }
        }
    }
}

public static class Timeout
{
    /// <summary>Wrap an existing node with a timeout.</summary>
    public static ILogic Wrap(ILogic logic, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return new TimeoutState(logic, timeout, behavior);
    }

    /// <summary>Wrap a delegate as a node with a timeout (convenience overload).</summary>
    public static ILogic For(TimeSpan timeout, Func<CancellationToken, ValueTask<Result>> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return new TimeoutState(new RelayState(run), timeout, behavior);
    }
}