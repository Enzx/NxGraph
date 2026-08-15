using NxGraph.Compatibility;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Shared region plumbing for the four parallel composites: wrapping plain graphs into
/// <see cref="ParallelRegion"/> entries, building region machines (live or name-bound/unbound
/// forms) with their per-region ports, and the per-region owned-board enumeration.
/// </summary>
internal static class ParallelComposites
{
    internal static ParallelRegion[] ToRegions(Graph[] regions)
    {
        Guard.NotNull(regions, nameof(regions));
        ParallelRegion[] entries = new ParallelRegion[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            entries[i] = new ParallelRegion(regions[i]);
        }

        return entries;
    }

    internal static ParallelRegion[] ToRegions(Graph[] regions, SubGraphPorts?[]? ports)
    {
        Guard.NotNull(regions, nameof(regions));
        if (ports is not null && ports.Length != regions.Length)
        {
            throw new ArgumentException(
                $"Ports array length ({ports.Length}) does not match the region count ({regions.Length}).",
                nameof(ports));
        }

        ParallelRegion[] entries = new ParallelRegion[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            entries[i] = new ParallelRegion(regions[i], ports?[i]);
        }

        return entries;
    }

    internal static StateMachine[] BuildSyncRegions(ParallelRegion[] regions, bool unbound)
    {
        ValidateRegions(regions);
        StateMachine[] machines = new StateMachine[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            machines[i] = unbound
                ? StateMachine.Unbound(regions[i].Graph, null, null, regions[i].Ports)
                : new StateMachine(regions[i].Graph, null, null, null, regions[i].Ports);
        }

        return machines;
    }

    internal static AsyncStateMachine[] BuildAsyncRegions(ParallelRegion[] regions, bool unbound)
    {
        ValidateRegions(regions);
        AsyncStateMachine[] machines = new AsyncStateMachine[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            machines[i] = unbound
                ? AsyncStateMachine.Unbound(regions[i].Graph, null, null, regions[i].Ports)
                : new AsyncStateMachine(regions[i].Graph, null, null, null, regions[i].Ports);
        }

        return machines;
    }

    private static void ValidateRegions(ParallelRegion[] regions)
    {
        Guard.NotNull(regions, nameof(regions));
        if (regions.Length == 0)
        {
            throw new ArgumentException("At least one region is required.", nameof(regions));
        }
    }

    internal static void ValidateDynamicRegionCount(int count, string paramName)
    {
        if (count > 64)
        {
            throw new ArgumentException(
                $"Dynamic parallel composites support at most 64 regions ({count} given) — " +
                "the selection mask is a single ulong.", paramName);
        }
    }

    internal static IEnumerable<OwnedBoardEntry> EnumerateOwnedBoards(int nodeIndex, StateMachine[] regions)
    {
        for (int r = 0; r < regions.Length; r++)
        {
            foreach (OwnedBoardEntry entry in regions[r].EnumerateOwnedBoards())
            {
                yield return new OwnedBoardEntry(OwnedBoards.RegionPath(nodeIndex, r, entry.Path), entry.Board);
            }
        }
    }

    internal static IEnumerable<OwnedBoardEntry> EnumerateOwnedBoards(int nodeIndex,
        AsyncStateMachine[] regions)
    {
        for (int r = 0; r < regions.Length; r++)
        {
            foreach (OwnedBoardEntry entry in regions[r].EnumerateOwnedBoards())
            {
                yield return new OwnedBoardEntry(OwnedBoards.RegionPath(nodeIndex, r, entry.Path), entry.Board);
            }
        }
    }
}
