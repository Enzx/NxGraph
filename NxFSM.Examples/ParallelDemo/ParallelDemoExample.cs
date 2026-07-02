using System.Text;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.ParallelDemo;

/// <summary>
/// Sync parallel composites demo: a stronghold under siege, driven tick-by-tick the way a
/// game loop would call <c>fsm.Execute()</c> from <c>Update()</c>.
/// <para>
/// Features demonstrated:
/// <list type="bullet">
///   <item><see cref="ParallelState"/> with <see cref="ParallelStepMode.RoundPerTick"/> —
///   two always-on regions (watchtower, gate crew) advance one node per frame, so region
///   progress aligns 1:1 with game-loop ticks; the join lands on the final frame.</item>
///   <item><see cref="DynamicParallelState"/> with <see cref="ParallelStepMode.RunToJoin"/> —
///   a blackboard-driven selector picks which defense regions run each wave, composed
///   allocation-free with <c>RegionMask.Bit(i) | ...</c>.</item>
///   <item>An empty selection is a vacuous join: no threat, no regions stepped, immediate
///   <see cref="Result.Success"/>.</item>
///   <item>Context forwarding: the selector <i>and</i> the region nodes read the same
///   machine-bound board — the composite hands the stamped context to its region machines.</item>
/// </list>
/// </para>
/// </summary>
public static class ParallelDemoExample
{
    private static readonly BlackboardSchema Schema = new("siege");
    private static readonly BlackboardKey<int> Threat = Schema.Register<int>("threat");

    public static void Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("┌───────────────────────────────────────────┐");
        Console.WriteLine("│  Stronghold Siege – Parallel Regions Demo │");
        Console.WriteLine("└───────────────────────────────────────────┘");
        Console.ResetColor();

        RunMorningWatch();
        RunSiegeWaves();
    }

    // ── Part 1: static AND-state, one round per frame ────────────────────

    private static void RunMorningWatch()
    {
        Console.WriteLine();
        Console.WriteLine("  Morning watch — every subsystem runs, one node per frame (RoundPerTick):");

        Graph watchtower = GraphBuilder
            .StartWith(() => Report("Watchtower", "scans the horizon"))
            .To(() => Report("Watchtower", "spots a dust cloud to the east"))
            .To(() => Report("Watchtower", "signals the yard"))
            .Build();

        Graph gateCrew = GraphBuilder
            .StartWith(() => Report("Gate crew", "swings the gate shut"))
            .To(() => Report("Gate crew", "drops the bar"))
            .Build();

        StateMachine fsm = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, watchtower, gateCrew)
            .ToStateMachine();

        int frame = 0;
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  ── frame {++frame} ──");
            Console.ResetColor();
            result = fsm.Execute(); // one round: each still-running region advances one node
        }

        Console.WriteLine($"  Both regions joined on frame {frame}: {result}");
    }

    // ── Part 2: dynamic selection by threat level ────────────────────────

    private static void RunSiegeWaves()
    {
        Console.WriteLine();
        Console.WriteLine("  Siege waves — the threat level on the blackboard picks the defenses:");

        // Region nodes read the same board the selector does — the composite forwards the
        // machine-bound context into its region machines.
        Graph archers = DefenseRegion("Archers", "loose a volley");
        Graph cauldrons = DefenseRegion("Cauldrons", "pour boiling oil");
        Graph catapults = DefenseRegion("Catapults", "hurl a boulder");

        Blackboard board = new(Schema);

        StateMachine fsm = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, SelectDefenses, archers, cauldrons, catapults)
            .ToStateMachine()
            .WithBlackboard(board);

        foreach (int level in (int[]) [1, 3, 0])
        {
            board.Set(Threat, level);
            Console.WriteLine($"  ── wave: threat level {level} ──");
            Result result = fsm.Execute(); // RunToJoin: the whole wave resolves in one tick
            if (level == 0)
            {
                Console.WriteLine($"    (no threat — vacuous join, nothing stepped: {result})");
            }
        }
    }

    private static RegionMask SelectDefenses(BlackboardContext bb)
    {
        // Composed with Bit | Bit — selectors run once per composite execution, on the
        // measured hot path, so RegionMask.Of(params) (which allocates) stays setup-only.
        int level = bb.Get(Threat);
        RegionMask mask = RegionMask.None;
        if (level >= 1)
        {
            mask |= RegionMask.Bit(0); // archers
        }

        if (level >= 2)
        {
            mask |= RegionMask.Bit(1); // cauldrons
        }

        if (level >= 3)
        {
            mask |= RegionMask.Bit(2); // catapults
        }

        return mask;
    }

    private static Graph DefenseRegion(string crew, string action)
    {
        return GraphBuilder
            .StartWith(bb => Report(crew, $"{action} (threat {bb.Get(Threat)})"))
            .Build();
    }

    private static Result Report(string who, string what)
    {
        Console.WriteLine($"    {who,-10} {what}");
        return Result.Success;
    }
}
