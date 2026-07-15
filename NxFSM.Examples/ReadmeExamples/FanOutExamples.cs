using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Runnable versions of the README fan-out sections: parallel regions in both runtimes
/// (cooperative interleaving, RunToJoin vs RoundPerTick) and dynamic (some-of-many)
/// region selection with a blackboard-driven <see cref="RegionMask"/> selector.
/// </summary>
public static class FanOutExamples
{
    static readonly BlackboardSchema Schema = new("defenses");
    static readonly BlackboardKey<int> Threat = Schema.Register<int>("Threat");

    public static async ValueTask RunAsync()
    {
        await AsyncParallelRegionsAsync();
        SyncParallelRegions();
        DynamicParallelRegions();
    }

    // ── Parallel regions: async runtime (cooperative round interleaving) ──

    static async ValueTask AsyncParallelRegionsAsync()
    {
        Console.WriteLine("=== Fan-out: async parallel regions ===");

        Graph tents = GraphBuilder
            .StartWithAsync(_ => Step("Tents", "pitch the canvas"))
            .ToAsync(_ => Step("Tents", "stake it down"))
            .Build();

        Graph fire = GraphBuilder
            .StartWithAsync(_ => Step("Fire", "gather wood"))
            .ToAsync(_ => Step("Fire", "light it"))
            .Build();

        Graph scouts = GraphBuilder
            .StartWithAsync(_ => Step("Scouts", "sweep the ridge"))
            .Build();

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Parallel(tents, fire, scouts) // three region graphs, one node each per round
            .ToAsyncStateMachine();

        Result joined = await fsm.ExecuteAsync();
        Console.WriteLine($"  All regions joined: {joined}");
    }

    static ValueTask<Result> Step(string who, string what)
    {
        Console.WriteLine($"  {who,-7} {what}");
        return ResultHelpers.Success;
    }

    // ── Parallel regions: sync runtime, one round per tick ────────────────

    static void SyncParallelRegions()
    {
        Console.WriteLine("=== Fan-out: sync parallel regions (RoundPerTick) ===");

        Graph watchtower = GraphBuilder
            .StartWith(() => Tick("Watchtower", "scans the horizon"))
            .To(() => Tick("Watchtower", "signals the yard"))
            .Build();

        Graph gateCrew = GraphBuilder
            .StartWith(() => Tick("Gate crew", "drops the bar"))
            .Build();

        StateMachine fsm = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RoundPerTick, watchtower, gateCrew)
            .ToStateMachine();

        // From Update(): each call advances every still-running region by one node.
        int frame = 0;
        Result r = Result.InProgress;
        while (r == Result.InProgress)
        {
            frame++;
            r = fsm.Execute();
        }

        Console.WriteLine($"  Joined on frame {frame}: {r}");
    }

    static Result Tick(string who, string what)
    {
        Console.WriteLine($"  {who,-10} {what}");
        return Result.Success;
    }

    // ── Dynamic (some-of-many) regions: blackboard-driven selector ────────

    static void DynamicParallelRegions()
    {
        Console.WriteLine("=== Fan-out: dynamic (some-of-many) regions ===");

        // Region nodes read the same machine-bound board the selector does.
        Graph archers = GraphBuilder
            .StartWith(bb => Tick("Archers", $"loose a volley (threat {bb.Get(Threat)})"))
            .Build();
        Graph cauldrons = GraphBuilder
            .StartWith(bb => Tick("Cauldrons", $"pour boiling oil (threat {bb.Get(Threat)})"))
            .Build();
        Graph catapults = GraphBuilder
            .StartWith(bb => Tick("Catapults", $"hurl a boulder (threat {bb.Get(Threat)})"))
            .Build();

        Blackboard board = new(Schema);

        StateMachine fsm = GraphBuilder
            .Start()
            .Parallel(ParallelStepMode.RunToJoin, SelectDefenses, archers, cauldrons, catapults)
            .ToStateMachine()
            .WithBlackboard(board);

        board.Set(Threat, 2);
        Result result = fsm.Execute(); // archers + cauldrons run; catapults is never stepped
        Console.WriteLine($"  Wave resolved: {result}");
    }

    static RegionMask SelectDefenses(BlackboardContext bb)
    {
        RegionMask mask = RegionMask.Bit(0);                    // archers always
        if (bb.Get(Threat) >= 2) mask |= RegionMask.Bit(1);     // cauldrons
        if (bb.Get(Threat) >= 3) mask |= RegionMask.Bit(2);     // catapults
        return mask;
    }
}
