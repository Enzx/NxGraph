namespace NxGraph.Fsm;

/// <summary>
/// How the delay between retry attempts grows.
/// </summary>
public enum BackoffKind : byte
{
    /// <summary>Every retry waits the same base delay.</summary>
    Fixed = 0,

    /// <summary>Retry N waits N × base delay.</summary>
    Linear = 1,

    /// <summary>Retry N waits 2^(N-1) × base delay.</summary>
    Exponential = 2,
}

/// <summary>
/// Per-node retry policy: when the node's logic returns <c>Failure</c>, the machine
/// re-runs it in place until it succeeds or <see cref="MaxAttempts"/> executions have
/// been consumed, then falls through to the normal failure handling (failure edge or
/// terminal failure). Attempt counters live in the executing state machine, not the graph.
/// <para>
/// Backoff delays are honored by the async runtime only. The synchronous runtime is
/// frame-stepped (one node per <c>Execute()</c> call) and must never block, so it retries
/// on the next tick regardless of the configured backoff.
/// </para>
/// </summary>
public readonly struct RetryPolicy
{
    public RetryPolicy(byte maxAttempts, TimeSpan backoff = default, BackoffKind backoffKind = BackoffKind.Fixed)
    {
        if (maxAttempts == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts),
                "MaxAttempts must be at least 1 (use RetryPolicy.None for no retries).");
        }

        if (backoff < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(backoff), "Backoff cannot be negative.");
        }

        MaxAttempts = maxAttempts;
        Backoff = backoff;
        BackoffKind = backoffKind;
    }

    /// <summary>Total executions allowed for the node, including the first one. 0 = no policy.</summary>
    public byte MaxAttempts { get; }

    /// <summary>Base delay between attempts; <see cref="TimeSpan.Zero"/> retries immediately.</summary>
    public TimeSpan Backoff { get; }

    public BackoffKind BackoffKind { get; }

    /// <summary>No retry policy — a failing node fails on its first attempt (the zero-initialized default).</summary>
    public static readonly RetryPolicy None;

    /// <summary>
    /// The delay to wait before the next attempt, given how many attempts have already failed.
    /// </summary>
    public TimeSpan DelayForAttempt(int failedAttempts)
    {
        if (Backoff <= TimeSpan.Zero || failedAttempts <= 0)
        {
            return TimeSpan.Zero;
        }

        return BackoffKind switch
        {
            BackoffKind.Linear => Backoff * failedAttempts,
            BackoffKind.Exponential => Backoff * (1L << Math.Min(failedAttempts - 1, 30)),
            _ => Backoff,
        };
    }
}
