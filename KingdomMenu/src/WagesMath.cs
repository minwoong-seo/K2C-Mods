using System;

namespace KingdomMenu;

/// <summary>
/// The arithmetic behind soldier wages and the banker's deposits, kept free of game types so it can be checked on its
/// own (see <see cref="Wages"/> and <see cref="BankerDropOffPatch"/>).
/// </summary>
internal static class WagesMath
{
    /// <summary>Seconds the banker waits after the last coin he was given before taking his purse to the bank.</summary>
    public const float DepositDelay = 2f;

    /// <summary>The day's wages: 1 coin for every soldiersPerCoin soldiers, rounded up.</summary>
    public static int PerDay(int soldiers, int soldiersPerCoin)
    {
        var per = Math.Max(1, soldiersPerCoin);
        return soldiers <= 0 ? 0 : (soldiers + per - 1) / per;
    }

    /// <summary>How much debt weakens soldiers: perCoin for each coin owed, but never more than max (itself at most 0.9).</summary>
    public static float Penalty(int debt, float perCoin, float max) =>
        debt <= 0 ? 0f : Math.Clamp(debt * perCoin, 0f, Math.Clamp(max, 0f, 0.9f));

    /// <summary>
    /// One dawn of a new bank's grace period. daysLeft is the days until its first wages (1 = this dawn); returns what's
    /// left after this dawn and whether this dawn's wages are due.
    /// </summary>
    public static (int DaysLeft, bool Due) GraceDawn(int daysLeft) => daysLeft > 1 ? (daysLeft - 1, false) : (0, true);

    /// <summary>Pays what's owed from the bank, as far as it goes; the rest stays owed.</summary>
    public static (int Stash, int Owed, int Paid) Settle(int stash, int owed)
    {
        stash = Math.Max(0, stash);
        owed = Math.Max(0, owed);
        var paid = Math.Min(owed, stash);
        return (stash - paid, owed - paid, paid);
    }

    /// <summary>
    /// Whether the banker should take what he's carrying to the bank: he has coins, he's idle, nobody has given him one
    /// for <see cref="DepositDelay"/> seconds, and there's none left near him to pick up (asked last: it scans for coins).
    /// </summary>
    public static bool ShouldDeposit(int coins, bool idle, float secondsSinceLastCoin, Func<bool> coinToPickUp) =>
        coins > 0 && idle && secondsSinceLastCoin >= DepositDelay && !coinToPickUp();
}
