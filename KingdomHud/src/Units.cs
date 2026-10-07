using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomHud;

/// <summary>One job shown in the counter.</summary>
internal class UnitKind
{
    public string Name;
    /// <summary>Idle sprite used until a live unit of this kind is seen (then its biome-correct sprite is used).</summary>
    public string FallbackSprite;
    /// <summary>
    /// Starting jobs are always shown. The rest stay hidden until unlocked (so the HUD doesn't spoil them): once you
    /// have one, once its stand has been placed, or (farmers) once a farm is built.
    /// </summary>
    public bool AlwaysShow;
    /// <summary>Stands whose appearance means this job is unlocked (the game only places them once the town center allows it).</summary>
    public PayableShop.ShopType[] Stands = Array.Empty<PayableShop.ShopType>();
    public Func<Kingdom, int> Count;
    /// <summary>Component type used to sample a live unit's sprite for the icon.</summary>
    public Il2CppSystem.Type SampleType;
    /// <summary>Optional capacity to show as "count/max".</summary>
    public Func<Kingdom, int?> Max;
}

internal static class Units
{
    public static readonly List<UnitKind> Kinds = new()
    {
        new UnitKind { Name = "Vagrants", FallbackSprite = "beggar_idle_0", AlwaysShow = true, Count = k => k.Beggars?.Count ?? 0, SampleType = Il2CppType.Of<Beggar>() },
        new UnitKind { Name = "Villagers", FallbackSprite = "peasant_idle_0", AlwaysShow = true, Count = _ => CountVillagers(), SampleType = Il2CppType.Of<Peasant>() },
        new UnitKind { Name = "Builders", FallbackSprite = "worker_idle_0", AlwaysShow = true, Count = k => k.Workers?.Count ?? 0, SampleType = Il2CppType.Of<Worker>() },
        new UnitKind { Name = "Archers", FallbackSprite = "archer_idle1_0", AlwaysShow = true, Count = k => k.Archers?.Count ?? 0, SampleType = Il2CppType.Of<Archer>() },
        new UnitKind { Name = "Farmers", FallbackSprite = "farmer_idle_0", Count = k => k.Farmers?.Count ?? 0, SampleType = Il2CppType.Of<Farmer>(),
            Max = _ => { var slots = KingdomInfo.FarmSlots(); return slots > 0 ? slots : null; } },
        new UnitKind { Name = "Knights", FallbackSprite = "knight_Idle_0", Count = k => k.Knights?.Count ?? 0, SampleType = Il2CppType.Of<Knight>(),
            Stands = new[] { PayableShop.ShopType.ShieldShopLeft, PayableShop.ShopType.ShieldShopRight } },
        new UnitKind { Name = "Pikemen", FallbackSprite = "pikeman_idle_0", Count = k => k.Pikemen?.Count ?? 0, SampleType = Il2CppType.Of<Pikeman>(),
            Stands = new[] { PayableShop.ShopType.PikeLeft, PayableShop.ShopType.PikeRight, PayableShop.ShopType.Pike_OLDHANDLE } },
        new UnitKind { Name = "Berserkers", FallbackSprite = "berserker_idle_norselands_0", Count = k => k.Berserkers?.Count ?? 0, SampleType = Il2CppType.Of<Berserker>() },
        new UnitKind { Name = "Ninjas", FallbackSprite = "ninja_idle_0", Count = _ => CountActive<Ninja>(), SampleType = Il2CppType.Of<Ninja>(),
            Stands = new[] { PayableShop.ShopType.NinjaLeft, PayableShop.ShopType.NinjaRight } },
        new UnitKind { Name = "Warriors", FallbackSprite = "peasant_idle_0", Count = _ => CountActive<WarriorPeasant>(), SampleType = Il2CppType.Of<WarriorPeasant>() },
        new UnitKind { Name = "Stable keepers", FallbackSprite = "stable_keeper_idle_norselands_0", Count = k => k._stableKeepers?.Count ?? 0, SampleType = Il2CppType.Of<StableKeeper>() },
    };

    /// <summary>The stands currently placed in the kingdom (only active ones: stands not unlocked yet don't exist).</summary>
    public static HashSet<PayableShop.ShopType> PlacedStands()
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

    /// <summary>Idle peasants waiting for a tool (warrior peasants are counted separately).</summary>
    private static int CountVillagers()
    {
        var count = 0;
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Peasant>(), FindObjectsSortMode.None))
        {
            var p = obj.TryCast<Peasant>();
            if (p != null && p.isActiveAndEnabled && p.TryCast<WarriorPeasant>() == null)
                count++;
        }
        return count;
    }

    private static int CountActive<T>() where T : Behaviour
    {
        var count = 0;
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<T>(), FindObjectsSortMode.None))
        {
            var b = obj.TryCast<T>();
            if (b != null && b.isActiveAndEnabled)
                count++;
        }
        return count;
    }

    /// <summary>The sprite a live unit of this kind is showing, if it's an idle frame (the most portrait-like pose).</summary>
    public static Sprite SampleIdleSprite(UnitKind kind)
    {
        foreach (var obj in Object.FindObjectsByType(kind.SampleType, FindObjectsSortMode.None))
        {
            var component = obj.TryCast<Component>();
            if (component == null)
                continue;
            var renderer = component.GetComponentInChildren<SpriteRenderer>();
            var sprite = renderer != null ? renderer.sprite : null;
            if (sprite != null && sprite.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                return sprite;
        }
        return null;
    }
}
