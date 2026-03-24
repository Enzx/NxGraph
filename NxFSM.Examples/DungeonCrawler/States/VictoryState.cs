using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// Terminal success state — the hero emerges victorious.
/// Prints a summary of the adventure.
/// </summary>
public sealed class VictoryState : State<DungeonContext>
{
    protected override void OnEnter()
    {
        Log("════════════════════════════════════════════════");
        Log("              ✦  V I C T O R Y  ✦             ");
        Log("════════════════════════════════════════════════");
    }

    protected override Result OnRun()
    {
        Log($"  Hero:            {Agent.HeroName}");
        Log($"  Final HP:        {Agent.HeroHp}/{Agent.HeroMaxHp}");
        Log($"  Gold collected:  {Agent.Gold}");
        Log($"  Monsters slain:  {Agent.MonstersSlain}");
        Log($"  Traps survived:  {Agent.TrapsDisarmed}");
        Log($"  Treasures found: {Agent.TreasuresFound}");
        Log($"  Rooms explored:  {Agent.RoomNumber}");
        Log($"  Potions left:    {Agent.Potions}");
        Log($"  Boss:            {Agent.BossName} — DEFEATED");
        return Result.Success;
    }

    protected override void OnExit()
    {
        Log("The hero steps into the sunlight, the dungeon conquered at last.");
    }
}

