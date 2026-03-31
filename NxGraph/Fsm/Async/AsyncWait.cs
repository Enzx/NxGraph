namespace NxGraph.Fsm.Async;

/// <summary>
/// Represents a state that waits for a specified duration before completing.
/// </summary>
public static class AsyncWait
{
    /// <summary>
    /// Creates a state that waits for a specified delay before completing.
    /// </summary>
    /// <param name="delay">The duration to wait before completing the state.</param>
    /// <returns>A new instance of <see cref="AsyncState"/> that represents the delay state.</returns>
    public static AsyncState For(TimeSpan delay)
    {
        return new AsyncDelayState(delay);
    }

    private sealed class AsyncDelayState(TimeSpan delay) : AsyncState
    {
        protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            if (delay <= TimeSpan.Zero)
            {
                return Result.Success;
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
            return Result.Success;
        }
    }
}