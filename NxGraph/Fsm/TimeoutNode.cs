using System.Collections.Concurrent;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Wraps any <see cref="INode"/> and enforces a maximum execution time.
/// If the inner node does not complete within the timeout, the wrapper either
/// returns <see cref="Result.Failure"/> or throws <see cref="TimeoutException"/>,
/// depending on <see cref="TimeoutBehavior"/>.
/// </summary>
public class TimeoutNode : INode
{
    private readonly INode _inner;
    private readonly TimeSpan _timeout;
    private readonly TimeoutBehavior _behavior;
    private readonly Action<object?> _cachedCancelCallback = CancelCallback;

    public TimeoutNode(INode inner, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
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
            reg = ct.UnsafeRegister(_cachedCancelCallback, cts);
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

            if (cts.TryReset())
            {
                return cts;
            }

            // Could not reset safely; dispose and fall through to new
            cts.Dispose();

            return new CancellationTokenSource();
        }

        public static void Return(CancellationTokenSource cts)
        {
            if (cts.TryReset())
            {
                Bag.Add(cts);
            }
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
    public static INode Wrap(INode node, TimeSpan timeout, TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return new TimeoutNode(node, timeout, behavior);
    }

    /// <summary>Wrap a delegate as a node with a timeout (convenience overload).</summary>
    public static INode For(TimeSpan timeout, Func<CancellationToken, ValueTask<Result>> run,
        TimeoutBehavior behavior = TimeoutBehavior.Fail)
    {
        return new TimeoutNode(new RelayState(run), timeout, behavior);
    }
}