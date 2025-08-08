namespace NxGraph.Fsm;

public static class Wait
{
    public static State For(TimeSpan delay, CancellationToken ct = default) => new DelayState(delay, ct);

    private sealed class DelayState(TimeSpan delay, CancellationToken cancel = default) : State
    {
        protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested || cancel.IsCancellationRequested)
            {
                return Result.Failure;
            }
            
            if (delay <= TimeSpan.Zero)
            {
                return Result.Success;
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
            return Result.Success;
        }
    }
}