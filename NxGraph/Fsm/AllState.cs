using NxGraph.Blackboards;
using NxGraph.Fsm.Async;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="AsyncAllState"/>: runs N independent works
/// sequentially, in declaration order, within one <see cref="State.Execute"/> call and joins
/// on all of them — <c>Success</c> iff every work returned <c>Success</c>, else <c>Failure</c>.
/// Wall-clock overlap is an async-only mechanic (like retry backoff); the essence — N works,
/// one node, all-succeed join — is identical, so a graph moved between runtimes sees the same
/// board effects.
/// <para>
/// All works always run, even after an early <c>Failure</c> — the same no-early-abort
/// semantics as the async twin — and a per-node <c>.Retry(...)</c> re-runs all works. Each
/// work receives the machine-bound routed <see cref="BlackboardContext"/>; the disjoint-keys
/// contract of the async twin is not load-bearing here (works never overlap) but keeping it
/// makes the graph portable to <c>.ToAllAsync</c>. Allocation-free per tick: the works array
/// is construction-time state. Being <see cref="ILogic"/>, this node also runs under the
/// async machine via the sync-logic adapter.
/// </para>
/// </summary>
public sealed class AllState : State
{
    private readonly Func<BlackboardContext, Result>[] _works;

    /// <param name="works">The works to run in order. At least one is required (a single
    /// work degenerates to an ordinary relay); null entries are rejected.</param>
    public AllState(params Func<BlackboardContext, Result>[] works)
    {
        if (works is null || works.Length == 0)
        {
            throw new ArgumentException("At least one work is required.", nameof(works));
        }

        foreach (Func<BlackboardContext, Result>? work in works)
        {
            if (work is null)
            {
                throw new ArgumentException("Works must not contain null entries.", nameof(works));
            }
        }

        _works = works;
    }

    protected override Result OnRun()
    {
        bool allSucceeded = true;
        Func<BlackboardContext, Result>[] works = _works;
        for (int i = 0; i < works.Length; i++)
        {
            if (works[i](Bb) != Result.Success)
            {
                allSucceeded = false;
            }
        }

        return allSucceeded ? Result.Success : Result.Failure;
    }
}
