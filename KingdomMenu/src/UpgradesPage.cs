using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KingdomMenu;

/// <summary>Coin upgrades for workers, army, steed and walls, saved per campaign (see <see cref="UpgradeStore"/>).</summary>
internal sealed class UpgradesPage : Page
{
    private const float ResetConfirmSeconds = 3f;

    private static readonly Category[] Categories = { Category.Workers, Category.Army, Category.Monarch, Category.Kingdom };
    private static readonly string[] CategoryNames = { "WORKERS", "ARMY", "MONARCH", "KINGDOM" };

    private float _resetConfirmUntil;

    public UpgradesPage(PageContext ctx) : base(ctx) { }

    /// <summary>With soldier wages on, army upgrades need a bank on this island (see <see cref="Wages"/>).</summary>
    private bool ArmyTab => Categories[SubTab] == Category.Army;

    // Mentions the bank only once the campaign has had one (no spoilers).
    public override string EmptyMessage => ArmyTab && Wages.ArmyNeedsBank
        ? (UpgradeStore.Flag(Wages.BankSeenFlag) ? "Army upgrades need a bank on this island." : "Nothing to train yet.")
        : "";

    public override string Title => "UPGRADES";
    public override IReadOnlyList<string> SubTabs => CategoryNames;
    public override string PriceLabel => FreeLabel;
    public override string ResetLabel => Time.unscaledTime < _resetConfirmUntil ? "SURE?" : "RESET";

    public override string Hint(int player)
    {
        var (submit, cancel, tabs) = Coop.KeyNames(player);
        return $"Hold {submit}: buy  {tabs}: tabs  {cancel}: close";
    }

    public override void Fill(List<RowModel> rows)
    {
        var playing = GameState.Playing();
        var preview = Ctx.Preview && !playing;
        var wallet = playing ? Coop.Monarch(Ctx.Player)?.wallet : null;
        var coins = wallet != null ? wallet.GetCurrency(CurrencyType.Coins) : (preview ? 30 : (int?)null);
        var free = Plugin.FreePurchases.Value;

        // Army upgrades bring soldier wages: the wages row comes first, once the island has a bank.
        var armyOff = ArmyTab && Wages.ArmyNeedsBank;
        if (ArmyTab)
        {
            var wages = WagesRow.Build(Ctx, armyTab: true);
            if (wages != null)
                rows.Add(wages);
        }

        // Upgrades for units not reached yet stay hidden (unless already bought, or in the layout preview). Without a
        // bank on this island (wages on), army upgrades can't be bought and the ones already bought are switched off.
        var page = Upgrades.In(Categories[SubTab])
            .Where(u => preview || (playing && UpgradeStore.Level(u, Ctx.Player) > 0)
                        || (!armyOff && (u.Unlocked == null || u.Unlocked())));
        var i = 0;
        foreach (var u in page)
        {
            var level = playing ? UpgradeStore.Level(u, Ctx.Player) : (preview ? (i * 2) % (u.MaxLevel + 1) : 0);
            i++;
            var maxed = level >= u.MaxLevel;
            var cost = free ? 0 : u.Cost(level);
            if (armyOff)
            {
                rows.Add(new RowModel
                {
                    Icon = Ctx.Art.IconFor(u, Ctx.Player),
                    Name = u.Name,
                    NameColor = MenuView.Dim,
                    Detail = "Off until this island has a bank",
                    Tip = "Army upgrades need a bank on this island to pay the soldiers.",
                    Pips = u.MaxLevel,
                    PipsFull = level,
                    PipsGold = true,
                    Buttons = new[] { new RowButton { Label = "BUY", Enabled = false } },
                });
                continue;
            }
            var canBuy = !maxed && (playing || preview) && coins.HasValue && coins.Value >= cost;
            var upgrade = u;
            if (u.IsSwitch && maxed)
            {
                // Bought: switch it on and off.
                var on = !UpgradeStore.Flag(SwitchOffFlag(u, Ctx.Player));
                rows.Add(new RowModel
                {
                    Icon = Ctx.Art.IconFor(u, Ctx.Player),
                    Name = u.Name,
                    NameColor = on ? MenuView.Gold : MenuView.Cream,
                    Detail = on ? $"On: {u.SwitchText}" : "Off: as normal",
                    Pips = u.MaxLevel,
                    PipsFull = level,
                    PipsGold = true,
                    Buttons = new[]
                    {
                        new RowButton { Label = on ? "ON" : "OFF", Enabled = playing, HoldSeconds = SwitchHoldSeconds, Activate = () => Switch(upgrade) },
                    },
                });
                continue;
            }
            rows.Add(new RowModel
            {
                Tip = WageTip(u, playing),
                Icon = Ctx.Art.IconFor(u, Ctx.Player),
                Name = u.Name,
                NameColor = maxed ? MenuView.Gold : MenuView.Cream,
                Detail = u.Effect(level),
                Pips = u.MaxLevel,
                PipsFull = level,
                PipsGold = true,
                Price = maxed ? null : cost,
                PriceLabel = maxed ? "MAX" : null,
                CanAfford = canBuy,
                Buttons = new[]
                {
                    new RowButton { Label = "BUY", Enabled = canBuy, HoldSeconds = HoldSecondsForPrice(cost), Activate = () => Buy(upgrade) },
                },
            });
        }
    }

    private const float SwitchHoldSeconds = 0.25f;

    /// <summary>Army rows' tooltip: the first army upgrade is what starts soldier wages.</summary>
    private static string WageTip(Upgrade upgrade, bool playing)
    {
        if (upgrade.Category != Category.Army || !playing || !Wages.Enabled || !Wages.BankHere)
            return null;
        var cost = Wages.DailyCost(WagesInfo.Soldiers);
        if (Wages.ArmyUpgraded)
            return $"Army upgrades come with wages: {cost}, paid at dawn.";
        return Wages.PerDay(WagesInfo.Soldiers) > 0
            ? $"Buying this starts wages: {cost}, {Wages.FirstPaid(Wages.FirstWagesIn())}."
            : $"Buying this starts soldier wages: {cost}.";
    }

    /// <summary>Per-save flag set while a bought switch is off (so a fresh purchase starts on).</summary>
    private static string SwitchOffFlag(Upgrade upgrade, int player) => upgrade.Key(player) + "_off";

    private void Switch(Upgrade upgrade)
    {
        if (!GameState.Playing())
            return;
        var flag = SwitchOffFlag(upgrade, Ctx.Player);
        var nowOff = !UpgradeStore.Flag(flag);
        UpgradeStore.SetFlag(flag, nowOff);
        Ctx.SetStatus($"{upgrade.Name} {(nowOff ? "off" : "on")}.");
    }

    private void Buy(Upgrade upgrade)
    {
        if (!GameState.Playing())
        {
            Ctx.SetStatus(Ctx.Preview ? "Preview only - load into a kingdom to buy." : "Load into a kingdom first.");
            return;
        }

        // Only the machine that owns the world simulates units; a client's changes wouldn't stick.
        if (!NetworkBigBoss.HasWorldAuth)
        {
            Ctx.SetStatus("Only the host can buy upgrades.");
            return;
        }

        if (upgrade.Category == Category.Army && Wages.ArmyNeedsBank)
        {
            Ctx.SetStatus("Army upgrades need a bank on this island.");
            return;
        }

        var level = UpgradeStore.Level(upgrade, Ctx.Player);
        if (level >= upgrade.MaxLevel)
        {
            Ctx.SetStatus($"{upgrade.Name} is already maxed.");
            return;
        }

        // Each player pays from their own purse, for kingdom-wide upgrades too.
        var cost = Plugin.FreePurchases.Value ? 0 : upgrade.Cost(level);
        if (cost > 0)
        {
            var wallet = Coop.Monarch(Ctx.Player)?.wallet;
            if (wallet == null)
            {
                Ctx.SetStatus("Couldn't find your purse.");
                return;
            }
            var have = wallet.GetCurrency(CurrencyType.Coins);
            if (have < cost)
            {
                Ctx.SetStatus($"Not enough coins: need {cost}, have {have}.");
                return;
            }
            wallet.RemoveCurrency(CurrencyType.Coins, cost);
        }

        var startsWages = upgrade.Category == Category.Army && Wages.Enabled && !Wages.ArmyUpgraded;
        var firstIn = Wages.FirstWagesIn();
        UpgradeStore.SetLevel(upgrade, Ctx.Player, level + 1);
        Ctx.SetStatus(startsWages
            ? $"{upgrade.Name} level {level + 1}! Wages start, {Wages.FirstPaid(firstIn)}."
            : $"{upgrade.Name} level {level + 1}!");
    }

    public override void OnReset()
    {
        if (!GameState.Playing())
        {
            Ctx.SetStatus("Load into a kingdom first.");
            return;
        }

        // Two presses: the first arms it ("SURE?"), the second within a few seconds resets. No coins are refunded.
        if (Time.unscaledTime >= _resetConfirmUntil)
        {
            _resetConfirmUntil = Time.unscaledTime + ResetConfirmSeconds;
            Ctx.SetStatus("Press again to reset (no refund).");
            return;
        }

        _resetConfirmUntil = 0f;
        UpgradeStore.ResetCurrentSave(Ctx.Player);
        Ctx.SetStatus(Coop.TwoPlayers ? "Kingdom upgrades and your own reset." : "All upgrades reset for this save.");
    }
}
