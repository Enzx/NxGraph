using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// In-node concurrency (README "In-node concurrency: .ToAllAsync(...)"): parallel regions
/// interleave cooperatively and never overlap in time — wall-clock concurrency lives inside
/// a single node. <c>.ToAllAsync(...)</c> starts every work with the node's token, awaits all,
/// and joins (Success iff every work succeeded); each work writes its own disjoint port and
/// the next node combines them. <c>.ToAll(...)</c> is the sequential sync twin.
/// </summary>
public static class InNodeConcurrencyExample
{
    private static class ScoutIo
    {
        public static readonly BlackboardSchema Schema = new("scout-io"); // Graph scope (default)
        public static readonly BlackboardKey<string> Weather = Schema.Register<string>("weather", "");
        public static readonly BlackboardKey<string> Terrain = Schema.Register<string>("terrain", "");
    }

    private static async ValueTask<string> FetchWeatherAsync(CancellationToken ct)
    {
        await Task.Delay(20, ct); // stands in for a slow I/O call
        return "clear skies";
    }

    private static async ValueTask<string> FetchTerrainAsync(CancellationToken ct)
    {
        await Task.Delay(20, ct); // overlaps with the weather call — ~max(t1, t2), not t1 + t2
        return "open plains";
    }

    private static ValueTask<Result> PlanRouteAsync(string weather, string terrain)
    {
        Console.WriteLine($"  Planning route for {weather} over {terrain}.");
        return ResultHelpers.Success;
    }

    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== In-node concurrency (.ToAllAsync + ports) ===");

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync( // both calls in flight at once — ~max(t1, t2), not t1 + t2
                async (bb, ct) => { bb.Set(ScoutIo.Weather, await FetchWeatherAsync(ct)); return Result.Success; },
                async (bb, ct) => { bb.Set(ScoutIo.Terrain, await FetchTerrainAsync(ct)); return Result.Success; })
            .ToAsync(ScoutIo.Weather, (weather, bb, ct) => // the next node combines the ports
                PlanRouteAsync(weather, bb.Get(ScoutIo.Terrain)))
            .WithSchema(ScoutIo.Schema)
            .Build();

        Result result = await graph.ToAsyncStateMachine()
            .WithBlackboard(new Blackboard(ScoutIo.Schema))
            .ExecuteAsync();
        Console.WriteLine($"  Concurrent scout report joined: {result}");

        RunSync();
    }

    private static void RunSync()
    {
        Console.WriteLine("=== In-node concurrency (sync twin .ToAll, sequential in one tick) ===");

        // Same join semantics (all works run, Success iff all succeeded) without wall-clock
        // overlap — the works run sequentially, in order, within one Execute() call.
        Graph graph = GraphBuilder
            .Start()
            .ToAll(
                bb => { bb.Set(ScoutIo.Weather, "drizzle"); return Result.Success; },
                bb => { bb.Set(ScoutIo.Terrain, "marshland"); return Result.Success; })
            .To(ScoutIo.Weather, (weather, bb) =>
            {
                Console.WriteLine($"  Planning route for {weather} over {bb.Get(ScoutIo.Terrain)}.");
                return Result.Success;
            })
            .WithSchema(ScoutIo.Schema)
            .Build();

        StateMachine machine = graph.ToStateMachine().WithBlackboard(new Blackboard(ScoutIo.Schema));
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Console.WriteLine($"  Sync twin joined: {result}");
    }
}
