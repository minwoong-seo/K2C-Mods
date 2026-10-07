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

        // Upgrades for units not reached yet stay hidden (unless already bought, or in the layout preview).
        var page = Upgrades.In(Categories[SubTab])
            .Where(u => preview || u.Unlocked == null || u.Unlocked() || (playing && UpgradeStore.Level(u, Ctx.Player) > 0));
        var i = 0;
        foreach (var u in page)
        {
            var level = playing ? UpgradeStore.Level(u, Ctx.Player) : (preview ? (i * 2) % (u.MaxLevel + 1) : 0);
            i++;
            var maxed = level >= u.MaxLevel;
            var cost = free ? 0 : u.Cost(level);
            var canBuy = !maxed && (playing || preview) && coins.HasValue && coins.Value >= cost;
            var upgrade = u;
            rows.Add(new RowModel
            {
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

        UpgradeStore.SetLevel(upgrade, Ctx.Player, level + 1);
        Ctx.SetStatus($"{upgrade.Name} level {level + 1}!");
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
