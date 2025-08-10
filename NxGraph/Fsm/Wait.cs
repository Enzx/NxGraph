namespace NxGraph.Fsm;

public static class Wait
{
    /// <summary>
    /// Creates a state that waits for a specified delay before completing.
    /// </summary>
    /// <param name="delay">The duration to wait before completing the state.</param>
    /// <returns>A new instance of <see cref="State"/> that represents the delay state.</returns>
    public static State For(TimeSpan delay)
    {
        return new DelayState(delay);
    }

    private sealed class DelayState(TimeSpan delay) : State
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