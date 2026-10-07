using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Each unlocked job with its count and stats read from a live unit, so upgrades and statue blessings are already in
/// the numbers (speeds in the game's units per second). Damage upgrades act per hit, so they're shown as multipliers.
/// Jobs the kingdom hasn't reached stay hidden, like everywhere else in the menu.
/// </summary>
internal sealed class UnitsPage : Page
{
    public UnitsPage(PageContext ctx) : base(ctx) { }

    public override string Title => "UNITS";

    public override string EmptyMessage => "Load into a kingdom to see your units.";

    public override void Fill(List<RowModel> rows)
    {
        if (Coop.Kingdom == null || (!GameState.Playing() && !Ctx.Preview))
            return;

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
            var stamina = Multiplier(Upgrades.SteedStamina, Ctx.Player);
            rows.Add(new RowModel
            {
                Icon = Ctx.Art.SteedSprite(Ctx.Player),
                Name = "Your steed",
                Detail = $"Walk {N(steed.walkSpeed)}  Run {N(steed.runSpeed)}" + (stamina > 1f ? $"  Stamina x{N(stamina)}" : ""),
            });
        }
    }

    private static string Stats(UnitKind kind, Component unit)
    {
        var soldierDamage = Multiplier(Upgrades.SoldierDamage);
        if (kind == Unlocks.Villagers)
        {
            var p = unit.TryCast<Peasant>();
            return p == null ? "" : $"Walk {N(p.walkSpeed)}  Run {N(p.runSpeed)}";
        }
        if (kind == Unlocks.Builders)
        {
            var w = unit.TryCast<Worker>();
            return w == null ? "" : $"Work {N(w.workTime)}s  Walk {N(w.walkSpeed)}  Run {N(w.runSpeed)}";
        }
        if (kind == Unlocks.Archers)
        {
            var a = unit.TryCast<Archer>();
            var damage = Multiplier(Upgrades.ArcherDamage);
            return a == null ? "" : $"Reload {N(a.shootCooldownTime)}s  Run {N(a.runSpeed)}" + (damage > 1f ? $"  Dmg x{N(damage)}" : "");
        }
        if (kind == Unlocks.Farmers)
        {
            var f = unit.TryCast<Farmer>();
            return f == null ? "" : $"Walk {N(f._walkSpeed)}  Run {N(f._runSpeed)}";
        }
        if (kind == Unlocks.Knights)
        {
            var k = unit.TryCast<Knight>();
            return k == null ? "" : $"Hit {N(k._attackDamage * soldierDamage)} every {N(k._slashCooldown)}s  Run {N(k._runSpeed)}";
        }
        if (kind == Unlocks.Pikemen)
        {
            var p = unit.TryCast<Pikeman>();
            return p == null ? "" : $"Run {N(p._runSpeed)}" + (soldierDamage > 1f ? $"  Dmg x{N(soldierDamage)}" : "");
        }
        if (kind == Unlocks.Berserkers)
        {
            var b = unit.TryCast<Berserker>();
            return b == null ? "" : $"Hit {N(b.attackDamage * soldierDamage)} every {N(b.attackCooldown)}s  Run {N(b.runSpeed)}";
        }
        if (kind == Unlocks.Ninjas)
        {
            var n = unit.TryCast<Ninja>();
            return n == null ? "" : $"Hit {N(n.attackDamage * soldierDamage)} every {N(n._strikeCooldown)}s  Run {N(n.runSpeed)}";
        }
        return "";
    }

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

    private static float Multiplier(Upgrade upgrade, int player = 0) =>
        GameState.Playing() ? upgrade.Multiplier(UpgradeStore.Level(upgrade, player)) : 1f;

    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
