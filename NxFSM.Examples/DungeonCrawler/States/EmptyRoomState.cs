using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// An empty room – the hero catches a brief respite and recovers a few HP.
/// </summary>
public sealed class EmptyRoomState : State<DungeonContext>
{
    protected override void OnEnter()
    {
        Log("🕯 The chamber is quiet and still.");
    }

    protected override Result OnRun()
    {
        Log("  Nothing of interest here… but the silence lets you rest briefly.");

        int recover = 5;
        int before = Agent.HeroHp;
        Agent.HeroHp = Math.Min(Agent.HeroHp + recover, Agent.HeroMaxHp);
        int actual = Agent.HeroHp - before;

        if (actual > 0)
        {
            Log($"  💚 Recovered {actual} HP → HP:{Agent.HeroHp}/{Agent.HeroMaxHp}");
        }

        return Result.Success;
    }

    protected override void OnExit()
    {
        Log("You move on, torchlight flickering against damp stone walls.");
    }
}

