using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Each unlocked job with its count and stats read from a live unit (speeds in the game's units per second). Every
/// stat shows its base value, followed by what upgrades, statues and unpaid wages change it by: blue when that's
/// better, red when it's worse ("Run 3 +0.3", "Reload 1.5s -0.2"). Above them, the soldiers' wages and any debt.
/// Jobs the kingdom hasn't reached stay hidden, like everywhere else in the menu.
/// </summary>
internal sealed class UnitsPage : Page
{
    // Rich-text colours for the changes (the Detail line supports <color> tags).
    internal const string Better = "#78B4FF";
    internal const string Worse = "#F26147";

    public UnitsPage(PageContext ctx) : base(ctx) { }

    public override string Title => "UNITS";

    public override string EmptyMessage => "Load into a kingdom to see your units.";

    public override string Hint(int player)
    {
        if (!WagesInfo.Active || Wages.Debt <= 0)
            return null;
        var (submit, cancel, tabs) = Coop.KeyNames(player);
        return $"Hold {submit}: pay debt  {tabs}: tabs  {cancel}: close";
    }

    public override void Fill(List<RowModel> rows)
    {
        if (Coop.Kingdom == null || (!GameState.Playing() && !Ctx.Preview))
            return;

        var wages = WagesRow.Build(Ctx, armyTab: false);
        if (wages != null)
            rows.Add(wages);

        foreach (var kind in Unlocks.Kinds)
        {
            if (!Unlocks.Has(kind))
                continue;
            var alive = Unlocks.Alive(kind);
            var sample = alive.Count > 0 ? alive[0] : null;
            rows.Add(new RowModel
            {
                Icon = IdleSprite(alive) ?? Ctx.Art.Get(kind.Icon),
                Name = kind.Name,
                Right = alive.Count.ToString(),
                RightColor = alive.Count > 0 ? MenuView.Cream : MenuView.Dim,
                Detail = sample != null ? Stats(kind, sample) : "None right now",
            });
        }

        var steed = Coop.Monarch(Ctx.Player)?.steed;
        if (steed != null)
        {
            var stamina = GameState.Playing() ? Upgrades.SteedStamina.Multiplier(UpgradeStore.Level(Upgrades.SteedStamina, Ctx.Player)) : 1f;
            rows.Add(new RowModel
            {
                Icon = Ctx.Art.SteedSprite(Ctx.Player),
                Name = "Your steed",
                Detail = Join(Stat("Walk", Base(steed, "walkSpeed", steed.walkSpeed), steed.walkSpeed),
                    Stat("Run", Base(steed, "runSpeed", steed.runSpeed), steed.runSpeed),
                    stamina != 1f ? Stat("Stamina x", 1f, stamina) : null),
            });
        }
    }

    private static string Stats(UnitKind kind, Component unit)
    {
        var soldierDamage = DamagePatch.SoldierMultiplier;
        if (kind == Unlocks.Villagers)
        {
            var p = unit.TryCast<Peasant>();
            return p == null ? "" : Join(Speed(p, "walkSpeed", "Walk", p.walkSpeed), Speed(p, "runSpeed", "Run", p.runSpeed));
        }
        if (kind == Unlocks.Builders)
        {
            var w = unit.TryCast<Worker>();
            return w == null ? "" : Join(Stat("Work", Base(w, "workTime", w.workTime), w.workTime, lowerIsBetter: true, unit: "s"),
                Speed(w, "walkSpeed", "Walk", w.walkSpeed), Speed(w, "runSpeed", "Run", w.runSpeed));
        }
        if (kind == Unlocks.Archers)
        {
            var a = unit.TryCast<Archer>();
            if (a == null)
                return "";
            var arrow = a._arrowAttack != null && a._arrowAttack._arrowPrefab != null ? a._arrowAttack._arrowPrefab.hitDamage : 0;
            var damage = arrow > 0
                ? Stat("Dmg", arrow, arrow * DamagePatch.ArcherMultiplier)
                : Stat("Dmg x", 1f, DamagePatch.ArcherMultiplier);
            // (No speed: with all three changed the line wouldn't fit. Soldier movement is on the Upgrades page.)
            return Join(Stat("Reload", Base(a, "shootCooldownTime", a.shootCooldownTime), a.shootCooldownTime, lowerIsBetter: true, unit: "s"),
                Stat("Range", Base(a, "shootRange", a.shootRange), a.shootRange), damage);
        }
        if (kind == Unlocks.Farmers)
        {
            var f = unit.TryCast<Farmer>();
            return f == null ? "" : Join(Speed(f, "_walkSpeed", "Walk", f._walkSpeed), Speed(f, "_runSpeed", "Run", f._runSpeed));
        }
        if (kind == Unlocks.Knights)
        {
            var k = unit.TryCast<Knight>();
            return k == null ? "" : Join(Stat("Hit", k._attackDamage, k._attackDamage * soldierDamage) + $" every {N(k._slashCooldown)}s",
                Speed(k, "_runSpeed", "Run", k._runSpeed));
        }
        if (kind == Unlocks.Pikemen)
        {
            var p = unit.TryCast<Pikeman>();
            return p == null ? "" : Join(Stat("Dmg x", 1f, soldierDamage), Speed(p, "_runSpeed", "Run", p._runSpeed));
        }
        if (kind == Unlocks.Berserkers)
        {
            var b = unit.TryCast<Berserker>();
            return b == null ? "" : Join(Stat("Hit", b.attackDamage, b.attackDamage * soldierDamage) + $" every {N(b.attackCooldown)}s",
                Speed(b, "runSpeed", "Run", b.runSpeed));
        }
        if (kind == Unlocks.Ninjas)
        {
            var n = unit.TryCast<Ninja>();
            return n == null ? "" : Join(Stat("Hit", n.attackDamage, n.attackDamage * soldierDamage) + $" every {N(n._strikeCooldown)}s",
                Speed(n, "runSpeed", "Run", n.runSpeed));
        }
        return "";
    }

    private static string Speed(Il2CppObjectBase unit, string field, string label, float current) =>
        Stat(label, Base(unit, field, current), current);

    /// <summary>The field's value before upgrades (StatApplier remembers it), or the current one if it isn't changed.</summary>
    private static float Base(Il2CppObjectBase obj, string field, float current) =>
        StatApplier.Current != null ? StatApplier.Current.BaseOf(obj, field, current) : current;

    /// <summary>"Run 3 +0.3": the base value, then the change from it in blue (better) or red (worse), if any.</summary>
    internal static string Stat(string label, float baseValue, float current, bool lowerIsBetter = false, string unit = "")
    {
        var separator = label.EndsWith("x") ? "" : " ";
        var text = $"{label}{separator}{N(baseValue)}{unit}";
        var change = current - baseValue;
        if (Math.Abs(change) < 0.005f)
            return text;
        var better = lowerIsBetter ? change < 0f : change > 0f;
        return $"{text} {Colour((change > 0f ? "+" : "-") + N(Math.Abs(change)), better ? Better : Worse)}";
    }

    internal static string Colour(string text, string colour) => $"<color={colour}>{text}</color>";

    private static string Join(params string[] parts) => string.Join("  ", Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));

    /// <summary>A live unit's current sprite if it's an idle frame (the most portrait-like pose), so the icon matches the biome.</summary>
    private static Sprite IdleSprite(List<Component> alive)
    {
        foreach (var unit in alive)
        {
            var renderer = unit.GetComponentInChildren<SpriteRenderer>();
            var sprite = renderer != null ? renderer.sprite : null;
            if (sprite != null && sprite.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                return sprite;
        }
        return null;
    }

    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
