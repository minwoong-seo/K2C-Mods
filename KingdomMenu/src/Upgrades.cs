using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomMenu;

internal enum Category
{
    Workers,
    Army,
    Monarch,
    Kingdom,
}

/// <summary>
/// One purchasable upgrade. Every level adds <see cref="PerLevel"/> to a stat multiplier (1 + PerLevel * level);
/// the next level costs BaseCost * Growth^level coins.
/// </summary>
internal sealed class Upgrade
{
    public string Id;
    public string Name;
    public string Stat;
    public Category Category;
    public string[] Icons;
    public int MaxLevel;
    public int BaseCost;
    public float Growth = 1.5f;
    public float PerLevel;

    /// <summary>Levels are kept per player (the steed upgrades: each monarch upgrades their own mount).</summary>
    public bool PerPlayer;

    /// <summary>Hidden until this returns true (so the menu doesn't spoil units not reached yet); null = always shown.</summary>
    public Func<bool> Unlocked;

    /// <summary>The value at a level, for upgrades that aren't a multiplier (e.g. "2.5" reach, "+1" coin); null = "x1.15".</summary>
    public Func<int, string> Value;

    /// <summary>Key in the levels file; per-player upgrades get "_p1"/"_p2".</summary>
    public string Key(int player) => PerPlayer ? $"{Id}_p{player + 1}" : Id;

    public float Multiplier(int level) => 1f + PerLevel * level;

    public int Cost(int level) => (int)Math.Round(BaseCost * Math.Pow(Growth, level), MidpointRounding.AwayFromZero);

    // Shown as multipliers ("x1.15"): in the game's pixel font '%', '|', '>' and '~' are controller-button icons, not symbols.
    public string Effect(int level)
    {
        var now = Format(level);
        var next = Format(level + 1);
        if (level >= MaxLevel)
            return $"{now} {Stat} (max)";
        return level == 0 ? $"Next: {next} {Stat}" : $"{now}, next {next}";
    }

    private string Format(int level) => Value != null ? Value(level) : "x" + Multiplier(level).ToString("0.##", Invariant);

    internal static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
}

internal static class Upgrades
{
    public static readonly Upgrade BuilderWork = new()
    {
        Id = "builder_work", Name = "Builder work speed", Stat = "work speed", Category = Category.Workers,
        Icons = new[] { "tools_hammer" }, MaxLevel = 5, BaseCost = 4, PerLevel = 0.15f,
    };

    public static readonly Upgrade BuilderMove = new()
    {
        Id = "builder_move", Name = "Builder movement", Stat = "speed", Category = Category.Workers,
        Icons = new[] { "worker_idle_0", "tools_hammer" }, MaxLevel = 5, BaseCost = 3, PerLevel = 0.10f,
    };

    public static readonly Upgrade VillagerMove = new()
    {
        Id = "villager_move", Name = "Villager movement", Stat = "speed", Category = Category.Workers,
        Icons = new[] { "peasant_idle_0", "farmer_idle_0" }, MaxLevel = 5, BaseCost = 3, PerLevel = 0.10f,
    };

    public static readonly Upgrade RecruitSpeed = new()
    {
        Id = "recruit_speed", Name = "Faster recruits", Stat = "recruiting", Category = Category.Workers,
        Icons = new[] { "beggar_idle_0", "peasant_idle_0" }, MaxLevel = 3, BaseCost = 5, PerLevel = 0.25f,
    };

    public static readonly Upgrade ArcherRate = new()
    {
        Id = "archer_rate", Name = "Archer fire rate", Stat = "fire rate", Category = Category.Army,
        Icons = new[] { "tools_bow" }, MaxLevel = 5, BaseCost = 5, PerLevel = 0.15f,
    };

    public static readonly Upgrade ArcherDamage = new()
    {
        Id = "archer_damage", Name = "Archer damage", Stat = "damage", Category = Category.Army,
        Icons = new[] { "archer_idle1_0", "tools_bow" }, MaxLevel = 4, BaseCost = 6, PerLevel = 0.25f,
    };

    public static readonly Upgrade ArcherRange = new()
    {
        Id = "archer_range", Name = "Archer range", Stat = "range", Category = Category.Army,
        Icons = new[] { "arrow", "tools_bow" }, MaxLevel = 3, BaseCost = 6, PerLevel = 0.10f,
    };

    public static readonly Upgrade SoldierDamage = new()
    {
        Id = "soldier_damage", Name = "Soldier damage", Stat = "damage", Category = Category.Army,
        Icons = new[] { "soldier" }, Unlocked = () => Unlocks.AnySoldier, MaxLevel = 4, BaseCost = 6, PerLevel = 0.25f,
    };

    public static readonly Upgrade SoldierMove = new()
    {
        Id = "soldier_move", Name = "Soldier movement", Stat = "speed", Category = Category.Army,
        Icons = new[] { "soldier", "archer_idle1_0" }, MaxLevel = 5, BaseCost = 3, PerLevel = 0.10f,
    };

    public static readonly Upgrade SteedStamina = new()
    {
        Id = "steed_stamina", PerPlayer = true, Name = "Steed stamina", Stat = "stamina", Category = Category.Monarch,
        Icons = new[] { "steed" }, MaxLevel = 5, BaseCost = 4, PerLevel = 0.20f,
    };

    public static readonly Upgrade SteedSpeed = new()
    {
        Id = "steed_speed", PerPlayer = true, Name = "Steed speed", Stat = "speed", Category = Category.Monarch,
        Icons = new[] { "steed" }, MaxLevel = 5, BaseCost = 5, PerLevel = 0.06f,
    };

    /// <summary>Reach of the coin magnet at a level, in world units (about 20 fit across the screen).</summary>
    public static float MagnetReach(int level) => level <= 0 ? 0f : 0.5f + level;

    public static readonly Upgrade CoinMagnet = new()
    {
        Id = "coin_magnet", PerPlayer = true, Name = "Coin magnet", Stat = "reach", Category = Category.Monarch,
        Icons = new[] { "coin_spin_0" }, MaxLevel = 4, BaseCost = 4,
        Value = l => MagnetReach(l).ToString("0.#", Upgrade.Invariant),
    };

    public static readonly Upgrade QuickHands = new()
    {
        Id = "quick_hands", PerPlayer = true, Name = "Quick hands", Stat = "paying speed", Category = Category.Monarch,
        Icons = new[] { "coin_indicator", "coin_spin_0" }, MaxLevel = 3, BaseCost = 3, PerLevel = 0.25f,
    };

    // The purse is a physics bag holding about 25 coins before they spill (half of spilled coins are lost in the water).
    // Each level makes the bag 25% bigger each way, so it holds about 39 / 56 / 77.
    public static readonly Upgrade BiggerPurse = new()
    {
        Id = "bigger_purse", PerPlayer = true, Name = "Bigger purse", Stat = "coins held", Category = Category.Monarch,
        Icons = new[] { "coinbag_closed", "coin_spin_0" }, MaxLevel = 3, BaseCost = 6, Growth = 1.6f,
        // About that many: the bag is physics. (No '~': in the game's pixel font it's a controller-button icon.)
        Value = l => PurseSize.Capacity(l).ToString(),
    };

    public static readonly Upgrade WallToughness = new()
    {
        Id = "wall_toughness", Name = "Wall toughness", Stat = "toughness", Category = Category.Kingdom,
        Icons = new[] { "wall", "wall_0" }, MaxLevel = 5, BaseCost = 5, PerLevel = 0.20f,
    };

    public static readonly Upgrade WallRepair = new()
    {
        Id = "wall_repair", Name = "Wall repair", Stat = "repair", Category = Category.Kingdom,
        Icons = new[] { "tools_hammer" }, MaxLevel = 4, BaseCost = 4, PerLevel = 0.25f,
    };

    // Income upgrades are priced as investments: each Bank interest level adds at most ~2 coins a day (pays back in
    // 5-20 days); each Harvest yield level adds 1 coin per farm-plot harvest (6 -> 7) for the whole kingdom.
    public static readonly Upgrade BankInterest = new()
    {
        Id = "bank_interest", Name = "Bank interest", Stat = "interest", Category = Category.Kingdom,
        Icons = new[] { "banker_idle_0" }, MaxLevel = 4, BaseCost = 10, Growth = 1.6f, PerLevel = 0.25f,
        Unlocked = () => Unlocks.HasBanker,
    };

    public static readonly Upgrade HarvestYield = new()
    {
        Id = "harvest_yield", Name = "Harvest yield", Stat = "coin/harvest", Category = Category.Kingdom,
        Icons = new[] { "tools_scythe" }, MaxLevel = 3, BaseCost = 25, Growth = 1.6f,
        Value = l => $"+{l}", Unlocked = () => Unlocks.Has(Unlocks.Farmers),
    };

    public static readonly IReadOnlyList<Upgrade> All = new[]
    {
        BuilderWork, BuilderMove, VillagerMove, RecruitSpeed,
        ArcherRate, ArcherDamage, ArcherRange, SoldierDamage, SoldierMove,
        SteedStamina, SteedSpeed, CoinMagnet, QuickHands, BiggerPurse,
        WallToughness, WallRepair, BankInterest, HarvestYield,
    };

    public static List<Upgrade> In(Category category) => All.Where(u => u.Category == category).ToList();

    public static int MaxPerCategory => Enum.GetValues(typeof(Category)).Cast<Category>().Max(c => In(c).Count);
}
