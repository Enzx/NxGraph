using NxGraph.Blackboards;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Runs N independent async works truly concurrently inside one node and joins on all of them:
/// every work is started with the node's own <see cref="CancellationToken"/>, all are awaited
/// (<see cref="Task.WhenAll(Task[])"/> over the materialized tasks), and the results combine
/// afterwards — <c>Success</c> iff every work returned <c>Success</c>, else <c>Failure</c>.
/// This is the wall-clock concurrency primitive: regions structure logic, nodes structure time.
/// <para>
/// A failing work does <b>not</b> cancel its siblings — all works run to completion and the
/// result is combined afterwards, so partial effects are never timing-dependent and a per-node
/// <c>.Retry(...)</c> re-runs all works. A throwing work propagates its exception out of the
/// node after all works settle (first exception, <c>WhenAll</c> semantics); cancelling the
/// machine's token cancels all works. Each work receives the machine-bound routed
/// <see cref="BlackboardContext"/>: concurrent works must touch <b>disjoint</b> keys — same-key
/// access from two works is a data race, while distinct keys write to distinct slots and are
/// genuinely safe.
/// </para>
/// </summary>
public sealed class AsyncAllState : AsyncState
{
    private readonly Func<BlackboardContext, CancellationToken, ValueTask<Result>>[] _works;

    /// <param name="works">The works to run concurrently. At least one is required (a single
    /// work degenerates to an ordinary async relay); null entries are rejected.</param>
    public AsyncAllState(params Func<BlackboardContext, CancellationToken, ValueTask<Result>>[] works)
    {
        if (works is null || works.Length == 0)
        {
            throw new ArgumentException("At least one work is required.", nameof(works));
        }

        foreach (Func<BlackboardContext, CancellationToken, ValueTask<Result>>? work in works)
        {
            if (work is null)
            {
                throw new ArgumentException("Works must not contain null entries.", nameof(works));
            }
        }

        _works = works;
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        Task<Result>[] tasks = new Task<Result>[_works.Length];
        for (int i = 0; i < _works.Length; i++)
        {
            tasks[i] = RunWork(_works[i], Bb, ct);
        }

        Result[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (Result result in results)
        {
            if (result != Result.Success)
            {
                return Result.Failure;
            }
        }

        return Result.Success;
    }

    /// <summary>
    /// Materializes one work as a task so that a synchronously-throwing work faults its task
    /// instead of aborting the start loop — every sibling still starts, and the exception
    /// propagates only after all works settle.
    /// </summary>
    private static async Task<Result> RunWork(
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> work,
        BlackboardContext bb, CancellationToken ct)
    {
        return await work(bb, ct).ConfigureAwait(false);
    }
}
