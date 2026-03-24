using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// The hero fights a random monster.
/// Combat is a simple loop of trading blows until one side falls.
/// Always returns <see cref="Result.Success"/> — the alive-check is done by a
/// downstream <see cref="NxGraph.Fsm.ChoiceState"/> director node.
/// </summary>
public sealed class CombatState : State<DungeonContext>
{
    private static readonly string[] MonsterNames =
        ["Skeleton Warrior", "Goblin Shaman", "Stone Golem", "Shadow Wraith", "Cave Troll"];

    private string _monsterName = string.Empty;
    private int _monsterHp;
    private int _monsterAttack;

    protected override void OnEnter()
    {
        _monsterName = MonsterNames[Agent.Rng.Next(MonsterNames.Length)];
        _monsterHp = 30 + Agent.Rng.Next(30);         // 30-59 HP
        _monsterAttack = 8 + Agent.Rng.Next(10);       // 8-17 ATK
        Log($"⚔ A wild {_monsterName} appears!  HP:{_monsterHp}  ATK:{_monsterAttack}");
    }

    protected override Result OnRun()
    {
        int round = 0;
        while (_monsterHp > 0 && Agent.HeroAlive)
        {
            round++;

            // Hero strikes
            int heroHit = Agent.HeroAttack + Agent.Rng.Next(-3, 4);
            _monsterHp = Math.Max(0, _monsterHp - heroHit);
            Log($"  Round {round}: {Agent.HeroName} deals {heroHit} damage → {_monsterName} HP:{_monsterHp}");

            if (_monsterHp <= 0) break;

            // Monster strikes
            int dmg = Agent.TakeDamage(_monsterAttack + Agent.Rng.Next(-2, 3));
            Log($"  Round {round}: {_monsterName} deals {dmg} damage → {Agent.HeroName} HP:{Agent.HeroHp}/{Agent.HeroMaxHp}");

            // Auto-potion
            int healed = Agent.TryUsePotion();
            if (healed > 0)
            {
                Log($"  🧪 {Agent.HeroName} quaffs a potion! +{healed} HP → HP:{Agent.HeroHp}/{Agent.HeroMaxHp}  (Potions left: {Agent.Potions})");
            }
        }

        if (_monsterHp <= 0)
        {
            int goldDrop = 10 + Agent.Rng.Next(20);
            Agent.Gold += goldDrop;
            Agent.MonstersSlain++;
            Log($"✓ {_monsterName} defeated!  +{goldDrop} gold  (Total: {Agent.Gold})");
        }
        else
        {
            Log($"✗ {Agent.HeroName} has fallen in battle against {_monsterName}!");
        }

        return Result.Success;
    }

    protected override void OnExit()
    {
        Log($"Combat ended. Hero HP: {Agent.HeroHp}/{Agent.HeroMaxHp}");
    }
}

