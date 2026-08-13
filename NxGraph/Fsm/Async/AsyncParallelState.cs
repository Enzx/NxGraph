using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Orthogonal (AND-state) regions via <b>cooperative interleaving</b>: each region is a child
/// graph, and one execution of this composite round-robin steps every still-running region —
/// one node per region per round — until all regions reach a terminal result (the join).
/// The composite returns <see cref="Result.Success"/> only when every region succeeded;
/// if any region failed it returns <see cref="Result.Failure"/> after the join, which then
/// flows through the parent's unified fault model (failure edges, retries).
/// <para>
/// This is deliberately <b>not</b> thread-concurrent. True parallel execution would require
/// multiple in-flight tasks and a join (<c>WhenAll</c>), which allocate — that variant is out
/// of scope to preserve the library's zero-allocation hot-path guarantee. Interleaved progress
/// gives AND-state semantics at 0 B per step, which covers the game/agent use cases this
/// library targets; run truly CPU-parallel work inside a single node instead.
/// </para>
/// <para>
/// Deliberately does <b>not</b> implement <see cref="ISuspendableComposite"/>: every
/// execution runs all regions to terminal inside one <c>ExecuteAsync</c> and resets its
/// bookkeeping on entry, so the composite holds no durable visit or cross-visit state at a
/// parent step boundary — there is nothing for a deep snapshot to capture, and its absence
/// from one is correct, not a gap.
/// </para>
/// </summary>
public sealed class AsyncParallelState : IAsyncLogic, ISubGraphProvider, IBlackboardSettable,
    IOwnedBoardProvider
{
    private readonly AsyncStateMachine[] _regions;
    private readonly bool[] _done;

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        // The recursive stamping walk stops at IBlackboardSettable nodes — forward to the
        // region machines ourselves (each validates against its own graph's declarations),
        // matching AsyncDynamicParallelState.
        for (int i = 0; i < _regions.Length; i++)
        {
            ((IBlackboardSettable)_regions[i]).SetBlackboards(in context);
        }
    }

    /// <summary>The region machines.</summary>
    public IReadOnlyList<AsyncStateMachine> Regions => _regions;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs
    {
        get
        {
            foreach (AsyncStateMachine region in _regions)
            {
                yield return region.Graph;
            }
        }
    }

    public AsyncParallelState(params Graph[] regions)
        : this(ParallelComposites.BuildAsyncRegions(ParallelComposites.ToRegions(regions), unbound: false))
    {
    }

    /// <summary>
    /// Region-entry overload: each <see cref="ParallelRegion"/> pairs its graph with an
    /// optional per-region ports declaration (see <see cref="SubGraphPorts"/>) — sibling
    /// regions running the same child graph each get their own board.
    /// </summary>
    public AsyncParallelState(ParallelRegion[] regions)
        : this(ParallelComposites.BuildAsyncRegions(regions, unbound: false))
    {
    }

    private AsyncParallelState(AsyncStateMachine[] regions)
    {
        _regions = regions;
        _done = new bool[regions.Length];
    }

    /// <summary>
    /// Creates a composite whose region machines carry name-bound ports — the deserialization
    /// rebind form (see <see cref="AsyncStateMachine.Unbound"/>). <paramref name="ports"/>
    /// aligns with <paramref name="regions"/> by index; <see langword="null"/> entries (or a
    /// <see langword="null"/> array) mean the shared-board default.
    /// </summary>
    public static AsyncParallelState Unbound(Graph[] regions, SubGraphPorts?[]? ports) =>
        new(ParallelComposites.BuildAsyncRegions(ParallelComposites.ToRegions(regions, ports),
            unbound: true));

    IEnumerable<OwnedBoardEntry> IOwnedBoardProvider.EnumerateOwnedBoards(int nodeIndex) =>
        ParallelComposites.EnumerateOwnedBoards(nodeIndex, _regions);

    public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        int remaining = _regions.Length;
        for (int i = 0; i < _done.Length; i++)
        {
            _done[i] = false;
        }

        bool anyFailed = false;
        while (remaining > 0)
        {
            for (int i = 0; i < _regions.Length; i++)
            {
                if (_done[i])
                {
                    continue;
                }

                Result step = await _regions[i].StepAsync(ct).ConfigureAwait(false);
                if (!step.IsCompleted)
                {
                    continue;
                }

                _done[i] = true;
                remaining--;
                if (step.IsFailure)
                {
                    anyFailed = true;
                }
            }
        }

        // Several regions may target the same parent output key: re-applying the terminal
        // copies in region order at the join makes that conflict resolve by region order (the
        // documented rule) instead of by completion order. Values are unchanged since each
        // region's terminal, so this is idempotent for every non-conflicting declaration.
        for (int i = 0; i < _regions.Length; i++)
        {
            _regions[i].RecopyOutputPortsAtJoin();
        }

        return anyFailed ? Result.Failure : Result.Success;
    }
}
