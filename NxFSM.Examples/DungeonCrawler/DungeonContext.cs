namespace NxFSM.Examples.DungeonCrawler;

/// <summary>
/// Shared mutable game state injected as the agent into every <c>State&lt;DungeonContext&gt;</c>.
/// Holds hero stats, inventory, room progress, and encounter results.
/// </summary>
public sealed class DungeonContext(int seed = 42)
{
    // ── Random ──────────────────────────────────────────────────────────
    public Random Rng { get; } = new(seed);

    // ── Hero stats ──────────────────────────────────────────────────────
    public string HeroName { get; } = "Aldric";
    public int HeroHp { get; set; } = 100;
    public int HeroMaxHp { get; } = 100;
    public int HeroAttack { get; } = 18;
    public int HeroDefense { get; } = 5;

    // ── Inventory ───────────────────────────────────────────────────────
    public int Potions { get; set; } = 2;
    public int Gold { get; set; }
    public int MonstersSlain { get; set; }
    public int TrapsDisarmed { get; set; }
    public int TreasuresFound { get; set; }

    // ── Dungeon progress ────────────────────────────────────────────────
    public int RoomNumber { get; set; }
    public int TotalRooms { get; } = 7;
    public EncounterType CurrentEncounter { get; set; } = EncounterType.Empty;

    // ── Boss ────────────────────────────────────────────────────────────
    public bool BossDefeated { get; set; }
    public int BossHp { get; set; } = 150;
    public int BossMaxHp { get; } = 150;
    public int BossAttack { get; } = 22;
    public string BossName { get; } = "Valthor the Undying";

    // ── Derived ─────────────────────────────────────────────────────────
    public bool HeroAlive => HeroHp > 0;

    /// <summary>
    /// Automatically quaffs a potion when health drops below 30 %.
    /// Returns the amount healed, or 0 if no potion was used.
    /// </summary>
    public int TryUsePotion()
    {
        if (Potions <= 0 || HeroHp >= HeroMaxHp * 0.30)
        {
            return 0;
        }

        Potions--;
        int heal = 35;
        int before = HeroHp;
        HeroHp = Math.Min(HeroHp + heal, HeroMaxHp);
        return HeroHp - before;
    }

    /// <summary>
    /// Applies damage to the hero, clamping HP to 0.
    /// </summary>
    public int TakeDamage(int rawDamage)
    {
        int effective = Math.Max(1, rawDamage - HeroDefense);
        HeroHp = Math.Max(0, HeroHp - effective);
        return effective;
    }
}

