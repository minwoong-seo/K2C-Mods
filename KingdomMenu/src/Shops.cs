using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomMenu;

internal class ShopGroup
{
    public string Label;
    public PayableShop.ShopType? Type;
    public readonly List<PayableShop> Shops = new();
}

/// <summary>
/// Finds the kingdom's tool/weapon stands and buys from them through the game's own payment path
/// (Payable.TransactionComplete -> PerformPay -> PayableShop.Pay), so the item appears on the stand
/// exactly as if coins had been dropped there.
/// </summary>
internal static class Shops
{
    private static Dictionary<string, PayableShop.ShopType> _tagToShopType;

    public static Kingdom GetKingdom()
    {
        if (Managers.InstExists && Managers.Inst != null && Managers.Inst.kingdom != null)
            return Managers.Inst.kingdom;
        return Object.FindObjectOfType<Kingdom>();
    }

    /// <summary>
    /// Whether the stand is actually open for business. Stands don't exist until the game unlocks them
    /// (ShopPlanner spawns them as the town center is upgraded; farmhouses carry their own scythe stand),
    /// but a freshly placed stand is still a construction site until builders finish it, and a stand being
    /// removed is disabled while it sinks away. Neither should be offered.
    /// </summary>
    public static bool IsOpen(PayableShop shop, out string reason)
    {
        reason = null;
        if (shop == null || !shop.isActiveAndEnabled)
            reason = "disabled";
        else if (shop._workableBuilding != null && shop._workableBuilding.UnderConstruction)
            reason = "under construction";
        return reason == null;
    }

    /// <param name="summary">One line describing every stand found, including hidden ones (for the log).</param>
    /// <summary>True while actually playing a kingdom (not on the title screen or in a loading transition).</summary>
    public static bool InKingdom()
    {
        var game = Managers.InstExists && Managers.Inst != null ? Managers.Inst.game : null;
        return game != null && game.InPlayableState;
    }

    public static List<ShopGroup> Find(out string summary)
    {
        var groups = new List<ShopGroup>();
        var seen = new List<string>();
        summary = "";
        // Only active objects are returned, so stands the game hasn't spawned (or has pooled away) never show up.
        var found = Object.FindObjectsByType(Il2CppType.Of<PayableShop>(), FindObjectsSortMode.None);
        if (found == null)
            return groups;

        var byLabel = new Dictionary<string, ShopGroup>();
        foreach (var obj in found)
        {
            var shop = obj?.TryCast<PayableShop>();
            if (shop == null || shop.TryCast<ChangeRulerShop>() != null || shop.TryCast<ChangeItemOfPowerShop>() != null)
                continue;

            var type = GetShopType(shop);
            if (type is PayableShop.ShopType.ChangeRuler or PayableShop.ShopType.ChangeItem)
                continue;

            var label = type.HasValue
                ? PrettyShopType(type.Value)
                : CleanName(shop.itemPrefab != null ? shop.itemPrefab.name : null) ?? CleanName(shop.gameObject.name) ?? "Shop";

            var open = IsOpen(shop, out var reason);
            seen.Add(open ? label : $"{label} ({reason})");
            if (!open)
                continue;

            if (!byLabel.TryGetValue(label, out var group))
            {
                group = new ShopGroup { Label = label, Type = type };
                byLabel[label] = group;
            }
            group.Shops.Add(shop);
        }

        groups.AddRange(byLabel.Values.OrderBy(g => g.Type.HasValue ? (int)g.Type.Value : int.MaxValue).ThenBy(g => g.Label));
        seen.Sort(StringComparer.Ordinal);
        summary = string.Join(", ", seen);
        return groups;
    }

    private static PayableShop.ShopType? GetShopType(PayableShop shop)
    {
        var sided = shop.TryCast<PayableSidedShop>();
        if (sided != null)
            return sided.SidedShopType;

        if (_tagToShopType == null)
        {
            _tagToShopType = new Dictionary<string, PayableShop.ShopType>();
            foreach (PayableShop.ShopType t in Enum.GetValues(typeof(PayableShop.ShopType)))
            {
                if (t == PayableShop.ShopType.Total)
                    continue;
                try
                {
                    var tag = PayableShop.GetShopTag(t);
                    if (!string.IsNullOrEmpty(tag) && !_tagToShopType.ContainsKey(tag))
                        _tagToShopType[tag] = t;
                }
                catch (Exception)
                {
                    // Some shop types have no tag; ignore them.
                }
            }
        }

        return _tagToShopType.TryGetValue(shop.gameObject.tag, out var type) ? type : null;
    }

    private static string PrettyShopType(PayableShop.ShopType type) => type switch
    {
        PayableShop.ShopType.Bow => "Bow",
        PayableShop.ShopType.Hammer => "Hammer",
        PayableShop.ShopType.Scythe => "Scythe",
        PayableShop.ShopType.Pike_OLDHANDLE => "Pike",
        PayableShop.ShopType.PikeLeft => "Pike (left)",
        PayableShop.ShopType.PikeRight => "Pike (right)",
        PayableShop.ShopType.ShieldShopLeft => "Shield (left)",
        PayableShop.ShopType.ShieldShopRight => "Shield (right)",
        PayableShop.ShopType.WorkshopLeft => "Workshop (left)",
        PayableShop.ShopType.WorkshopRight => "Workshop (right)",
        PayableShop.ShopType.NinjaLeft => "Ninja (left)",
        PayableShop.ShopType.NinjaRight => "Ninja (right)",
        _ => type.ToString(),
    };

    private static string CleanName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return name.Replace("(Clone)", "").Trim();
    }

    public static int Capacity(PayableShop shop)
    {
        var limit = Math.Min(shop._limitedNumItems, shop.maxItems);
        return limit > 0 ? limit : shop.maxItems;
    }

    public static Sprite ItemSprite(ShopGroup group)
    {
        foreach (var shop in group.Shops)
        {
            if (shop == null || shop.itemPrefab == null)
                continue;
            var renderer = shop.itemPrefab.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer != null && renderer.sprite != null)
                return renderer.sprite;
        }
        return null;
    }

    public static string CurrencyName(CurrencyType currency) => currency switch
    {
        CurrencyType.Coins => "coins",
        CurrencyType.Gems => "gems",
        _ => currency.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// The Courier fee hardship: extra coins per item bought through the menu (none when purchases are free, or for
    /// things priced in gems).
    /// </summary>
    public static int CourierFee(CurrencyType currency) =>
        !Plugin.FreePurchases.Value && currency == CurrencyType.Coins ? Math.Clamp(Plugin.CourierFee.Value, 0, 5) : 0;

    /// <returns>null on success, otherwise a message explaining why nothing was bought.</returns>
    public static string TryBuyOne(ShopGroup group, Player buyer)
    {
        var buyerX = buyer.transform.position.x;
        var shop = group.Shops
            .Where(s => s != null && s.isActiveAndEnabled && s.CanPay(buyer))
            .OrderBy(s => Math.Abs(s.transform.position.x - buyerX))
            .FirstOrDefault();

        if (shop == null)
        {
            if (!buyer.hasCrown)
                return "You need your crown to buy.";
            return $"No {group.Label} stand has room right now.";
        }

        // Only the machine that owns the world spawns the item immediately; a remote client would pay
        // before the host confirms, so keep this host/single-player only.
        if (!NetworkBigBoss.HasWorldAuth)
            return "Only the host can buy remotely.";

        var stockBefore = shop.GetItemCount();
        var wallet = buyer.wallet;
        var currency = shop.Currency;
        var free = Plugin.FreePurchases.Value;
        var price = shop.Price + CourierFee(currency);

        if (!free)
        {
            if (wallet == null)
                return "Couldn't find your purse.";
            var have = wallet.GetCurrency(currency);
            if (have < price)
                return $"Not enough {CurrencyName(currency)}: need {price}, have {have}.";
            wallet.RemoveCurrency(currency, price);
        }

        var previous = shop.interactingPlayer;
        try
        {
            shop.interactingPlayer = buyer;
            shop.TransactionComplete();
        }
        catch (Exception e)
        {
            if (!free)
                wallet.AddCurrency(currency, price); // refund
            Plugin.Logger.LogError($"Purchase from {group.Label} failed: {e}");
            return "Purchase failed (see BepInEx log).";
        }
        finally
        {
            try { shop.interactingPlayer = previous; }
            catch (Exception) { /* shop may have been destroyed by the purchase */ }
        }

        if (shop.GetItemCount() <= stockBefore)
        {
            if (!free)
                wallet.AddCurrency(currency, price); // refund: the stand didn't actually produce an item
            return $"The {group.Label} stand didn't accept the purchase.";
        }

        return null;
    }
}
