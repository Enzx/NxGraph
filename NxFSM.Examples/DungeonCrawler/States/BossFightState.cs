using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// Multi-phase boss fight implemented as a <b>hierarchical FSM</b>: this state
/// builds and executes a child <see cref="StateMachine{TAgent}"/> internally,
/// demonstrating nested / composite state machines.
/// <para>
/// The child FSM has three phases chained with the fluent DSL, each phase
/// being a <see cref="State{TAgent}"/> with full lifecycle.
/// </para>
/// </summary>
public sealed class BossFightState : State<DungeonContext>
{
    protected override void OnEnter()
    {
        Log($"🐉 {Agent.BossName} rises from the shadows!  HP:{Agent.BossHp}/{Agent.BossMaxHp}");
        Log("═══════════════════════════════════════════════");
    }

    protected override Result OnRun()
    {
        // Create a forwarding observer so the child FSM's Log() calls
        // are surfaced through the parent state machine's observer.
        LogForwardingObserver childObserver = new(SyncLogReport);

        // Build a child FSM with three distinct boss phases using the DSL.
        StateMachine<DungeonContext> childFsm = GraphBuilder
            .StartWith(new BossPhaseState(
                phaseName: "Phase 1 – Infernal Breath",
                bossAttackBonus: 0,
                heroAttackBonus: 5,
                phaseThresholdPct: 0.60))
                .SetName("BossPhase1")
            .To(new BossPhaseState(
                phaseName: "Phase 2 – Berserk Rage",
                bossAttackBonus: 8,
                heroAttackBonus: 0,
                phaseThresholdPct: 0.25))
                .SetName("BossPhase2")
            .To(new BossPhaseState(
                phaseName: "Phase 3 – Death Throes",
                bossAttackBonus: 4,
                heroAttackBonus: 10,
                phaseThresholdPct: 0.0))
                .SetName("BossPhase3")
            .ToStateMachine<DungeonContext>(childObserver)
            .WithAgent(Agent);

        Result result = childFsm.Execute();

        if (result == Result.Success && Agent.BossHp <= 0)
        {
            Agent.BossDefeated = true;
            Log($"🏆 {Agent.BossName} has been vanquished!");
        }

        return result;
    }

    protected override void OnExit()
    {
        Log("═══════════════════════════════════════════════");
        Log("The boss chamber falls silent.");
    }

    // ── Inner observer: forwards child log reports to the parent ────────

    /// <summary>
    /// Minimal <see cref="IStateMachineObserver"/> that forwards
    /// <see cref="IStateMachineObserver.OnLogReport"/> calls to the
    /// parent state's <see cref="State.SyncLogReport"/> callback.
    /// </summary>
    private sealed class LogForwardingObserver(Action<string>? parentLog) : IStateMachineObserver
    {
        public void OnLogReport(NodeId nodeId, string message)
        {
            parentLog?.Invoke(message);
        }
    }

    // ── Inner state: a single boss phase ────────────────────────────────

    /// <summary>
    /// One phase of the boss fight. Runs combat rounds until the boss's HP
    /// drops below the given threshold percentage, or the hero dies.
    /// </summary>
    private sealed class BossPhaseState(
        string phaseName,
        int bossAttackBonus,
        int heroAttackBonus,
        double phaseThresholdPct) : State<DungeonContext>
    {
        protected override void OnEnter()
        {
            Log($"--- {phaseName} ---");
        }

        protected override Result OnRun()
        {
            int targetHp = (int)(Agent.BossMaxHp * phaseThresholdPct);
            int round = 0;

            while (Agent.BossHp > targetHp && Agent.HeroAlive)
            {
                round++;

                // Hero attacks boss
                int heroHit = Agent.HeroAttack + heroAttackBonus + Agent.Rng.Next(-2, 4);
                Agent.BossHp = Math.Max(0, Agent.BossHp - heroHit);
                Log($"  [{phaseName}] Rd {round}: {Agent.HeroName} → {Agent.BossName} for {heroHit} dmg  (Boss HP:{Agent.BossHp})");

                if (Agent.BossHp <= targetHp) break;

                // Boss attacks hero
                int bossRaw = Agent.BossAttack + bossAttackBonus + Agent.Rng.Next(-2, 3);
                int dmg = Agent.TakeDamage(bossRaw);
                Log($"  [{phaseName}] Rd {round}: {Agent.BossName} → {Agent.HeroName} for {dmg} dmg  (Hero HP:{Agent.HeroHp}/{Agent.HeroMaxHp})");

                // Auto-potion
                int healed = Agent.TryUsePotion();
                if (healed > 0)
                {
                    Log($"  🧪 Emergency potion! +{healed} HP  (Potions left: {Agent.Potions})");
                }
            }

            if (!Agent.HeroAlive)
            {
                Log($"  ✗ {Agent.HeroName} was slain by {Agent.BossName} during {phaseName}!");
                return Result.Failure;
            }

            return Result.Success;
        }

        protected override void OnExit()
        {
            if (Agent.HeroAlive)
            {
                Log($"  {phaseName} complete.  Boss HP:{Agent.BossHp}  Hero HP:{Agent.HeroHp}/{Agent.HeroMaxHp}");
            }
        }
    }
}

