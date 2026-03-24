using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// The hero triggers a trap and takes damage.
/// Always returns <see cref="Result.Success"/> — the alive-check is downstream.
/// </summary>
public sealed class TrapRoomState : State<DungeonContext>
{
    private static readonly string[] TrapTypes =
        ["poison dart", "spike pit", "falling boulder", "fire glyph", "freezing rune"];

    protected override void OnEnter()
    {
        Log("⚠ TRAP TRIGGERED!");
    }

    protected override Result OnRun()
    {
        string trap = TrapTypes[Agent.Rng.Next(TrapTypes.Length)];
        int rawDamage = 10 + Agent.Rng.Next(15);
        int actual = Agent.TakeDamage(rawDamage);

        Log($"  A {trap} hits {Agent.HeroName} for {actual} damage! HP:{Agent.HeroHp}/{Agent.HeroMaxHp}");

        Agent.TrapsDisarmed++;

        // Auto-potion
        int healed = Agent.TryUsePotion();
        if (healed > 0)
        {
            Log($"  🧪 Emergency potion! +{healed} HP → HP:{Agent.HeroHp}/{Agent.HeroMaxHp}  (Potions left: {Agent.Potions})");
        }

        if (!Agent.HeroAlive)
        {
            Log($"  ✗ {Agent.HeroName} succumbed to the trap…");
        }

        return Result.Success;
    }

    protected override void OnExit()
    {
        Log("The trap has been disarmed.");
    }
}

