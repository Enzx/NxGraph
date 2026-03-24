using System.Text;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxFSM.Examples.DungeonCrawler.States;

namespace NxFSM.Examples.DungeonCrawler;

/// <summary>
/// Comprehensive NxGraph example: a video-game dungeon crawler built with
/// the <b>synchronous</b> <see cref="StateMachine"/> and <see cref="State{TAgent}"/>.
/// <para>
/// Features demonstrated:
/// <list type="bullet">
///   <item>Custom <see cref="State{TAgent}"/> classes with full lifecycle (OnEnter / OnRun / OnExit)</item>
///   <item>Agent propagation via <see cref="StateMachine{TAgent}"/> + <c>WithAgent</c></item>
///   <item><see cref="SwitchState{TKey}"/> branching (encounter routing)</item>
///   <item><see cref="ChoiceState"/> branching (alive check, boss-defeated check)</item>
///   <item>Graph cycles (loop back to Explore after each encounter)</item>
///   <item>Hierarchical / nested FSM (boss fight is a child <see cref="StateMachine"/>)</item>
///   <item><see cref="IStateMachineObserver"/> for full event tracing</item>
///   <item><see cref="State.Log"/> reports routed through the observer</item>
///   <item><c>GraphBuilder.SetName</c> for human-readable node names</item>
///   <item>Mermaid graph export via <see cref="GraphExportExtensions.ToMermaid"/></item>
/// </list>
/// </para>
/// </summary>
public static class DungeonCrawlerExample
{
    public static void Run(int seed = 42)
    {
        // Ensure the console can render UTF-8 characters (emojis, box-drawing, etc.)
        Console.OutputEncoding = Encoding.UTF8;

        // ── 1. Shared game state (agent) ────────────────────────────────
        DungeonContext ctx = new(seed);
        DungeonObserver observer = new();

        // ── 2. Create state instances ───────────────────────────────────
        EnterDungeonState enterDungeon = new();
        ExploreRoomState  explore      = new();
        CombatState       combat       = new();
        TreasureRoomState treasure     = new();
        TrapRoomState     trap         = new();
        EmptyRoomState    emptyRoom    = new();
        BossFightState    bossFight    = new();
        VictoryState      victory      = new();
        DefeatState       defeat       = new();

        // ── 3. Build the graph using Fluent DSL + Manual Wiring ─────────
        
        // StartWith -> Explore
        // Using DSL to start the graph and define the initial sequence
        var exploreToken = GraphBuilder.StartWith(enterDungeon).SetName("EnterDungeon")
            .To(explore).SetName("Explore");

        GraphBuilder builder = exploreToken.Builder;

        // We must drop down to the builder to wire the converging paths (merges) 
        // and loop backs, which the linear DSL does not support directly.
        // Also, for 'SwitchState' to log target names correctly, we must supply
        // the named NodeIds explicitly to its constructor.

        // Add Encounter Nodes and use the named ids directly
        NodeId combatId    = builder.AddNode(combat);    builder.SetName(combatId, "Combat");       combatId = combatId.WithName("Combat");
        NodeId treasureId  = builder.AddNode(treasure);  builder.SetName(treasureId, "Treasure");   treasureId = treasureId.WithName("Treasure");
        NodeId trapId      = builder.AddNode(trap);      builder.SetName(trapId, "Trap");           trapId = trapId.WithName("Trap");
        NodeId bossFightId = builder.AddNode(bossFight); builder.SetName(bossFightId, "BossFight"); bossFightId = bossFightId.WithName("BossFight");
        NodeId emptyRoomId = builder.AddNode(emptyRoom); builder.SetName(emptyRoomId, "EmptyRoom"); emptyRoomId = emptyRoomId.WithName("EmptyRoom");

        // Manually build SwitchState with named nodes
        SwitchState<EncounterType> encounterSwitch = new(
            () => ctx.CurrentEncounter,
            new Dictionary<EncounterType, NodeId>
            {
                { EncounterType.Monster,  combatId },
                { EncounterType.Treasure, treasureId },
                { EncounterType.Trap,     trapId },
                { EncounterType.Boss,     bossFightId },
                { EncounterType.Empty,    emptyRoomId },
            });

        NodeId switchId = builder.AddNode(encounterSwitch);
        builder.SetName(switchId, "EncounterSwitch");
        switchId = switchId.WithName("EncounterSwitch");

        // Wire Explore -> Switch manually
        builder.AddTransition(exploreToken.Id, switchId);

        // Create Terminal Nodes
        NodeId victoryId = builder.AddNode(victory); builder.SetName(victoryId, "Victory"); victoryId = victoryId.WithName("Victory");
        NodeId defeatId  = builder.AddNode(defeat);  builder.SetName(defeatId, "Defeat");   defeatId = defeatId.WithName("Defeat");

        // Build Director Logic (Choice) - resolving loops manually

        // Boss Defeated? -> Victory OR Explore (Loop)
        ChoiceState bossCheck = new(() => ctx.BossDefeated, victoryId, exploreToken.Id);
        NodeId bossCheckId = builder.AddNode(bossCheck);
        builder.SetName(bossCheckId, "BossDefeatedCheck");
        bossCheckId = bossCheckId.WithName("BossDefeatedCheck");

        // Hero Alive? -> BossCheck OR Defeat
        ChoiceState aliveCheck = new(() => ctx.HeroAlive, bossCheckId, defeatId);
        NodeId aliveCheckId = builder.AddNode(aliveCheck);
        builder.SetName(aliveCheckId, "AliveCheck");
        aliveCheckId = aliveCheckId.WithName("AliveCheck");

        // Wire Encounters -> AliveCheck (Many-to-One Merge)
        builder.AddTransition(combatId,    aliveCheckId);
        builder.AddTransition(treasureId,  aliveCheckId);
        builder.AddTransition(trapId,      aliveCheckId);
        builder.AddTransition(bossFightId, aliveCheckId);
        builder.AddTransition(emptyRoomId, aliveCheckId);

        // ── 4. Build the FSM ────────────────────────────────────────────
        Graph graph = builder.Build();
        StateMachine<DungeonContext> fsm = graph
            .ToStateMachine<DungeonContext>(observer)
            .WithAgent(ctx);

        // ── 5. Export graph to Mermaid before execution ─────────────────
        string mermaid = graph.ToMermaid(new MermaidExportOptions
        {
            Direction = FlowDirection.TopToBottom,
            TerminalLabel = "End"
        });

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("┌─────────────────────────────────────┐");
        Console.WriteLine("│   Dungeon Crawler – Graph (Mermaid) │");
        Console.WriteLine("└─────────────────────────────────────┘");
        Console.ResetColor();
        Console.WriteLine(mermaid);
        Console.WriteLine();

        // ── 6. Execute the FSM synchronously ────────────────────────────
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("┌─────────────────────────────────────┐");
        Console.WriteLine("│   Dungeon Crawler – Execution Log   │");
        Console.WriteLine("└─────────────────────────────────────┘");
        Console.ResetColor();
        Console.WriteLine();

        Result result = fsm.Execute();

        Console.WriteLine();
        ConsoleColor resultColour = result == Result.Success ? ConsoleColor.Green : ConsoleColor.Red;
        Console.ForegroundColor = resultColour;
        Console.WriteLine($"Dungeon Crawler finished with result: {result}");
        Console.ResetColor();
    }
}

