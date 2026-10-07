using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>
/// The Coin magnet upgrade: a monarch collects loose coins and gems lying within reach, as if they had walked over them.
/// The game's own pickup (<c>Wallet.TryToGrabCurrency</c>) does the collecting, so its sounds, purse and rules apply.
/// To keep the game's coin mechanics intact, a coin is left alone while it's still flying or moving to a target, while a
/// unit has claimed it (a vagrant walking to it), if this monarch dropped it (coins thrown to vagrants, the banker or a
/// stand, or spilled from a full purse), or if the game says this monarch can't pick it up yet.
///
/// The purse is a little physics bag: once it's packed to the rim, every coin added pushes one over the edge, and half
/// of those fall in the water and are lost. So the magnet takes one coin at a time, stops while the purse is nearly
/// full, and pauses for a while after any coin spills, leaving coins on the ground for later instead.
/// </summary>
internal static class CoinMagnet
{
    private const float Interval = 0.15f;
    /// <summary>How far above or below the monarch's feet a coin may lie (coins rest on the ground).</summary>
    private const float VerticalReach = 2.5f;
    /// <summary>
    /// Height (bag units: bottom -0.56, neck 0.28, rim 0.56) above which a settled coin means the purse is nearly full.
    /// 15 coins pile up to about 0.03, and the bag holds about 25 to its rim. Measured in the game's own bag shape, so it
    /// means the same in a bigger purse (<see cref="PurseSize.ShapeHeight"/>).
    /// </summary>
    private const float NearlyFullHeight = 0.4f;
    /// <summary>Coins still dropping into the purse; more than this and the magnet waits for them to settle.</summary>
    private const int MaxFalling = 2;
    private const float SpillPause = 20f;

    private static float _next;
    private static readonly float[] PausedUntil = new float[Coop.MaxPlayers];

    /// <summary>A coin spilled out of this player's full purse: leave coins on the ground for a while.</summary>
    public static void NoteSpill(Player player)
    {
        for (var i = 0; i < Coop.MaxPlayers; i++)
        {
            var monarch = Coop.Monarch(i);
            if (monarch != null && player != null && monarch.Pointer == player.Pointer)
                PausedUntil[i] = Time.unscaledTime + SpillPause;
        }
    }

    public static void Tick()
    {
        if (Time.unscaledTime < _next || !GameState.Playing() || !NetworkBigBoss.HasWorldAuth)
            return;
        _next = Time.unscaledTime + Interval;

        float[] reach = { Upgrades.MagnetReach(UpgradeStore.Level(Upgrades.CoinMagnet, 0)), Upgrades.MagnetReach(UpgradeStore.Level(Upgrades.CoinMagnet, 1)) };
        if (reach[0] <= 0f && reach[1] <= 0f)
            return;

        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Object> coins = null;
        for (var i = 0; i < Coop.MaxPlayers; i++)
        {
            if (reach[i] <= 0f || Time.unscaledTime < PausedUntil[i])
                continue;
            var monarch = Coop.Monarch(i);
            var wallet = monarch != null ? monarch.wallet : null;
            if (wallet == null || !wallet.CanGrabCoins || PurseNearlyFull(monarch))
                continue;

            coins ??= Object.FindObjectsByType(Il2CppType.Of<DroppableCurrency>(), FindObjectsSortMode.None);
            if (coins == null || coins.Length == 0)
                return;

            // The nearest loose coin in reach, one per tick: they fly in one by one, as if walked over.
            var at = monarch.transform.position;
            var asPicker = new IPickupAttributeProvider(monarch.Pointer);
            DroppableCurrency nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var obj in coins)
            {
                var coin = obj.TryCast<DroppableCurrency>();
                if (coin == null || !coin.isActiveAndEnabled)
                    continue;
                var pos = coin.transform.position;
                var distance = Math.Abs(pos.x - at.x);
                if (distance > reach[i] || distance >= nearestDistance || Math.Abs(pos.y - at.y) > VerticalReach)
                    continue;
                if (!Loose(coin, monarch.gameObject) || !coin.CanBePickedUp(asPicker))
                    continue;
                nearest = coin;
                nearestDistance = distance;
            }
            if (nearest != null)
                wallet.TryToGrabCurrency(nearest.gameObject);
        }
    }

    private static bool Loose(DroppableCurrency coin, GameObject monarch)
    {
        if (coin.pickedUp || coin.IsFake() || !coin._hasHitGround || coin._moving)
            return false;
        if (coin.friendlyClaimer != null)
            return false;
        var dropper = coin.dropper;
        return dropper == null || dropper.Pointer != monarch.Pointer;
    }

    /// <summary>Whether the coins in the purse are piled near its neck (or several are still dropping in).</summary>
    private static bool PurseNearlyFull(Player monarch)
    {
        var bag = monarch.GetCurrencyBag();
        var inBag = bag != null ? bag._currencyObjects : null;
        if (inBag == null)
            return false;

        var falling = 0;
        for (var i = 0; i < inBag.Count; i++)
        {
            var coin = inBag[i];
            if (coin == null || !coin.isActiveAndEnabled)
                continue;
            var body = coin._rigidbody;
            if (body != null && body.linearVelocity.sqrMagnitude > 0.05f)
            {
                if (++falling > MaxFalling)
                    return true;
                continue;
            }
            if (PurseSize.ShapeHeight(bag, coin.transform.localPosition.y) > NearlyFullHeight)
                return true;
        }
        return false;
    }
}

/// <summary>Tells the coin magnet when a coin spills out of a full purse (Player.CoinFellFromBag drops it at the monarch's feet).</summary>
[HarmonyPatch(typeof(Player), nameof(Player.CoinFellFromBag))]
internal static class PurseSpillPatch
{
    private static void Postfix(Player __instance) => CoinMagnet.NoteSpill(__instance);
}
