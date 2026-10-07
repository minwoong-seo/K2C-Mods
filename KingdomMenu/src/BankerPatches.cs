using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Banker keeps collecting. In the game the banker picks up coins dropped near him until his own purse holds
/// TotalCapacity * coinGatherTargetPercentage of them (10), then stops picking up (Banker.ShouldGrabCoin) and walks to
/// the bank to drop them off (ShouldDropOff); with fewer he just keeps them. StatApplier lifts that limit (his purse
/// capacity and the gather target), and this decides when he goes instead: when he's idle with coins, hasn't been given
/// one for <see cref="WagesMath.DepositDelay"/> seconds, and there are none left near him to pick up. His state machine checks
/// ShouldDropOff before ShouldGrabCoin while idle (Idle -> DropOff, then Idle -> GrabCoin), hence the GrabCoin check here.
/// </summary>
[HarmonyPatch(typeof(Banker), nameof(Banker.ShouldDropOff))]
internal static class BankerDropOffPatch
{
    private static readonly Dictionary<IntPtr, (int Coins, float Since)> Seen = new();
    private static bool _loggedError;

    private static bool Prefix(Banker __instance, ref bool __result)
    {
        if (!Plugin.BankerKeepsCollecting.Value)
            return true;

        try
        {
            var wallet = __instance._wallet;
            if (wallet == null)
                return true;

            // When his purse last changed: a coin picked up restarts the wait.
            var coins = wallet.Coins;
            var now = Time.time;
            if (!Seen.TryGetValue(__instance.Pointer, out var seen) || seen.Coins != coins)
                Seen[__instance.Pointer] = seen = (coins, now);

            var fsm = __instance._fsm;
            var idle = fsm != null && fsm.Current == Banker.State.Idle;
            __result = WagesMath.ShouldDeposit(coins, idle, now - seen.Since, __instance.ShouldGrabCoin);
            return false;
        }
        catch (Exception e)
        {
            if (!_loggedError)
            {
                _loggedError = true;
                Plugin.Logger.LogError($"Banker drop-off change failed, he banks at the game's own limit instead: {e}");
            }
            // The game's own rule, with its own limit: StatApplier has raised his purse, so its check would never fire.
            try
            {
                var wallet = __instance._wallet;
                var applier = StatApplier.Current;
                var limit = applier != null
                    ? applier.BaseOf(wallet, "TotalCapacity", wallet.TotalCapacity) * applier.BaseOf(__instance, "coinGatherTargetPercentage", __instance.coinGatherTargetPercentage)
                    : wallet.TotalCapacity * __instance.coinGatherTargetPercentage;
                __result = wallet.Coins > 0 && wallet.Coins >= limit;
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}

/// <summary>
/// The game saves only the banker's bank (BankerData.stashedCoins), not coins he's carrying, and nightfall sends him into
/// hiding wherever he is, even on his way to the bank. Carrying 10 at most that was a small loss; now that he takes
/// everything he's given, whatever is still in his purse once he's hidden goes straight into the bank.
/// </summary>
internal static class BankerSafekeeping
{
    /// <summary>Every second, from MenuHost.</summary>
    public static void Tick()
    {
        if (!Plugin.BankerKeepsCollecting.Value || !GameState.Playing() || !NetworkBigBoss.HasWorldAuth)
            return;
        var banker = Wages.FindBanker();
        var wallet = banker != null ? banker._wallet : null;
        var fsm = banker != null ? banker._fsm : null;
        if (wallet == null || fsm == null || fsm.Current != Banker.State.Hide)
            return;
        var coins = wallet.Coins;
        if (coins <= 0)
            return;
        wallet.RemoveCurrency(CurrencyType.Coins, coins);
        banker._stashedCoins += coins;
        Plugin.Logger.LogInfo($"Banker hid for the night carrying {coins} coins; they went into the bank ({banker._stashedCoins}).");
    }
}

/// <summary>
/// The game saves the banker as just his bank (BankerData.stashedCoins). Whatever he's carrying when it saves (given to
/// him and not yet banked) is added to the saved bank, so a save mid-walk loses nothing. Only the saved copy changes:
/// in the running game he still carries them to the bank, where they arrive once.
/// </summary>
[HarmonyPatch(typeof(Banker), nameof(Banker.Persistent_IBehaviour_RetrieveData))]
internal static class BankerSavePatch
{
    private static void Postfix(Banker __instance, Il2CppSystem.Object __result)
    {
        try
        {
            if (!Plugin.BankerKeepsCollecting.Value)
                return;
            var data = __result?.TryCast<BankerData>();
            var carrying = __instance._wallet != null ? __instance._wallet.Coins : 0;
            if (data != null && carrying > 0)
                data.stashedCoins += carrying;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Saving the banker's coins failed: {e}");
        }
    }
}

/// <summary>Dawn: the banker has just added the day's interest; now the soldiers are paid (see <see cref="Wages"/>).</summary>
[HarmonyPatch(typeof(Banker), nameof(Banker.HandleOnDayStart))]
internal static class BankerDawnPatch
{
    private static void Postfix(Banker __instance)
    {
        try
        {
            Wages.Payday(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Paying the soldiers' wages failed: {e}");
        }
    }
}
