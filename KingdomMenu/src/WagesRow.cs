using System;

namespace KingdomMenu;

/// <summary>
/// The "Soldier wages" row: what the soldiers cost a day, when the first wages are due, or the debt in red with a PAY
/// button. It's on the UNITS page while wages are being paid or owed, and at the top of the ARMY upgrades once the
/// island has a bank, since army upgrades are what bring wages.
/// </summary>
internal static class WagesRow
{
    public static RowModel Build(PageContext ctx, bool armyTab)
    {
        if (!Wages.Enabled)
            return null;
        var playing = GameState.Playing();
        var preview = ctx.Preview && !playing;
        if (!preview && (armyTab ? !Wages.BankHere : !WagesInfo.Active))
            return null;

        var soldiers = preview ? 22 : WagesInfo.Soldiers;
        var perDay = Wages.PerDay(soldiers);
        var upgraded = preview || Wages.ArmyUpgraded;
        var debt = preview ? 12 : Wages.Debt;
        var strength = preview ? 0.76f : Wages.Strength;
        var startsIn = preview ? 0 : WagesInfo.StartsIn;
        var count = $"{soldiers} {(soldiers == 1 ? "soldier" : "soldiers")}";
        var wallet = playing ? Coop.Monarch(ctx.Player)?.wallet : null;
        var coins = wallet != null ? wallet.GetCurrency(CurrencyType.Coins) : 0;
        var pay = Math.Min(debt, coins);

        return new RowModel
        {
            Icon = ctx.Art.Get("banker_idle_0"),
            Name = "Soldier wages",
            Right = $"{perDay}/day",
            RightColor = upgraded ? MenuView.Cream : MenuView.Dim,
            Detail = debt > 0
                ? $"Debt {UnitsPage.Colour(debt.ToString(), UnitsPage.Worse)}  {UnitsPage.Stat("Strength", 1f, strength)}"
                : !upgraded ? $"{count}, from your first army upgrade"
                : startsIn > 1 ? $"{count}, first paid in {startsIn} days"
                : startsIn == 1 ? $"{count}, first paid tomorrow"
                : $"{count}, paid from the bank at dawn",
            Tip = debt > 0
                ? "Soldiers fight weaker until it's repaid: bank coins or hold PAY."
                : !upgraded ? $"Your first army upgrade starts these wages, {Wages.FirstPaid(Wages.FirstWagesIn())}."
                : "Paid from this island's bank at dawn; any shortfall becomes debt.",
            Buttons = debt > 0
                ? new[]
                {
                    new RowButton
                    {
                        Label = "PAY", Enabled = pay > 0 && !preview && NetworkBigBoss.HasWorldAuth, HoldSeconds = Math.Clamp(0.25f + 0.12f * pay, 0.4f, 1.6f),
                        Activate = () => PayDebt(ctx),
                    },
                }
                : Array.Empty<RowButton>(),
        };
    }

    private static void PayDebt(PageContext ctx)
    {
        var paid = Wages.PayFromPurse(Coop.Monarch(ctx.Player));
        var left = Wages.Debt;
        ctx.SetStatus(paid <= 0 ? "No coins to pay with." : left > 0 ? $"Paid {paid} of the debt, {left} left." : $"Paid off the debt ({paid}).");
    }
}
