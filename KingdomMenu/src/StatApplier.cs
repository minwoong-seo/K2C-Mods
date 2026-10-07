using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>
/// Applies upgrade levels to the live units by scaling their own tuning fields
/// (e.g. Worker.workTime, Archer.shootCooldownTime, Steed.runSpeed).
///
/// Each field's original value is remembered per object, and the field is always written as original * multiplier,
/// so re-applying never compounds and dropping a level (or a reset) restores the original. If the game changes a field
/// itself (the value no longer matches what we wrote), that new value becomes the original.
/// </summary>
internal sealed class StatApplier
{
    private struct Entry
    {
        public float Base;
        public float Written;
    }

    /// <summary>The one MenuHost runs, so pages can show a stat's base value next to the current one.</summary>
    internal static StatApplier Current;

    /// <summary>Purse size the banker gets while Banker keeps collecting is on (see BankerDropOffPatch).</summary>
    private const int BankerCapacity = 999;

    private Dictionary<(IntPtr, string), Entry> _cache = new();
    private Dictionary<(IntPtr, string), Entry> _next = new();
    private bool _loggedBanker;

    /// <summary>A field's value before any upgrade or penalty (what it would be unmodded), or fallback if it isn't changed.</summary>
    public float BaseOf(Il2CppObjectBase obj, string field, float fallback) =>
        obj != null && _cache.TryGetValue((obj.Pointer, field), out var entry) ? entry.Base : fallback;

    public void Apply()
    {
        var playing = GameState.Playing();
        // Kingdom-wide upgrades use player index 0 (it's ignored for them); steed upgrades are per player.
        float L(Upgrade u, int player = 0) => playing ? u.Multiplier(UpgradeStore.Level(u, player)) : 1f;

        var builderWork = L(Upgrades.BuilderWork);
        var builderMove = L(Upgrades.BuilderMove);
        var villagerMove = L(Upgrades.VillagerMove);
        // With soldier wages on, army upgrades only work on an island with a bank (Wages); unpaid wages make every
        // soldier hit softer (damage only, so archers and knights lose the same).
        var armyOn = playing && !Wages.ArmyNeedsBank;
        float A(Upgrade u) => armyOn ? L(u) : 1f;
        var strength = playing ? Wages.Strength : 1f;
        var archerRate = A(Upgrades.ArcherRate);
        var soldierMove = A(Upgrades.SoldierMove);
        float[] steedStamina = { L(Upgrades.SteedStamina, 0), L(Upgrades.SteedStamina, 1) };
        float[] steedSpeed = { L(Upgrades.SteedSpeed, 0), L(Upgrades.SteedSpeed, 1) };
        var ridden = new[] { Coop.Monarch(0)?.steed, Coop.Monarch(1)?.steed };
        var wallRepair = L(Upgrades.WallRepair);
        var recruit = L(Upgrades.RecruitSpeed);
        var archerRange = A(Upgrades.ArcherRange);
        var bank = L(Upgrades.BankInterest);
        var harvest = playing ? UpgradeStore.Level(Upgrades.HarvestYield, 0) : 0;
        float[] quickHands = { L(Upgrades.QuickHands, 0), L(Upgrades.QuickHands, 1) };
        var kingdom = Coop.Kingdom;
        var monarchs = new[] { kingdom != null ? kingdom.playerOne : null, kingdom != null ? kingdom.playerTwo : null };

        // Damage is changed per hit in DamagePatch rather than on objects.
        DamagePatch.ArcherMultiplier = A(Upgrades.ArcherDamage) * strength;
        DamagePatch.SoldierMultiplier = A(Upgrades.SoldierDamage) * strength;
        DamagePatch.WallDamageMultiplier = 1f / L(Upgrades.WallToughness);

        _next.Clear();

        if (playing)
        {
            // Builders: each work tick waits max(workTime, job's own minimum), so a shorter workTime means faster work.
            ForEach<Worker>(w =>
            {
                Scale(w, "workTime", () => w.workTime, v => w.workTime = v, b => b / builderWork);
                Scale(w, "walkSpeed", () => w.walkSpeed, v => w.walkSpeed = v, b => b * builderMove);
                Scale(w, "runSpeed", () => w.runSpeed, v => w.runSpeed = v, b => b * builderMove);
            });

            ForEach<Peasant>(p =>
            {
                Scale(p, "walkSpeed", () => p.walkSpeed, v => p.walkSpeed = v, b => b * villagerMove);
                Scale(p, "runSpeed", () => p.runSpeed, v => p.runSpeed = v, b => b * villagerMove);
            });

            ForEach<Farmer>(f =>
            {
                Scale(f, "_walkSpeed", () => f._walkSpeed, v => f._walkSpeed = v, b => b * villagerMove);
                Scale(f, "_runSpeed", () => f._runSpeed, v => f._runSpeed = v, b => b * villagerMove);
                Scale(f, "_fleeSpeed", () => f._fleeSpeed, v => f._fleeSpeed = v, b => b * villagerMove);
            });

            // Archers: the Shoot routine sets its cooldown from these, then counts it down in Update.
            ForEach<Archer>(a =>
            {
                Scale(a, "shootCooldownTime", () => a.shootCooldownTime, v => a.shootCooldownTime = v, b => b / archerRate);
                Scale(a, "shootCooldownWithKnightTime", () => a.shootCooldownWithKnightTime, v => a.shootCooldownWithKnightTime = v, b => b / archerRate);
                Scale(a, "playerShootCooldownTime", () => a.playerShootCooldownTime, v => a.playerShootCooldownTime = v, b => b / archerRate);
                Scale(a, "walkSpeed", () => a.walkSpeed, v => a.walkSpeed = v, b => b * soldierMove);
                Scale(a, "runSpeed", () => a.runSpeed, v => a.runSpeed = v, b => b * soldierMove);
                Scale(a, "shootRange", () => a.shootRange, v => a.shootRange = v, b => b * archerRange);
                Scale(a, "towerShootRange", () => a.towerShootRange, v => a.towerShootRange = v, b => b * archerRange);
            });

            // Recruits: cottages refill a villager every _cooldownOfSpawning seconds, vagrant camps spawn every spawnInterval.
            ForEach<CitizenHousePayable>(c =>
                Scale(c, "_cooldownOfSpawning", () => c._cooldownOfSpawning, v => c._cooldownOfSpawning = v, b => b / recruit));
            ForEach<BeggarCamp>(c => Scale(c, "spawnInterval", () => c.spawnInterval, v => c.spawnInterval = v, b => b / recruit));

            // Paying: the monarch drops one coin every timeBetweenCoins seconds while paying.
            ForEach<Player>(p =>
            {
                var index = Index(p, monarchs);
                var rate = index >= 0 ? quickHands[index] : 1f;
                Scale(p, "timeBetweenCoins", () => p.timeBetweenCoins, v => p.timeBetweenCoins = v, b => b / rate);
                Scale(p, "timeBetweenCoinsForTouchSupport", () => p.timeBetweenCoinsForTouchSupport, v => p.timeBetweenCoinsForTouchSupport = v, b => b / rate);
            });

            // Bank: at dawn the banker adds min(maxInterest, ceil(stash * dailyInterest)); both scale.
            // Banker keeps collecting: he stops picking coins up at TotalCapacity * coinGatherTargetPercentage of them
            // (and at TotalCapacity), so both are lifted; BankerDropOffPatch decides when he takes them to the bank.
            var keepCollecting = Plugin.BankerKeepsCollecting.Value;
            ForEach<Banker>(b =>
            {
                Scale(b, "dailyInterest", () => b.dailyInterest, v => b.dailyInterest = v, x => x * bank);
                Scale(b, "maxInterest", () => b.maxInterest, v => b.maxInterest = (int)Math.Round(v), x => MathF.Round(x * bank));
                Scale(b, "coinGatherTargetPercentage", () => b.coinGatherTargetPercentage, v => b.coinGatherTargetPercentage = v,
                    x => keepCollecting ? Math.Max(x, 1f) : x);
                var w = b._wallet;
                if (w != null)
                    Scale(w, "TotalCapacity", () => w.TotalCapacity, v => w.TotalCapacity = (int)Math.Round(v),
                        x => keepCollecting ? Math.Max(x, BankerCapacity) : x);
                if (!_loggedBanker && w != null)
                {
                    _loggedBanker = true;
                    // (From this pass's entries: the fields have just been written.)
                    var capacity = _next.TryGetValue((w.Pointer, "TotalCapacity"), out var c) ? c.Base : w.TotalCapacity;
                    var target = _next.TryGetValue((b.Pointer, "coinGatherTargetPercentage"), out var t) ? t.Base : b.coinGatherTargetPercentage;
                    Plugin.Logger.LogInfo($"Banker: purse {capacity}, banks at {capacity * target} coins in the game" +
                                          (keepCollecting ? "; Banker keeps collecting is on." : "."));
                }
            });

            // Farms: each plot drops coinYield coins when harvested.
            ForEach<Farmland>(f => Scale(f, "coinYield", () => f.coinYield, v => f.coinYield = (int)Math.Round(v), b => b + harvest));

            ForEach<Knight>(k =>
            {
                Scale(k, "_walkSpeed", () => k._walkSpeed, v => k._walkSpeed = v, b => b * soldierMove);
                Scale(k, "_runSpeed", () => k._runSpeed, v => k._runSpeed = v, b => b * soldierMove);
                Scale(k, "_retreatSpeed", () => k._retreatSpeed, v => k._retreatSpeed = v, b => b * soldierMove);
            });

            ForEach<Pikeman>(p =>
                Scale(p, "_runSpeed", () => p._runSpeed, v => p._runSpeed = v, b => b * soldierMove));

            // Steeds: Player.UpdateActionState does Stamina += rate * dt with signed rates (negative = drain,
            // positive = recovery). More stamina = drain divided, recovery multiplied, bigger second wind.
            // Each steed gets its rider's own levels; an unridden steed goes back to its base stats. Because fields are
            // always written as base * multiplier, a steed changing riders never compounds.
            ForEach<Steed>(s =>
            {
                var rider = Rider(s, ridden);
                var speed = rider >= 0 ? steedSpeed[rider] : 1f;
                var stamina = rider >= 0 ? steedStamina[rider] : 1f;
                Scale(s, "walkSpeed", () => s.walkSpeed, v => s.walkSpeed = v, b => b * speed);
                Scale(s, "runSpeed", () => s.runSpeed, v => s.runSpeed = v, b => b * speed);
                Scale(s, "walkStaminaRate", () => s.walkStaminaRate, v => s.walkStaminaRate = v, b => Stamina(b, stamina));
                Scale(s, "runStaminaRate", () => s.runStaminaRate, v => s.runStaminaRate = v, b => Stamina(b, stamina));
                Scale(s, "glideStaminaRate", () => s.glideStaminaRate, v => s.glideStaminaRate = v, b => Stamina(b, stamina));
                Scale(s, "standStaminaRate", () => s.standStaminaRate, v => s.standStaminaRate = v, b => Stamina(b, stamina));
                Scale(s, "reserveStamina", () => s.reserveStamina, v => s.reserveStamina = v, b => b * stamina);
            });

            // Walls: Wall.OnJobFinish adds repairRate hit points per repair tick.
            ForEach<Wall>(w =>
                Scale(w, "repairRate", () => w.repairRate, v => w.repairRate = (int)v, b => Math.Max(1f, MathF.Round(b * wallRepair))));
        }
        else
        {
            // Left the kingdom (title screen / loading): put back whatever we changed that still exists.
            RestoreAll();
        }

        (_cache, _next) = (_next, _cache);
    }

    /// <summary>0 = player 1's monarch, 1 = player 2's, -1 = neither.</summary>
    private static int Index(Player player, Player[] monarchs)
    {
        for (var i = 0; i < monarchs.Length; i++)
        {
            if (monarchs[i] != null && monarchs[i].Pointer == player.Pointer)
                return i;
        }
        return -1;
    }

    /// <summary>0 = player 1 rides it, 1 = player 2, -1 = nobody.</summary>
    private static int Rider(Steed steed, Steed[] ridden)
    {
        for (var i = 0; i < ridden.Length; i++)
        {
            if (ridden[i] != null && ridden[i].Pointer == steed.Pointer)
                return i;
        }
        return -1;
    }

    private static float Stamina(float baseRate, float multiplier) => baseRate < 0f ? baseRate / multiplier : baseRate * multiplier;

    private static void ForEach<T>(Action<T> apply) where T : Il2CppObjectBase
    {
        var found = Object.FindObjectsByType(Il2CppType.Of<T>(), FindObjectsSortMode.None);
        if (found == null)
            return;
        foreach (var obj in found)
        {
            var typed = obj?.TryCast<T>();
            if (typed != null)
                apply(typed);
        }
    }

    private void Scale(Il2CppObjectBase obj, string field, Func<float> get, Action<float> set, Func<float, float> transform)
    {
        var key = (obj.Pointer, field);
        var current = get();
        if (!_cache.TryGetValue(key, out var entry) || Math.Abs(current - entry.Written) > 0.0001f)
            entry = new Entry { Base = current };

        entry.Written = transform(entry.Base);
        if (Math.Abs(current - entry.Written) > 0.000001f)
            set(entry.Written);
        _next[key] = entry;
    }

    private void RestoreAll()
    {
        if (_cache.Count == 0)
            return;

        ForEach<Worker>(w => { Restore(w, "workTime", v => w.workTime = v); Restore(w, "walkSpeed", v => w.walkSpeed = v); Restore(w, "runSpeed", v => w.runSpeed = v); });
        ForEach<Peasant>(p => { Restore(p, "walkSpeed", v => p.walkSpeed = v); Restore(p, "runSpeed", v => p.runSpeed = v); });
        ForEach<Farmer>(f => { Restore(f, "_walkSpeed", v => f._walkSpeed = v); Restore(f, "_runSpeed", v => f._runSpeed = v); Restore(f, "_fleeSpeed", v => f._fleeSpeed = v); });
        ForEach<Archer>(a =>
        {
            Restore(a, "shootCooldownTime", v => a.shootCooldownTime = v);
            Restore(a, "shootCooldownWithKnightTime", v => a.shootCooldownWithKnightTime = v);
            Restore(a, "playerShootCooldownTime", v => a.playerShootCooldownTime = v);
            Restore(a, "walkSpeed", v => a.walkSpeed = v);
            Restore(a, "runSpeed", v => a.runSpeed = v);
            Restore(a, "shootRange", v => a.shootRange = v);
            Restore(a, "towerShootRange", v => a.towerShootRange = v);
        });
        ForEach<CitizenHousePayable>(c => Restore(c, "_cooldownOfSpawning", v => c._cooldownOfSpawning = v));
        ForEach<BeggarCamp>(c => Restore(c, "spawnInterval", v => c.spawnInterval = v));
        ForEach<Player>(p =>
        {
            Restore(p, "timeBetweenCoins", v => p.timeBetweenCoins = v);
            Restore(p, "timeBetweenCoinsForTouchSupport", v => p.timeBetweenCoinsForTouchSupport = v);
        });
        ForEach<Banker>(b =>
        {
            Restore(b, "dailyInterest", v => b.dailyInterest = v);
            Restore(b, "maxInterest", v => b.maxInterest = (int)Math.Round(v));
            Restore(b, "coinGatherTargetPercentage", v => b.coinGatherTargetPercentage = v);
            var w = b._wallet;
            if (w != null)
                Restore(w, "TotalCapacity", v => w.TotalCapacity = (int)Math.Round(v));
        });
        ForEach<Farmland>(f => Restore(f, "coinYield", v => f.coinYield = (int)Math.Round(v)));
        ForEach<Knight>(k => { Restore(k, "_walkSpeed", v => k._walkSpeed = v); Restore(k, "_runSpeed", v => k._runSpeed = v); Restore(k, "_retreatSpeed", v => k._retreatSpeed = v); });
        ForEach<Pikeman>(p => Restore(p, "_runSpeed", v => p._runSpeed = v));
        ForEach<Steed>(s =>
        {
            Restore(s, "walkSpeed", v => s.walkSpeed = v);
            Restore(s, "runSpeed", v => s.runSpeed = v);
            Restore(s, "walkStaminaRate", v => s.walkStaminaRate = v);
            Restore(s, "runStaminaRate", v => s.runStaminaRate = v);
            Restore(s, "glideStaminaRate", v => s.glideStaminaRate = v);
            Restore(s, "standStaminaRate", v => s.standStaminaRate = v);
            Restore(s, "reserveStamina", v => s.reserveStamina = v);
        });
        ForEach<Wall>(w => Restore(w, "repairRate", v => w.repairRate = (int)v));
    }

    private void Restore(Il2CppObjectBase obj, string field, Action<float> set)
    {
        if (_cache.TryGetValue((obj.Pointer, field), out var entry))
            set(entry.Base);
    }
}
