using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace KingdomHud;

/// <summary>Friendly names for the things a monarch can pay for or interact with.</summary>
internal static class Names
{
    private static readonly Dictionary<string, string> ByClass = new()
    {
        ["PayableGemChest"] = "Gem chest",
        ["PayableBoat"] = "Boat",
        ["Wharf"] = "Wharf",
        ["PayableTeleporter"] = "Teleporter",
        ["PayableTree"] = "Tree",
        ["PayableBush"] = "Bush",
        ["PayableBorder"] = "Border",
        ["Merchant"] = "Merchant",
        ["Cabin"] = "Cabin",
        ["BoatSummoningBell"] = "Boat bell",
        ["BoatSailPosition"] = "Set sail",
        ["Bomb"] = "Bomb",
        ["PayableBombPurchase"] = "Bomb",
        ["PayableBombLeft"] = "Bomb",
        ["PayableBombRight"] = "Bomb",
        ["PayableHorn"] = "War horn",
        ["PayableForge"] = "Forge",
        ["PayableShield"] = "Shield",
        ["PayableShieldWallActivator"] = "Shield wall",
        ["PayableWorkshop"] = "Workshop",
        ["PayableWorkshopBarrel"] = "Workshop barrel",
        ["CitizenHousePayable"] = "Citizen house",
        ["Signpost"] = "Signpost",
        ["Snowman"] = "Snowman",
        ["TimeStatue"] = "Time statue",
        ["TimedStatue"] = "Statue",
        ["Statue"] = "Statue",
        ["UnlockNewRulerStatue"] = "Ruler statue",
        ["Persephone"] = "Persephone",
        ["PayableGemGuard"] = "Gem guard",
        ["ChangeRulerShop"] = "Change ruler",
        ["ChangeItemOfPowerShop"] = "Change item of power",
        ["PayablePlayer"] = "Monarch",
    };

    private static readonly Dictionary<string, string> Words = new()
    {
        ["castle"] = "town center",
        ["keep"] = "keep",
        ["lvl"] = "",
        ["level"] = "",
        ["unbuild"] = "",
        ["unbuilt"] = "",
        ["build"] = "",
        ["clone"] = "",
    };

    private static readonly Regex BiomeSuffix = new(@"(_|\b)(greece|norselands|deadlands|bamboo|snow|olympus|shogun|party|anniversary)\b", RegexOptions.IgnoreCase);

    public static string For(Payable payable)
    {
        if (payable == null)
            return null;

        var shop = payable.TryCast<PayableShop>();
        if (shop != null && shop.TryCast<ChangeRulerShop>() == null && shop.TryCast<ChangeItemOfPowerShop>() == null)
            return ShopName(shop);

        var steed = payable.TryCast<Steed>();
        if (steed != null)
            return $"{SteedName(steed.steedType)} (mount)";

        var hermit = payable.TryCast<Hermit>();
        if (hermit != null)
            return $"Hermit of the {Humanize(hermit._hermitType.ToString()).ToLowerInvariant()}";

        var castle = payable.GetComponent<Castle>();
        if (castle != null)
            return $"Town center ({(int)castle.level + 1})";

        var className = payable.GetIl2CppType().Name;
        if (ByClass.TryGetValue(className, out var known))
            return known;

        // Buildings (walls, towers, farms...) are PayableUpgrade/PayableComponent on a named prefab.
        var name = Humanize(payable.gameObject.name);
        if (string.IsNullOrEmpty(name))
            name = Humanize(className.Replace("Payable", ""));

        var upgrade = payable.TryCast<PayableUpgrade>();
        if (upgrade != null && upgrade.nextPrefab != null)
            name += " (upgrade)";
        return name;
    }

    private static string ShopName(PayableShop shop)
    {
        var sided = shop.TryCast<PayableSidedShop>();
        var type = sided != null ? sided.SidedShopType : (PayableShop.ShopType?)null;
        if (type == null)
        {
            var item = shop.itemPrefab != null ? Humanize(shop.itemPrefab.name) : null;
            return string.IsNullOrEmpty(item) ? "Shop" : $"{item} shop";
        }

        return type.Value switch
        {
            PayableShop.ShopType.Bow => "Bow shop",
            PayableShop.ShopType.Hammer => "Hammer shop",
            PayableShop.ShopType.Scythe => "Scythe shop",
            PayableShop.ShopType.PikeLeft or PayableShop.ShopType.PikeRight or PayableShop.ShopType.Pike_OLDHANDLE => "Pike shop",
            PayableShop.ShopType.ShieldShopLeft or PayableShop.ShopType.ShieldShopRight => "Shield shop",
            PayableShop.ShopType.WorkshopLeft or PayableShop.ShopType.WorkshopRight => "Workshop",
            PayableShop.ShopType.NinjaLeft or PayableShop.ShopType.NinjaRight => "Ninja shop",
            _ => Humanize(type.Value.ToString()),
        };
    }

    public static string SteedName(SteedType type) => type switch
    {
        SteedType.P1Griffin or SteedType.P2Griffin => "Griffin",
        SteedType.P1Warhorse or SteedType.P2Warhorse => "Warhorse",
        SteedType.P1Default or SteedType.P2Default => "Horse",
        SteedType.HorseStamina => "Stamina horse",
        SteedType.HorseBurst => "Burst horse",
        SteedType.HorseFast => "Fast horse",
        SteedType.P1Wolf or SteedType.P2Wolf => "Wolf",
        SteedType.P2Stag => "Stag",
        SteedType.P2Kelpie => "Kelpie",
        SteedType.Reindeer_Norselands or SteedType.P2Reindeer_Norselands => "Reindeer",
        SteedType.Spookyhorse => "Spooky horse",
        SteedType.Bloodstained => "Bloodstained horse",
        SteedType.CatCart => "Cat cart",
        SteedType.DayNight => "Day and night horse",
        SteedType.TheChariotDay or SteedType.TheChariotNight => "Chariot",
        SteedType.MolossianHound => "Molossian hound",
        SteedType.RainbowPony => "Rainbow pony",
        _ => Humanize(type.ToString()),
    };

    /// <summary>"wall_lvl2_greece(Clone)" -> "Wall", "CitizenHouse" -> "Citizen house".</summary>
    public static string Humanize(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var s = raw.Replace("(Clone)", "");
        s = BiomeSuffix.Replace(s, "");
        s = Regex.Replace(s, @"([a-z])([A-Z])", "$1 $2");
        s = Regex.Replace(s, @"[_\-]+", " ");
        s = Regex.Replace(s, @"\d+", " ");

        var words = new List<string>();
        foreach (var w in s.Split(' '))
        {
            if (w.Length == 0)
                continue;
            var lower = w.ToLowerInvariant();
            if (Words.TryGetValue(lower, out var mapped))
                lower = mapped;
            if (lower.Length > 0)
                words.Add(lower);
        }
        if (words.Count == 0)
            return "";

        var sb = new StringBuilder(string.Join(" ", words));
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }
}
