using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// The hero finds a treasure chest containing gold and sometimes a potion.
/// </summary>
public sealed class TreasureRoomState : State<DungeonContext>
{
    protected override void OnEnter()
    {
        Log("💰 You discover a treasure chest buried under rubble!");
    }

    protected override Result OnRun()
    {
        int gold = 20 + Agent.Rng.Next(30);
        Agent.Gold += gold;
        Agent.TreasuresFound++;
        Log($"  +{gold} gold found!  (Total: {Agent.Gold})");

        // 50 % chance of finding a potion
        if (Agent.Rng.Next(2) == 0)
        {
            Agent.Potions++;
            Log($"  🧪 A healing potion was hidden inside!  (Potions: {Agent.Potions})");
        }

        return Result.Success;
    }

    protected override void OnExit()
    {
        Log("You pocket the loot and press onward.");
    }
}

