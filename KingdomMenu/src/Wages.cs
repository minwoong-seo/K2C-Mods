using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>
/// The wages numbers, for other mods: Kingdom HUD reads these by reflection, so neither mod needs the other installed.
/// Updated every second while a kingdom is being played.
/// </summary>
public static class WagesInfo
{
    /// <summary>Wages are being paid (or owed) here: they're on, this island has a bank, and the army is upgraded.</summary>
    public static bool Active;
    public static int Soldiers;
    public static int PerDay;
    public static int Debt;
    /// <summary>What unpaid wages leave of the soldiers' fighting strength (1 = full strength).</summary>
    public static float Strength = 1f;
    /// <summary>Days until this island's first wages (its bank's grace period); 0 once they're being paid.</summary>
    public static int StartsIn;
    /// <summary>The last payday: coins taken from the bank, and Time.time when it happened (-1 = none yet this session).</summary>
    public static int LastPaid;
    public static float LastPaydayTime = -1f;
}

/// <summary>
/// The Soldier wages hardship. Army upgrades are what turn soldiers into paid ones: they can only be bought, and only
/// work, on an island with a bank, and once the army has any upgrade the soldiers (archers, knights, pikemen...) are
/// paid from that bank every dawn, right after the banker adds the day's interest: 1 coin for every SoldiersPerCoin
/// soldiers, rounded up, however many upgrades. Without army upgrades there are no wages. What the bank
/// can't cover is added to a debt kept with the save (next to the upgrade levels), and coins that reach the bank later
/// pay the debt off first (the menu's PAY button pays it from the purse). While there's debt, soldiers hit softer:
/// their damage is multiplied by 1 - debt * DebtPenaltyPerCoin, but never by less than 1 - MaxDebtPenalty.
///
/// When wages first apply on an island (its bank is built with the army already upgraded, or the first army upgrade is
/// bought there, or wages are switched on) there's a grace period: the first wages are due on the WageGraceDays-th dawn
/// (3 by default), so spending your last coins on it doesn't put you straight into debt.
///
/// Like the game's banks, debt belongs to an island: the save keeps each island's kingdom, banker and stash apart
/// (campaigns[]._islands[].objects[]), so each island has its own debt, and an island without a bank pays no wages and
/// owes nothing. A new reign starts with none: the fallen monarch's debts aren't the heir's.
/// </summary>
internal static class Wages
{
    public static bool Enabled => Plugin.SoldierWages.Value;

    private static (int Reign, int Land) _loggedWhere = (-2, -2);
    private static bool? _loggedArmyOff;

    /// <summary>The army has at least one upgrade level (army upgrades are kingdom-wide, kept per save).</summary>
    public static bool ArmyUpgraded => GameState.Playing() && Upgrades.In(Category.Army).Exists(u => UpgradeStore.Level(u, 0) > 0);

    /// <summary>
    /// This island has (had) a bank in this reign. Remembered once the banker has been seen, so it can't flicker while
    /// he's out of sight, and kept per reign and island like the bank itself.
    /// </summary>
    public static bool BankHere
    {
        get
        {
            var key = BankKey();
            return key != null && UpgradeStore.Value(key) > 0;
        }
    }

    /// <summary>With wages on, army upgrades only work (and can only be bought) on an island with a bank.</summary>
    public static bool ArmyNeedsBank => Enabled && GameState.Playing() && !BankHere;

    internal static string BankKey()
    {
        var (reign, land) = Where();
        return reign < 0 ? null : $"wage_bank_r{reign}_l{land}";
    }

    /// <summary>Per save: a bank has been seen at some point, so the menu may mention banks (no spoilers before that).</summary>
    public const string BankSeenFlag = "bank_seen";

    public static int Debt
    {
        get
        {
            var key = DebtKey();
            return key != null ? Math.Max(0, UpgradeStore.Value(key)) : 0;
        }
    }

    /// <summary>"wage_debt_r2_l0": the debt of this reign on this island; null outside a kingdom.</summary>
    internal static string DebtKey()
    {
        var (reign, land) = Where();
        return reign < 0 ? null : $"wage_debt_r{reign}_l{land}";
    }

    /// <summary>This island's grace period: the days until its first wages, plus 1 (0 = not started; 1 = over).</summary>
    internal static string GraceKey()
    {
        var (reign, land) = Where();
        return reign < 0 ? null : $"wage_grace_r{reign}_l{land}";
    }

    /// <summary>Days until this island's first wages; 0 once they're being paid (or before its bank is found).</summary>
    public static int StartsIn
    {
        get
        {
            var key = GraceKey();
            return key != null ? Math.Max(0, UpgradeStore.Value(key) - 1) : 0;
        }
    }

    private static int GraceDays => Math.Clamp(Plugin.WageGraceDays.Value, 1, 10);

    /// <summary>If wages started here now (the first army upgrade): days until the first ones are due (1 = next dawn).</summary>
    public static int FirstWagesIn()
    {
        var key = GraceKey();
        var stored = key != null ? UpgradeStore.Value(key) : 0;
        return stored == 0 ? GraceDays : Math.Max(1, stored - 1);
    }

    /// <summary>"first paid in 3 days" / "first paid tomorrow".</summary>
    public static string FirstPaid(int days) => days > 1 ? $"first paid in {days} days" : "first paid tomorrow";

    /// <summary>What wages would cost a day: "4 coins a day", or the rate while there are no soldiers.</summary>
    public static string DailyCost(int soldiers)
    {
        var perDay = PerDay(soldiers);
        return perDay > 0
            ? $"{perDay} {(perDay == 1 ? "coin" : "coins")} a day"
            : $"1 coin a day per {Math.Max(1, Plugin.SoldiersPerCoin.Value)} soldiers";
    }

    /// <summary>The reign day wages were last paid on this island, plus 1 (0 = never).</summary>
    internal static string PaidDayKey(int reign, int land) => $"wage_paid_day_r{reign}_l{land}";

    /// <summary>The campaign's reign and the island being played, or (-1, -1) outside a kingdom.</summary>
    internal static (int Reign, int Land) Where()
    {
        if (!GameState.Playing())
            return (-1, -1);
        try
        {
            var game = GameState.Game();
            var campaign = GlobalSaveData.loaded != null ? GlobalSaveData.loaded.GetCurrentCampaign() : null;
            return game != null && campaign != null ? (Math.Max(0, campaign.reign), game.currentLand) : (-1, -1);
        }
        catch (Exception)
        {
            return (-1, -1);
        }
    }

    /// <summary>How much the debt weakens soldiers right now (0 = not at all, 0.5 = half strength).</summary>
    public static float Penalty =>
        Enabled ? WagesMath.Penalty(Debt, Plugin.DebtPenaltyPerCoin.Value, Plugin.MaxDebtPenalty.Value) : 0f;

    public static float Strength => 1f - Penalty;

    /// <summary>The day's wages for this many soldiers: 1 coin per SoldiersPerCoin, rounded up.</summary>
    public static int PerDay(int soldiers) => WagesMath.PerDay(soldiers, Plugin.SoldiersPerCoin.Value);

    /// <summary>Every second: count the soldiers, pay off debt with whatever has reached the bank, publish the numbers.</summary>
    public static void Tick()
    {
        var playing = GameState.Playing();
        var banker = playing ? FindBanker() : null;
        var soldiers = playing ? CountSoldiers() : 0;

        var where = Where();
        if (playing && where != _loggedWhere)
        {
            _loggedWhere = where;
            WagesInfo.LastPaid = 0;
            WagesInfo.LastPaydayTime = -1f;
            Plugin.Logger.LogInfo(where.Reign < 0
                ? "Wages: couldn't tell which reign and island this is, so no wages are kept."
                : $"Wages: reign {where.Reign}, island {where.Land}: debt {Debt}, {(banker != null ? "bank " + banker._stashedCoins : "no bank")}.");
        }

        var bankKey = BankKey();
        if (banker != null && bankKey != null && UpgradeStore.Value(bankKey) == 0)
        {
            UpgradeStore.SetValue(bankKey, 1);
            UpgradeStore.SetFlag(BankSeenFlag);
            Plugin.Logger.LogInfo($"Wages: reign {where.Reign}, island {where.Land} has a bank.");
        }

        var armyOff = playing && ArmyNeedsBank;
        if (playing && armyOff != _loggedArmyOff)
        {
            _loggedArmyOff = armyOff;
            Plugin.Logger.LogInfo(armyOff ? "Wages: army upgrades are off, this island has no bank yet." : "Wages: army upgrades are on.");
        }

        var upgraded = ArmyUpgraded;
        if (Enabled && banker != null && NetworkBigBoss.HasWorldAuth)
        {
            // Wages applying here for the first time (bank and army upgrades, wages on) start the grace period.
            var graceKey = GraceKey();
            if (upgraded && graceKey != null && UpgradeStore.Value(graceKey) == 0)
            {
                UpgradeStore.SetValue(graceKey, GraceDays + 1);
                Plugin.Logger.LogInfo($"Wages: they apply on this island now, {FirstPaid(GraceDays)}.");
            }

            var key = DebtKey();
            var stash = banker._stashedCoins;
            var (left, owed, paid) = WagesMath.Settle(stash, Debt);
            if (paid > 0 && key != null)
            {
                banker._stashedCoins = left;
                UpgradeStore.SetValue(key, owed);
                Plugin.Logger.LogInfo($"Wages: paid {paid} of the soldiers' debt from the bank ({stash} -> {left}); debt now {owed}.");
            }
        }

        WagesInfo.Debt = Debt;
        WagesInfo.Active = Enabled && BankHere && (upgraded || WagesInfo.Debt > 0);
        WagesInfo.Soldiers = soldiers;
        WagesInfo.PerDay = banker != null ? PerDay(soldiers) : 0;
        WagesInfo.Strength = Strength;
        WagesInfo.StartsIn = WagesInfo.Active ? StartsIn : 0;
    }

    /// <summary>Dawn (after Banker.HandleOnDayStart has added the interest): pay the day's wages, once per day.</summary>
    public static void Payday(Banker banker)
    {
        if (!Enabled || banker == null || !GameState.Playing() || !NetworkBigBoss.HasWorldAuth)
            return;
        var director = Managers.InstExists && Managers.Inst != null ? Managers.Inst.director : null;
        if (director == null)
            return;

        var (reign, land) = Where();
        var debtKey = DebtKey();
        var graceKey = GraceKey();
        if (debtKey == null || graceKey == null)
            return;

        // Once per day, even if the game raises the event again (e.g. after loading).
        var day = director.TotalDaysInReign + 1;
        if (UpgradeStore.Value(PaidDayKey(reign, land)) == day)
            return;
        UpgradeStore.SetValue(PaidDayKey(reign, land), day);

        // No army upgrades, no wages (debt left over still gets paid off as coins reach the bank).
        if (!ArmyUpgraded)
        {
            Plugin.Logger.LogInfo("Payday: the army has no upgrades, so there are no wages.");
            return;
        }

        // Grace period (a bank first seen at this very dawn starts it now).
        var stored = UpgradeStore.Value(graceKey);
        var (daysLeft, due) = WagesMath.GraceDawn(stored == 0 ? GraceDays : stored - 1);
        UpgradeStore.SetValue(graceKey, daysLeft + 1);
        if (!due)
        {
            Plugin.Logger.LogInfo($"Payday: grace period, this island's wages are {FirstPaid(daysLeft)}.");
            return;
        }

        var soldiers = CountSoldiers();
        var wages = PerDay(soldiers);
        var stash = banker._stashedCoins;
        var (left, owed, paid) = WagesMath.Settle(stash, Debt + wages);
        banker._stashedCoins = left;
        UpgradeStore.SetValue(debtKey, owed);
        WagesInfo.LastPaid = paid;
        WagesInfo.LastPaydayTime = Time.time;
        Plugin.Logger.LogInfo($"Payday: {wages} wages for {soldiers} soldiers, paid {paid} from the bank ({stash} -> {left}); debt now {owed}.");
    }

    /// <summary>The PAY button: pays as much of the debt as the player's purse can. Returns the coins paid.</summary>
    public static int PayFromPurse(Player player)
    {
        // Like buying: only the machine that owns the world (the host, or single player).
        if (!GameState.Playing() || !NetworkBigBoss.HasWorldAuth)
            return 0;
        var key = DebtKey();
        var debt = Debt;
        var wallet = player != null ? player.wallet : null;
        if (key == null || debt <= 0 || wallet == null)
            return 0;
        var paid = Math.Min(debt, wallet.GetCurrency(CurrencyType.Coins));
        if (paid <= 0)
            return 0;
        wallet.RemoveCurrency(CurrencyType.Coins, paid);
        UpgradeStore.SetValue(key, debt - paid);
        Plugin.Logger.LogInfo($"Wages: paid {paid} of the soldiers' debt from the purse; debt now {debt - paid}.");
        return paid;
    }

    /// <summary>Archers and every other fighting unit the kingdom has (knights, pikemen, berserkers, ninjas).</summary>
    private static int CountSoldiers()
    {
        var count = 0;
        foreach (var kind in Unlocks.Kinds)
        {
            if (kind == Unlocks.Archers || kind.Soldier)
                count += Unlocks.Alive(kind).Count;
        }
        return count;
    }

    internal static Banker FindBanker()
    {
        foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<Banker>(), FindObjectsSortMode.None))
        {
            var banker = obj.TryCast<Banker>();
            if (banker != null && banker.isActiveAndEnabled)
                return banker;
        }
        return null;
    }
}
