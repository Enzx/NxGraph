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
/// </summary>
public sealed class AsyncParallelState : IAsyncLogic, ISubGraphProvider
{
    private readonly AsyncStateMachine[] _regions;
    private readonly bool[] _done;

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
    {
        Guard.NotNull(regions, nameof(regions));
        if (regions.Length == 0)
        {
            throw new ArgumentException("At least one region is required.", nameof(regions));
        }

        _regions = new AsyncStateMachine[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            _regions[i] = new AsyncStateMachine(regions[i]);
        }

        _done = new bool[regions.Length];
    }

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

        return anyFailed ? Result.Failure : Result.Success;
    }
}
