using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// First state – the hero steps into the dungeon.
/// Demonstrates <see cref="State{TAgent}.OnEnter"/>, <see cref="State{TAgent}.OnRun"/>,
/// <see cref="State{TAgent}.OnExit"/>, and <see cref="State.Log"/>.
/// </summary>
public sealed class EnterDungeonState : State<DungeonContext>
{
    protected override void OnEnter()
    {
        Log("The ancient gate creaks open…");
    }

    protected override Result OnRun()
    {
        Log($"Hero {Agent.HeroName} enters the Dungeon of Shadows.");
        Log($"  HP: {Agent.HeroHp}/{Agent.HeroMaxHp}  ATK: {Agent.HeroAttack}  DEF: {Agent.HeroDefense}");
        Log($"  Potions: {Agent.Potions}  Gold: {Agent.Gold}");
        Log($"  Rooms to explore: {Agent.TotalRooms} + Boss chamber");
        return Result.Success;
    }

    protected override void OnExit()
    {
        Log("The heavy stone doors slam shut behind you. There is no turning back.");
    }
}

