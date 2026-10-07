using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>One kind of unit the menu can talk about.</summary>
internal sealed class UnitKind
{
    public string Name;
    public Il2CppSystem.Type Type;
    public string Icon;
    /// <summary>Starting jobs, always shown.</summary>
    public bool Basic;
    /// <summary>Knights, pikemen, berserkers, ninjas: what the "Soldier" upgrades act on.</summary>
    public bool Soldier;
    /// <summary>Stands whose appearance means this unit is unlocked (the game only places them once the town center allows it).</summary>
    public PayableShop.ShopType[] Stands = Array.Empty<PayableShop.ShopType>();
}

/// <summary>
/// What this kingdom has reached so far, so the menu never shows (spoils) units or wall tiers the player hasn't unlocked.
/// A unit kind counts once one is alive or its stand has been placed (farmers: once a farm is built), and stays counted
/// until the kingdom changes.
/// </summary>
internal static class Unlocks
{
    public static readonly UnitKind Villagers = new() { Name = "Villagers", Type = Il2CppType.Of<Peasant>(), Icon = "peasant_idle_0", Basic = true };
    public static readonly UnitKind Builders = new() { Name = "Builders", Type = Il2CppType.Of<Worker>(), Icon = "worker_idle_0", Basic = true };
    public static readonly UnitKind Archers = new() { Name = "Archers", Type = Il2CppType.Of<Archer>(), Icon = "archer_idle1_0", Basic = true };
    public static readonly UnitKind Farmers = new() { Name = "Farmers", Type = Il2CppType.Of<Farmer>(), Icon = "farmer_idle_0" };

    public static readonly UnitKind Knights = new()
    {
        Name = "Knights", Type = Il2CppType.Of<Knight>(), Icon = "knight_Idle_0", Soldier = true,
        Stands = new[] { PayableShop.ShopType.ShieldShopLeft, PayableShop.ShopType.ShieldShopRight },
    };

    public static readonly UnitKind Pikemen = new()
    {
        Name = "Pikemen", Type = Il2CppType.Of<Pikeman>(), Icon = "pikeman_idle_0", Soldier = true,
        Stands = new[] { PayableShop.ShopType.PikeLeft, PayableShop.ShopType.PikeRight, PayableShop.ShopType.Pike_OLDHANDLE },
    };

    public static readonly UnitKind Berserkers = new() { Name = "Berserkers", Type = Il2CppType.Of<Berserker>(), Icon = "berserker_idle_norselands_0", Soldier = true };

    public static readonly UnitKind Ninjas = new()
    {
        Name = "Ninjas", Type = Il2CppType.Of<Ninja>(), Icon = "ninja_idle_0", Soldier = true,
        Stands = new[] { PayableShop.ShopType.NinjaLeft, PayableShop.ShopType.NinjaRight },
    };

    public static readonly UnitKind[] Kinds = { Villagers, Builders, Archers, Farmers, Knights, Pikemen, Berserkers, Ninjas };

    public static IEnumerable<string> IconNames => Array.ConvertAll(Kinds, k => k.Icon);

    private static readonly HashSet<UnitKind> Reached = new();
    private static IntPtr _kingdom;
    private static float _nextScan;

    /// <summary>Whether the kingdom has unlocked this kind of unit.</summary>
    public static bool Has(UnitKind kind)
    {
        if (kind.Basic)
            return true;
        Scan();
        return Reached.Contains(kind);
    }

    /// <summary>Whether the kingdom has any soldiers (knights, pikemen, berserkers, ninjas) unlocked yet.</summary>
    public static bool AnySoldier => Array.Exists(Kinds, k => k.Soldier && Has(k));

    /// <summary>Whether the kingdom has its banker (the bank only appears part-way through a campaign).</summary>
    public static bool HasBanker
    {
        get
        {
            foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Banker>(), FindObjectsSortMode.None))
            {
                var banker = obj.TryCast<Banker>();
                if (banker != null && banker.isActiveAndEnabled)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The idle sprite name of the first soldier kind reached, or null if none yet.</summary>
    public static string SoldierIcon() => Array.Find(Kinds, k => k.Soldier && Has(k))?.Icon;

    /// <summary>How the best wall standing in the kingdom looks right now, or null if none is built.</summary>
    public static Sprite BestWallSprite()
    {
        Wall best = null;
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Wall>(), FindObjectsSortMode.None))
        {
            var wall = obj.TryCast<Wall>();
            if (wall != null && wall.isActiveAndEnabled && (best == null || wall.level > best.level))
                best = wall;
        }
        var renderer = best == null ? null : best._spriteRenderer != null ? best._spriteRenderer : best.GetComponentInChildren<SpriteRenderer>();
        return renderer != null ? renderer.sprite : null;
    }

    /// <summary>The active units of one kind (warrior peasants aren't villagers).</summary>
    public static List<Component> Alive(UnitKind kind)
    {
        var list = new List<Component>();
        foreach (var obj in Object.FindObjectsByType(kind.Type, FindObjectsSortMode.None))
        {
            var b = obj.TryCast<Behaviour>();
            if (b == null || !b.isActiveAndEnabled)
                continue;
            if (kind == Villagers && b.TryCast<WarriorPeasant>() != null)
                continue;
            list.Add(b);
        }
        return list;
    }

    private static void Scan()
    {
        var kingdom = Coop.Kingdom;
        var ptr = kingdom != null ? kingdom.Pointer : IntPtr.Zero;
        if (ptr != _kingdom)
        {
            _kingdom = ptr;
            Reached.Clear();
            _nextScan = 0f;
        }
        if (kingdom == null || Time.unscaledTime < _nextScan)
            return;
        _nextScan = Time.unscaledTime + 1f;

        HashSet<PayableShop.ShopType> stands = null;
        foreach (var kind in Kinds)
        {
            if (kind.Basic || Reached.Contains(kind))
                continue;
            stands ??= PlacedStands();
            if (Array.Exists(kind.Stands, stands.Contains) || (kind == Farmers && AnyFarm()) || Alive(kind).Count > 0)
                Reached.Add(kind);
        }
    }

    private static HashSet<PayableShop.ShopType> PlacedStands()
    {
        var stands = new HashSet<PayableShop.ShopType>();
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<PayableSidedShop>(), FindObjectsSortMode.None))
        {
            var shop = obj.TryCast<PayableSidedShop>();
            if (shop != null && shop.isActiveAndEnabled)
                stands.Add(shop.SidedShopType);
        }
        return stands;
    }

    private static bool AnyFarm()
    {
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Farmhouse>(), FindObjectsSortMode.None))
        {
            var farm = obj.TryCast<Farmhouse>();
            if (farm != null && farm.isActiveAndEnabled && !farm.isStable)
                return true;
        }
        return false;
    }
}
