using System.Text;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxFSM.Examples.ParallelDemo;

/// <summary>
/// Async parallel composites demo: an expedition setting up camp, run under the
/// <see cref="AsyncStateMachine"/>.
/// <para>
/// Features demonstrated:
/// <list type="bullet">
///   <item><see cref="AsyncParallelState"/> via <c>.Parallel(regions...)</c> — three regions of
///   different lengths advance one node per round (cooperative interleaving, not threads);
///   one <c>ExecuteAsync</c> call runs rounds until every region joins.</item>
///   <item><see cref="AsyncDynamicParallelState"/> via <c>.Parallel(selector, regions...)</c> —
///   a blackboard-driven selector picks which drill regions run each execution, composed
///   allocation-free with <c>RegionMask.Bit(i) | ...</c>.</item>
///   <item>Join semantics: <see cref="Result.Success"/> only when every selected region
///   succeeded; failures flow through the parent's unified fault model.</item>
/// </list>
/// The sync twin of this demo (step modes, per-frame ticking) is
/// <see cref="ParallelDemoExample"/>.
/// </para>
/// </summary>
public static class AsyncParallelDemoExample
{
    private static readonly BlackboardSchema Schema = new("expedition");
    private static readonly BlackboardKey<bool> StormComing = Schema.Register<bool>("storm");

    public static async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("┌─────────────────────────────────────────────┐");
        Console.WriteLine("│  Expedition Camp – Async Parallel Regions   │");
        Console.WriteLine("└─────────────────────────────────────────────┘");
        Console.ResetColor();

        await RunCampSetupAsync();
        await RunWeatherDrillsAsync();
    }

    // ── Part 1: static AND-state — all regions run, rounds interleave ────

    private static async Task RunCampSetupAsync()
    {
        Console.WriteLine();
        Console.WriteLine("  Camp setup — three crews of different sizes, one node per crew per round:");

        Graph tents = GraphBuilder
            .StartWithAsync(_ => Report("Tent crew", "clears the ground"))
            .ToAsync(_ => Report("Tent crew", "raises the poles"))
            .ToAsync(_ => Report("Tent crew", "pegs the canvas"))
            .Build();

        Graph fire = GraphBuilder
            .StartWithAsync(_ => Report("Fire crew", "gathers kindling"))
            .ToAsync(_ => Report("Fire crew", "lights the fire"))
            .Build();

        Graph scouts = GraphBuilder
            .StartWithAsync(_ => Report("Scouts", "circle the perimeter"))
            .Build();

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Parallel(tents, fire, scouts)
            .ToAsyncStateMachine();

        // One call runs round after round until every region reaches a terminal result —
        // the interleaved output shows one node per still-running region per round.
        Result result = await fsm.ExecuteAsync();
        Console.WriteLine($"  Camp is up (all regions joined): {result}");
    }

    // ── Part 2: dynamic selection from the blackboard ─────────────────────

    private static async Task RunWeatherDrillsAsync()
    {
        Console.WriteLine();
        Console.WriteLine("  Weather drills — the forecast on the blackboard picks the drills:");

        Graph gearDrill = GraphBuilder
            .StartWithAsync(_ => Report("Crew", "stows loose gear"))
            .Build();

        Graph shelterDrill = GraphBuilder
            .StartWithAsync(_ => Report("Crew", "reinforces the shelter"))
            .Build();

        Blackboard board = new(Schema);

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Parallel(SelectDrills, gearDrill, shelterDrill)
            .ToAsyncStateMachine()
            .WithBlackboard(board);

        foreach (bool storm in (bool[]) [false, true])
        {
            board.Set(StormComing, storm);
            Console.WriteLine($"  ── forecast: {(storm ? "storm front incoming" : "clear skies")} ──");
            await fsm.ExecuteAsync(); // the selector re-runs at every composite entry
        }
    }

    private static RegionMask SelectDrills(BlackboardContext bb)
    {
        // Composed with Bit | Bit — selectors run once per composite execution, so the
        // allocating RegionMask.Of(params) stays setup-only.
        RegionMask mask = RegionMask.Bit(0); // the gear drill always runs
        if (bb.Get(StormComing))
        {
            mask |= RegionMask.Bit(1); // shelter drill only when a storm is coming
        }

        return mask;
    }

    private static ValueTask<Result> Report(string who, string what)
    {
        Console.WriteLine($"    {who,-10} {what}");
        return ResultHelpers.Success;
    }
}
