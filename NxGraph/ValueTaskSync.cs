namespace NxGraph;

/// <summary>
/// Synchronous completion helper for <see cref="ValueTask"/>-returning callbacks that must
/// finish before a sync caller returns (delivery-before-return). The single shared body used
/// by the sync-side log-report bridges (<c>State.Log</c>, the behavior composites): the
/// completed-successfully fast path costs nothing, and only a genuinely asynchronous
/// callback pays the blocking <c>AsTask()</c> fallback.
/// </summary>
internal static class ValueTaskSync
{
    /// <summary>
    /// Waits the task out synchronously: observe the result directly when it already
    /// completed successfully; otherwise block on the materialized task (propagating
    /// faults/cancellation like any awaited task).
    /// </summary>
    internal static void Await(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        task.AsTask().GetAwaiter().GetResult();
    }
}
