using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>
/// The kingdom's statues: whether each blessing is locked (gems), asleep (coins) or active, and what it does.
/// No spoilers: a statue only appears once a monarch has been near it on this campaign, or once its blessing has been
/// unlocked (it's no longer gem-locked). The time statue appears once it has been found. Being near is noticed in the
/// background (<see cref="TrackFound"/>), so walking past a statue counts with the menu closed.
/// </summary>
internal sealed class StatuesPage : Page
{
    /// <summary>How close (world units, about half a screen) a monarch must get for a statue to count as found.</summary>
    private const float FoundDistance = 10f;

    private static readonly Statue.Deity[] Deities =
        { Statue.Deity.Archer, Statue.Deity.Worker, Statue.Deity.Knight, Statue.Deity.Farmer, Statue.Deity.Pike };

    private string _lastSummary;

    public StatuesPage(PageContext ctx) : base(ctx) { }

    public override string Title => "STATUES";

    public override string EmptyMessage => GameState.Playing()
        ? "No statues found yet. Explore to find some."
        : "Load into a kingdom to see your statues.";

    public override void Fill(List<RowModel> rows)
    {
        if (!GameState.Playing())
        {
            if (Ctx.Preview)
                AddPreviewRows(rows);
            return;
        }

        // The statues standing on this island, by deity.
        var here = new Dictionary<Statue.Deity, Statue>();
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Statue>(), FindObjectsSortMode.None))
        {
            var statue = obj.TryCast<Statue>();
            if (statue == null || !statue.isActiveAndEnabled)
                continue;
            here[statue.deity] = statue;
        }

        // Counts only, so the log doesn't spoil which statues are where.
        var found = 0;
        foreach (var deity in here.Keys)
            found += UpgradeStore.Flag($"statue_found_{deity}") ? 1 : 0;
        var summary = $"Statues on this island: {here.Count} ({found} found)";
        if (Ctx.Player == 0 && summary != _lastSummary)
        {
            _lastSummary = summary;
            Plugin.Logger.LogInfo(summary);
        }

        foreach (var deity in Deities)
        {
            var status = Status(deity);
            here.TryGetValue(deity, out var statue);
            if (status == Statue.DeityStatus.GemLocked && !UpgradeStore.Flag($"statue_found_{deity}"))
                continue;
            rows.Add(StatueRow(deity, status, statue));
        }

        var time = Coop.Kingdom != null ? Coop.Kingdom.timeStatue : null;
        if (time != null && time.isActiveAndEnabled && UpgradeStore.Flag("statue_found_TimeStatue"))
            rows.Add(TimeRow(time));
    }

    /// <summary>Marks the statues a monarch is near as found on this save (called every second while playing).</summary>
    public static void TrackFound()
    {
        if (!GameState.Playing())
            return;
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Statue>(), FindObjectsSortMode.None))
        {
            var statue = obj.TryCast<Statue>();
            if (statue != null && statue.isActiveAndEnabled && Near(statue.transform.position.x))
                UpgradeStore.SetFlag($"statue_found_{statue.deity}");
        }
        var time = Coop.Kingdom != null ? Coop.Kingdom.timeStatue : null;
        if (time != null && time.isActiveAndEnabled && Near(time.transform.position.x))
            UpgradeStore.SetFlag("statue_found_TimeStatue");
    }

    private RowModel StatueRow(Statue.Deity deity, Statue.DeityStatus status, Statue statue)
    {
        var (right, color) = status switch
        {
            Statue.DeityStatus.Activated => ("ACTIVE", MenuView.Gold),
            Statue.DeityStatus.CoinLocked => ("ASLEEP", MenuView.Cream),
            _ => ("LOCKED", MenuView.Dim),
        };

        // Days left on this island's statue, if it's a timed one.
        var timed = statue != null ? statue.TryCast<TimedStatue>() : null;
        if (status == Statue.DeityStatus.Activated && timed != null && timed._daysLeft > 0)
            right = $"ACTIVE {timed._daysLeft}d";

        var detail = Effect(deity);
        if (statue != null && status == Statue.DeityStatus.GemLocked)
            detail += $"  Unlock: {statue.Price} {Shops.CurrencyName(statue.Currency)}";
        else if (statue != null && status == Statue.DeityStatus.CoinLocked)
            detail += $"  Wake: {statue.coinPrice} coins";
        else if (statue == null && status != Statue.DeityStatus.Activated)
            detail += "  (not on this island)";

        return new RowModel
        {
            Icon = MapIcon(deity, status == Statue.DeityStatus.GemLocked) ?? (statue != null ? StatueSprite(statue) : Ctx.Art.Get("menu_crown")),
            Name = $"{deity} statue",
            Right = right,
            RightColor = color,
            Detail = detail,
        };
    }

    private RowModel TimeRow(TimeStatue time)
    {
        var days = time._daysRemaining;
        var active = time.status == TimeStatue.Status.Activated;
        var renderer = time._spriteRenderer;
        return new RowModel
        {
            Icon = renderer != null ? renderer.sprite : null,
            Name = "Time statue",
            Right = active ? $"{days} days" : time.status == TimeStatue.Status.Destroyed ? "BROKEN" : "DORMANT",
            RightColor = !active ? MenuView.Dim : days <= 2 ? MenuView.Danger : days <= 5 ? MenuView.Gold : MenuView.Cream,
            Detail = active ? "Days left before time runs out. Pay it to add more." : "Not counting down.",
        };
    }

    /// <summary>The game's own map icon for a statue (the greyed "locked" one while it's locked).</summary>
    private Sprite MapIcon(Statue.Deity deity, bool locked)
    {
        var name = deity == Statue.Deity.Pike
            ? (locked ? "map_icon_pikeman_statue_locked_greece" : "map_icon_pikeman_statue_greece")
            : $"map_icon_{deity.ToString().ToLowerInvariant()}statue{(locked ? "_locked" : "")}";
        return Ctx.Art.Get(name);
    }

    /// <summary>What a blessing does, as far as the game's own numbers tell.</summary>
    private static string Effect(Statue.Deity deity)
    {
        switch (deity)
        {
            case Statue.Deity.Farmer:
                var extra = FarmerStatueExtra();
                return extra > 0 ? $"+{extra} farmland per farm" : "More farmland per farm";
            case Statue.Deity.Knight:
                return "Knights strike faster";
            case Statue.Deity.Worker:
                return "Blesses your builders";
            case Statue.Deity.Pike:
                return "Blesses your pikemen";
            default:
                return "Blesses your archers";
        }
    }

    private static int FarmerStatueExtra()
    {
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Farmhouse>(), FindObjectsSortMode.None))
        {
            var farm = obj.TryCast<Farmhouse>();
            if (farm != null && !farm.isStable)
                return farm.farmerStatueExtraFarmlands;
        }
        return 0;
    }

    private static Statue.DeityStatus Status(Statue.Deity deity)
    {
        try
        {
            return CampaignSaveData.GetDeityStatus(deity, null);
        }
        catch (Exception)
        {
            return Statue.DeityStatus.GemLocked;
        }
    }

    private static Sprite StatueSprite(Statue statue)
    {
        var renderer = statue._spriteRenderer != null ? statue._spriteRenderer : statue.GetComponentInChildren<SpriteRenderer>();
        return renderer != null ? renderer.sprite : null;
    }

    private static bool Near(float x)
    {
        for (var i = 0; i < Coop.MaxPlayers; i++)
        {
            var monarch = Coop.Monarch(i);
            if (monarch != null && Math.Abs(monarch.transform.position.x - x) <= FoundDistance)
                return true;
        }
        return false;
    }

    private void AddPreviewRows(List<RowModel> rows)
    {
        rows.Add(new RowModel { Icon = MapIcon(Statue.Deity.Archer, false), Name = "Archer statue", Right = "ACTIVE 3d", RightColor = MenuView.Gold, Detail = "Blesses your archers" });
        rows.Add(new RowModel { Icon = MapIcon(Statue.Deity.Farmer, false), Name = "Farmer statue", Right = "ASLEEP", Detail = "+2 farmland per farm  Wake: 6 coins" });
        rows.Add(new RowModel { Icon = MapIcon(Statue.Deity.Worker, true), Name = "Worker statue", Right = "LOCKED", RightColor = MenuView.Dim, Detail = "Blesses your builders  Unlock: 4 gems" });
    }
}
