using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    // ── In-node concurrency: run N works inside one node, join on all ────
    //
    // .ToAllAsync(...) is the wall-clock concurrency primitive: none of the fan-out
    // constructs (parallel regions, token fork/join) overlap in time — they structure
    // logic via cooperative interleaving. Overlapping I/O belongs inside a single node.

    /// <summary>
    /// Starts the graph with a node that runs all <paramref name="works"/> <b>concurrently</b>
    /// (each started with the node's own <see cref="CancellationToken"/>, all awaited) and joins
    /// on the combined result: <c>Success</c> iff every work returned <c>Success</c>, else
    /// <c>Failure</c>. This is the recommended shape for overlapping I/O — parallel regions and
    /// token fork/join interleave cooperatively and never overlap in time.
    /// <para>
    /// No early abort: a failing work does not cancel its siblings — all works run to completion
    /// before the results combine, so partial effects are never timing-dependent and a per-node
    /// <c>.Retry(...)</c> re-runs all works. A throwing work propagates its exception after all
    /// works settle (first exception, <c>WhenAll</c> semantics); cancelling the machine's token
    /// cancels all works.
    /// </para>
    /// <para>
    /// <b>Disjoint-keys contract:</b> the works share one routed <see cref="BlackboardContext"/>,
    /// which is not thread-safe — concurrent works must touch <b>disjoint</b> keys. Same-key
    /// access from two works is a data race; distinct keys write to distinct slots and are
    /// genuinely safe. The recommended shape is one Graph-scoped output port per work, combined
    /// by the next node. An empty or null works array throws <see cref="ArgumentException"/> at
    /// wiring time, as does a null entry; a single work is legal (it degenerates to
    /// <c>ToAsync</c>).
    /// </para>
    /// </summary>
    public static StateToken ToAllAsync(this StartToken token,
        params Func<BlackboardContext, CancellationToken, ValueTask<Result>>[] works)
    {
        return token.ToAsync(new AsyncAllState(works));
    }

    /// <inheritdoc cref="ToAllAsync(StartToken, Func{BlackboardContext, CancellationToken, ValueTask{Result}}[])"/>
    public static StateToken ToAllAsync(this StateToken prev,
        params Func<BlackboardContext, CancellationToken, ValueTask<Result>>[] works)
    {
        return prev.ToAsync(new AsyncAllState(works));
    }

    /// <inheritdoc cref="ToAllAsync(StartToken, Func{BlackboardContext, CancellationToken, ValueTask{Result}}[])"/>
    public static BranchBuilder ToAllAsync(this BranchBuilder branch,
        params Func<BlackboardContext, CancellationToken, ValueTask<Result>>[] works)
    {
        return branch.ToAsync(new AsyncAllState(works));
    }

    /// <inheritdoc cref="ToAllAsync(StartToken, Func{BlackboardContext, CancellationToken, ValueTask{Result}}[])"/>
    public static StateToken ToAllAsync(this BranchEnd branchEnd,
        params Func<BlackboardContext, CancellationToken, ValueTask<Result>>[] works)
    {
        return branchEnd.ToAsync(new AsyncAllState(works));
    }

    /// <summary>
    /// Starts the graph with the sync twin of
    /// <see cref="ToAllAsync(StartToken, Func{BlackboardContext, CancellationToken, ValueTask{Result}}[])"/>:
    /// all <paramref name="works"/> run <b>sequentially, in order</b>, within one tick, and the
    /// results combine identically — <c>Success</c> iff every work returned <c>Success</c>, else
    /// <c>Failure</c>. Wall-clock overlap is an async-only mechanic; the join semantics are the
    /// same, including no early abort: all works always run, even after an early <c>Failure</c>,
    /// so a graph moved between runtimes sees the same board effects. The node is plain
    /// <see cref="Fsm.ILogic"/> and also runs under the async machine via the sync-logic adapter.
    /// <para>
    /// The disjoint-keys contract of the async twin is not load-bearing here (works never
    /// overlap), but keeping each work on its own keys makes the graph portable to
    /// <c>ToAllAsync</c>. An empty or null works array throws <see cref="ArgumentException"/> at
    /// wiring time, as does a null entry; a single work is legal.
    /// </para>
    /// </summary>
    public static StateToken ToAll(this StartToken token,
        params Func<BlackboardContext, Result>[] works)
    {
        return token.To(new AllState(works));
    }

    /// <inheritdoc cref="ToAll(StartToken, Func{BlackboardContext, Result}[])"/>
    public static StateToken ToAll(this StateToken prev,
        params Func<BlackboardContext, Result>[] works)
    {
        return prev.To(new AllState(works));
    }

    /// <inheritdoc cref="ToAll(StartToken, Func{BlackboardContext, Result}[])"/>
    public static BranchBuilder ToAll(this BranchBuilder branch,
        params Func<BlackboardContext, Result>[] works)
    {
        return branch.To(new AllState(works));
    }

    /// <inheritdoc cref="ToAll(StartToken, Func{BlackboardContext, Result}[])"/>
    public static StateToken ToAll(this BranchEnd branchEnd,
        params Func<BlackboardContext, Result>[] works)
    {
        return branchEnd.To(new AllState(works));
    }
}
