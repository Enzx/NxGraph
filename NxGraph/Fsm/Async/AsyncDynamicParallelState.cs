using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Dynamic (some-of-many) variant of <see cref="AsyncParallelState"/>: at composite entry a
/// selector reads the machine-bound <see cref="BlackboardContext"/> and returns a
/// <see cref="RegionMask"/> deciding which region graphs run this execution. Only the selected
/// regions are round-robin stepped (one node per region per round) until all of them reach a
/// terminal result — unselected regions are never stepped and never reset. Join semantics
/// match the static composite: <see cref="Result.Success"/> only when every selected region
/// succeeded, else <see cref="Result.Failure"/> through the parent's unified fault model.
/// <para>
/// The selector runs <b>once per execution, at entry</b> — the selected set is fixed for the
/// run (a region that must stop early should terminate via its own graph logic). An empty mask
/// is a vacuous join: the composite returns <see cref="Result.Success"/> without stepping
/// anything. Mask bits at or above the region count fail loudly at entry. Selectors run once
/// per composite execution, so they must stay allocation-free (compose masks with
/// <see cref="RegionMask.Bit"/> and <c>|</c>, or precompute them at setup).
/// </para>
/// <para>
/// Implements <see cref="IBlackboardSettable"/> because the selector needs the context; the
/// stamping walk stops at settable nodes, so this composite forwards the received context to
/// every region machine itself. Agent stamping still reaches regions via
/// <see cref="ISubGraphProvider"/>.
/// </para>
/// </summary>
public sealed class AsyncDynamicParallelState : IAsyncLogic, ISubGraphProvider, IBlackboardSettable
{
    private readonly Func<BlackboardContext, RegionMask> _selector;
    private readonly AsyncStateMachine[] _regions;
    private readonly bool[] _done;
    private BlackboardContext _blackboards;

    /// <summary>The region machines (selected or not).</summary>
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

    public AsyncDynamicParallelState(Func<BlackboardContext, RegionMask> selector, params Graph[] regions)
    {
        _selector = Guard.NotNull(selector, nameof(selector));
        Guard.NotNull(regions, nameof(regions));
        if (regions.Length == 0)
        {
            throw new ArgumentException("At least one region is required.", nameof(regions));
        }

        if (regions.Length > 64)
        {
            throw new ArgumentException(
                $"Dynamic parallel composites support at most 64 regions ({regions.Length} given) — " +
                "the selection mask is a single ulong.", nameof(regions));
        }

        _regions = new AsyncStateMachine[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            _regions[i] = new AsyncStateMachine(regions[i]);
        }

        _done = new bool[regions.Length];
    }

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        // The recursive stamping walk stops at IBlackboardSettable nodes — forward to the
        // region machines ourselves (each validates against its own graph's declarations).
        _blackboards = context;
        for (int i = 0; i < _regions.Length; i++)
        {
            ((IBlackboardSettable)_regions[i]).SetBlackboards(in context);
        }
    }

    public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        RegionMask selected = _selector(_blackboards);
        ValidateMask(selected);

        int remaining = 0;
        for (int i = 0; i < _regions.Length; i++)
        {
            bool isSelected = selected.Contains(i);
            _done[i] = !isSelected; // unselected regions are "done" before the first round
            if (isSelected)
            {
                remaining++;
            }
        }

        if (remaining == 0)
        {
            return Result.Success; // vacuous join
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

    private void ValidateMask(RegionMask mask)
    {
        if (_regions.Length < 64 && mask.Bits >> _regions.Length != 0)
        {
            throw new InvalidOperationException(
                $"Selector returned {mask} with bits at or above the region count ({_regions.Length}).");
        }
    }
}
