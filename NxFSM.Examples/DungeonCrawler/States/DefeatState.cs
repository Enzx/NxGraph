using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// Terminal failure state — the hero has perished.
/// Returns <see cref="Result.Failure"/> so the FSM completes with a failure result.
/// </summary>
public sealed class DefeatState : State<DungeonContext>
{
    protected override void OnEnter()
    {
        Log("════════════════════════════════════════════════");
        Log("            ✗  G A M E   O V E R  ✗           ");
        Log("════════════════════════════════════════════════");
    }

    protected override Result OnRun()
    {
        Log($"  {Agent.HeroName} fell in the Dungeon of Shadows.");
        Log($"  Rooms explored:  {Agent.RoomNumber}");
        Log($"  Gold collected:  {Agent.Gold}");
        Log($"  Monsters slain:  {Agent.MonstersSlain}");
        return Result.Failure;
    }

    protected override void OnExit()
    {
        Log("Perhaps another hero will fare better…");
    }
}

