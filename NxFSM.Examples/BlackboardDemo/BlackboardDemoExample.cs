using System.Text;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization;

namespace NxFSM.Examples.BlackboardDemo;

/// <summary>
/// Comprehensive scoped-blackboard demo: a squad of guards patrolling a building while an
/// intruder sneaks through it, built on the <b>async</b> runtime.
/// <para>
/// Features demonstrated:
/// <list type="bullet">
///   <item>Agent vs blackboard as orthogonal channels — <c>WithBlackboard(...).WithBlackboard(...).WithAgent(...)</c></item>
///   <item><see cref="BlackboardScope.Global"/> world board shared by every machine (one guard raises the alarm, all react)</item>
///   <item><see cref="BlackboardScope.Graph"/> boards: one graph template, N guards, each with private working memory</item>
///   <item>Schema declarations on the graph (<c>.WithSchema</c>) with bind-time validation</item>
///   <item>Blackboard-driven branching: <c>.Switch(bb =&gt; bb.Get(GuardKeys.Mode))</c></item>
///   <item>Combined agent + context relay lambdas: <c>.ToAsync&lt;Guard&gt;((guard, bb, ct) =&gt; ...)</c></item>
///   <item>In-place struct mutation through <c>Bb.GetRef(...)</c></item>
///   <item>Durability: boards saved with <see cref="BlackboardSerializer"/>, restored into fresh
///   boards after a simulated restart, simulation resumes seamlessly</item>
/// </list>
/// </para>
/// </summary>
public static class BlackboardDemoExample
{
    public const int RoomCount = 5;

    /// <summary>The intruder's scripted route, one room per tick.</summary>
    private static readonly int[] IntruderPath = [2, 2, 3, 4, 4, 3, 2, 1, 1, 2];

    public static async ValueTask RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("┌───────────────────────────────────────────┐");
        Console.WriteLine("│  Guard Patrol – Scoped Blackboards Demo   │");
        Console.WriteLine("└───────────────────────────────────────────┘");
        Console.ResetColor();

        // ── 1. One graph template for every guard ───────────────────────
        // Sense → Decide (combined agent+context relay) → Switch on the guard's mode.
        // The schemas are declared on the graph, so binding a board over the wrong
        // schema fails fast at WithBlackboard time.
        Graph guardBrain = GraphBuilder
            .StartWithAsync(new SenseState()).SetName("Sense")
            .ToAsync<Guard>((guard, bb, _) =>
            {
                GuardMode mode =
                    bb.Get(WorldKeys.IntruderCaught) ? GuardMode.Rest :
                    bb.Get(GuardKeys.Stamina) < 20 ? GuardMode.Rest :
                    bb.Get(GuardKeys.Suspicion) >= 50 ? GuardMode.Chase :
                    bb.Get(WorldKeys.AlarmLevel) > 0 ? GuardMode.Investigate :
                    GuardMode.Patrol;
                bb.Set(GuardKeys.Mode, mode);
                return ResultHelpers.Success;
            }).SetName("Decide")
            .Switch(bb => bb.Get(GuardKeys.Mode))
            .CaseAsync(GuardMode.Patrol, new PatrolState())
            .CaseAsync(GuardMode.Investigate, new InvestigateState())
            .CaseAsync(GuardMode.Chase, new ChaseState())
            .CaseAsync(GuardMode.Rest, new RestState())
            .End().SetName("ModeSwitch")
            .WithSchema(GuardKeys.Schema)
            .WithSchema(WorldKeys.Schema)
            .Build();

        // ── 2. One world board, one machine + board + agent per guard ───
        Blackboard world = new(WorldKeys.Schema);

        (Guard guard, Blackboard board, AsyncStateMachine<Guard> fsm)[] squad =
        [
            CreateGuard(guardBrain, world, new Guard("Aldric", +1), startRoom: 0),
            CreateGuard(guardBrain, world, new Guard("Benna", +1), startRoom: 4),
        ];

        // ── 3. Run the first shift ───────────────────────────────────────
        int tick = 0;
        while (tick < 3)
        {
            await Tick(world, squad, tick++);
        }

        // ── 4. Save the world + every guard's board, then "restart" ─────
        // Boards are independent durability artifacts; between runs the machines are idle,
        // so no machine snapshot is needed here — the boards ARE the simulation state.
        BlackboardSerializer serializer = new();

        MemoryStream worldSave = new();
        await serializer.ToJsonAsync(world, worldSave);
        List<MemoryStream> guardSaves = [];
        foreach ((_, Blackboard board, _) in squad)
        {
            MemoryStream save = new();
            await serializer.ToJsonAsync(board, save);
            guardSaves.Add(save);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  💾 Saved 1 world board + 2 guard boards. Simulating an app restart...");
        Console.ResetColor();

        // Fresh boards over the same schemas (schemas are code), fresh machines over the
        // same graph template — then restore each payload into its new board.
        Blackboard restoredWorld = new(WorldKeys.Schema);
        worldSave.Position = 0;
        await serializer.RestoreFromJsonAsync(restoredWorld, worldSave);

        (Guard guard, Blackboard board, AsyncStateMachine<Guard> fsm)[] restoredSquad =
        [
            CreateGuard(guardBrain, restoredWorld, new Guard("Aldric", +1), startRoom: 0),
            CreateGuard(guardBrain, restoredWorld, new Guard("Benna", +1), startRoom: 4),
        ];
        for (int i = 0; i < restoredSquad.Length; i++)
        {
            guardSaves[i].Position = 0;
            await serializer.RestoreFromJsonAsync(restoredSquad[i].board, guardSaves[i]);
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  💾 Restored. Alarm level {restoredWorld.Get(WorldKeys.AlarmLevel)}, " +
                          $"sightings {restoredWorld.Get(WorldKeys.Sightings)} — the shift continues.");
        Console.ResetColor();

        // ── 5. Second shift: resume until the intruder is caught ────────
        while (!restoredWorld.Get(WorldKeys.IntruderCaught) && tick < IntruderPath.Length)
        {
            await Tick(restoredWorld, restoredSquad, tick++);
        }

        Console.WriteLine();
        if (restoredWorld.Get(WorldKeys.IntruderCaught))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ Intruder caught by {restoredWorld.Get(WorldKeys.CaughtBy)} " +
                              $"after {restoredWorld.Get(WorldKeys.Sightings)} sighting(s).");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ❌ The intruder slipped away.");
        }

        Console.ResetColor();
    }

    private static (Guard, Blackboard, AsyncStateMachine<Guard>) CreateGuard(
        Graph guardBrain, Blackboard world, Guard guard, int startRoom)
    {
        Blackboard board = new(GuardKeys.Schema);
        board.Set(GuardKeys.Room, startRoom);

        AsyncStateMachine<Guard> fsm = guardBrain.ToAsyncStateMachine<Guard>()
            .WithBlackboard(world) // routed to the Global slot by the schema's scope
            .WithBlackboard(board) // routed to the Graph slot
            .WithAgent(guard);     // the agent channel is untouched by all of this

        return (guard, board, fsm);
    }

    private static async ValueTask Tick(
        Blackboard world, (Guard guard, Blackboard board, AsyncStateMachine<Guard> fsm)[] squad, int tick)
    {
        if (!world.Get(WorldKeys.IntruderCaught))
        {
            world.Set(WorldKeys.IntruderRoom, IntruderPath[tick]);
        }

        Console.WriteLine();
        Console.WriteLine($"  ── Tick {tick + 1} — intruder sneaks through room {world.Get(WorldKeys.IntruderRoom)} ──");

        foreach ((_, _, AsyncStateMachine<Guard> fsm) in squad)
        {
            await fsm.ExecuteAsync(); // context + agent are re-stamped at every run start
        }
    }
}
