using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Scoped blackboards: one Global "world" board shared by every machine, plus one
/// Graph-scoped board per enemy. Keys are declared once in static schemas; the graph is a
/// shared template and each enemy binds its own board + agent.
/// </summary>
public static class BlackboardExample
{
    // Schemas are code: declare keys once, typically in a static holder like this.
    private static class WorldKeys
    {
        public static readonly BlackboardSchema Schema = new("world", BlackboardScope.Global);
        public static readonly BlackboardKey<bool> AlarmRaised = Schema.Register<bool>("AlarmRaised");
        public static readonly BlackboardKey<int> Sightings = Schema.Register<int>("Sightings");
    }

    private static class EnemyKeys
    {
        public static readonly BlackboardSchema Schema = new("enemy");
        public static readonly BlackboardKey<int> TargetDistance = Schema.Register<int>("TargetDistance", 10);
        public static readonly BlackboardKey<int> Speed = Schema.Register<int>("Speed", 1);
    }

    private sealed class Enemy(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class ScoutState : AsyncState<Enemy>
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            // Bb routes by the key's schema scope: global keys hit the shared world board,
            // graph keys hit this machine's own board — one call site, no chain walk.
            Bb.GetRef(EnemyKeys.TargetDistance) -= Bb.Get(EnemyKeys.Speed);
            if (Bb.Get(EnemyKeys.TargetDistance) <= 5 && !Bb.Get(WorldKeys.AlarmRaised))
            {
                Bb.Set(WorldKeys.AlarmRaised, true);
                Console.WriteLine($"  {Agent.Name} spotted the player — raising the world alarm!");
            }

            Bb.GetRef(WorldKeys.Sightings)++;
            return ResultHelpers.Success;
        }
    }

    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== Scoped Blackboards (Global + Graph) ===");

        // One shared graph template. The schema declarations travel with the graph and are
        // checked when boards are bound.
        Graph graph = GraphBuilder
            .StartWithAsync(new ScoutState()).SetName("Scout")
            .If(bb => bb.Get(WorldKeys.AlarmRaised))
            .ThenAsync((bb, _) =>
            {
                bb.Set(EnemyKeys.Speed, 3); // alarm up — everyone hunts faster
                return ResultHelpers.Success;
            })
            .ElseAsync((bb, _) => ResultHelpers.Success)
            .WithSchema(EnemyKeys.Schema)
            .WithSchema(WorldKeys.Schema)
            .Build();

        // One world board for everyone; one graph board + machine + agent per enemy.
        Blackboard world = new(WorldKeys.Schema);

        Enemy goblin = new("Goblin");
        Enemy archer = new("Archer");
        Blackboard goblinBoard = new(EnemyKeys.Schema);
        Blackboard archerBoard = new(EnemyKeys.Schema);
        goblinBoard.Set(EnemyKeys.TargetDistance, 6); // the goblin starts close

        AsyncStateMachine<Enemy> goblinFsm = graph.ToAsyncStateMachine<Enemy>()
            .WithBlackboard(world)
            .WithBlackboard(goblinBoard)
            .WithAgent(goblin);

        AsyncStateMachine<Enemy> archerFsm = graph.ToAsyncStateMachine<Enemy>()
            .WithBlackboard(world)
            .WithBlackboard(archerBoard)
            .WithAgent(archer);

        await goblinFsm.ExecuteAsync(); // goblin closes to 5 → raises the alarm
        await archerFsm.ExecuteAsync(); // archer sees the alarm → speeds up

        Console.WriteLine($"  Alarm raised: {world.Get(WorldKeys.AlarmRaised)}, " +
                          $"sightings: {world.Get(WorldKeys.Sightings)}");
        Console.WriteLine($"  Goblin distance: {goblinBoard.Get(EnemyKeys.TargetDistance)}, " +
                          $"archer speed after alarm: {archerBoard.Get(EnemyKeys.Speed)}");
    }
}
