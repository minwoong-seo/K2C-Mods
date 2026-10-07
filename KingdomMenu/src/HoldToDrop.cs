using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Hold to drop coins. In the game, pressing the pay key ("down") with nothing payable in reach drops one coin, and
/// holding it does nothing more, so dropping a handful means one tap per coin. Holding it now keeps dropping coins, one
/// every <see cref="Plugin.HoldToDropInterval"/> seconds after a short delay, until the key is let go.
///
/// Each extra coin is dropped by the game's own code as if the key had been pressed again (<c>Player.UpdatePayState</c>
/// with the key just pressed), so it flies, sounds and can be picked up exactly like a tapped one. Holding the key next
/// to something payable still pays for it as usual, and a hold that started paying never turns into dropping. Coins
/// only repeat while nothing payable is in reach: walking up to a building pauses them, it never pays for it. Only coins
/// repeat; it stops when the purse runs out of them rather than dropping gems.
/// </summary>
internal static class HoldToDrop
{
    /// <summary>How long the key is held before coins start repeating, so a tap still drops exactly one.</summary>
    private const float FirstRepeatDelay = 0.5f;
    /// <summary>A longer gap between the game's pay updates means the monarch lost their controls (a menu, a cutscene).</summary>
    private const float MaxGap = 0.25f;

    private sealed class Hold
    {
        public bool Armed;
        public float Next;
        public float LastSeen;
    }

    private static readonly Dictionary<IntPtr, Hold> Holds = new();
    private static bool _dropping;

    /// <summary>After the game has handled one frame of a monarch's pay key.</summary>
    public static void After(Player player, bool payKey, bool payKeyDown, bool usingTouch)
    {
        if (_dropping || player == null || !Plugin.HoldToDropCoins.Value)
            return;

        var now = Time.time;
        if (!Holds.TryGetValue(player.Pointer, out var hold))
            Holds[player.Pointer] = hold = new Hold();
        var gap = now - hold.LastSeen;
        hold.LastSeen = now;

        if (payKeyDown)
        {
            // The game just handled the press: it either started paying for something, or dropped a coin (or had
            // nothing to drop). Only the second kind of press repeats.
            hold.Armed = player._payState == Player.PayState.None;
            hold.Next = now + FirstRepeatDelay;
            return;
        }
        if (!payKey || gap > MaxGap)
        {
            hold.Armed = false;
            return;
        }
        if (!hold.Armed || now < hold.Next)
            return;
        // Something payable in reach: wait (the game would start paying for it on a press).
        if (player._payState != Player.PayState.None || player.selectedPayable != null)
            return;

        var wallet = player.wallet;
        if (wallet == null || wallet.Coins <= 0 || !wallet.HasDroppableCurrency
            || wallet.FirstAvailableDroppableCurrency() != CurrencyType.Coins)
        {
            hold.Armed = false; // out of coins: never go on to gems
            return;
        }

        hold.Next = now + Mathf.Max(0.05f, Plugin.HoldToDropInterval.Value);
        _dropping = true;
        try
        {
            // A fresh press with the key not held: drops a coin, and can't start a payment that carries on next frame.
            player.UpdatePayState(false, true, usingTouch);
        }
        finally
        {
            _dropping = false;
        }
    }
}

/// <summary>Player.UpdatePayState(payKey, payKeyDown, usingTouch) runs the pay key every frame for each monarch.</summary>
[HarmonyPatch(typeof(Player), nameof(Player.UpdatePayState))]
internal static class HoldToDropPatch
{
    private static void Postfix(Player __instance, bool payKey, bool payKeyDown, bool usingTouch) =>
        HoldToDrop.After(__instance, payKey, payKeyDown, usingTouch);
}
