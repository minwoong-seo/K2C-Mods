using System.Collections.Generic;
using System.Linq;

namespace KingdomMenu;

/// <summary>The Remote Shop: every open stand in the kingdom, bought from as if coins had been dropped on it.</summary>
internal sealed class ShopPage : Page
{
    private string _lastSummary;

    public ShopPage(PageContext ctx) : base(ctx) { }

    public override string Title => "SHOP";
    public override string PriceLabel => FreeLabel;

    public override string EmptyMessage => !GameState.Playing()
        ? "No shops found. Load into a kingdom first."
        : "No stands open yet. Build up your town center.";

    public override string Hint(int player)
    {
        var (submit, cancel, tabs) = Coop.KeyNames(player);
        return $"Hold {submit}: buy  {tabs}: tabs  {cancel}: close";
    }

    public override void Fill(List<RowModel> rows)
    {
        if (Ctx.Preview && !GameState.Playing())
        {
            AddPreviewRows(rows);
            return;
        }

        var groups = Shops.Find(out var summary);
        if (Ctx.Player == 0 && summary != _lastSummary)
        {
            _lastSummary = summary;
            Plugin.Logger.LogInfo($"Stands: {(summary.Length > 0 ? summary : "none")}");
        }

        var buyer = Coop.Monarch(Ctx.Player);
        var wallet = buyer != null ? buyer.wallet : null;
        var free = Plugin.FreePurchases.Value;

        foreach (var g in groups)
        {
            var live = g.Shops.Where(s => s != null).ToList();
            var price = live.Count > 0 ? live.Min(s => s.Price) : 0;
            var currency = live.Count > 0 ? live[0].Currency : CurrencyType.Coins;
            var affordable = free || (wallet != null && wallet.GetCurrency(currency) >= price);
            var available = buyer != null && live.Any(s => s.isActiveAndEnabled && s.CanPay(buyer));
            var canBuy = affordable && available;
            var hold = HoldSecondsForPrice(price);
            var group = g;
            rows.Add(StandRow(g.Label, Shops.ItemSprite(g) ?? Ctx.Art.IconFor(g.Type), free ? 0 : price, affordable, available,
                live.Sum(s => s.GetItemCount()), live.Sum(Shops.Capacity),
                new RowButton { Label = "BUY", Enabled = canBuy, HoldSeconds = hold, Activate = () => Buy(group, fill: false) },
                new RowButton { Label = "FILL", Enabled = canBuy, HoldSeconds = hold + 0.5f, Activate = () => Buy(group, fill: true) }));
        }
    }

    private static RowModel StandRow(string label, UnityEngine.Sprite icon, int price, bool affordable, bool available, int stock, int capacity,
        RowButton buy, RowButton fill)
    {
        // Stock as pips for small stands (like the items lying on the stand), as text otherwise.
        var pips = capacity > 0 && capacity <= 8;
        return new RowModel
        {
            Icon = icon,
            Name = label,
            NameColor = available ? MenuView.Cream : MenuView.Dim,
            Price = price,
            CanAfford = affordable,
            Pips = pips ? capacity : 0,
            PipsFull = stock,
            PipText = pips ? null : $"{stock}/{capacity}",
            Buttons = new[] { buy, fill },
        };
    }

    private void AddPreviewRows(List<RowModel> rows)
    {
        RowButton Button(string label, bool enabled) =>
            new() { Label = label, Enabled = enabled, Activate = () => Ctx.SetStatus("Preview only - nothing was bought.") };

        var art = Ctx.Art;
        rows.Add(StandRow("Bow", art.IconFor(PayableShop.ShopType.Bow), 2, true, true, 1, 4, Button("BUY", true), Button("FILL", true)));
        rows.Add(StandRow("Hammer", art.IconFor(PayableShop.ShopType.Hammer), 3, true, true, 4, 4, Button("BUY", true), Button("FILL", true)));
        rows.Add(StandRow("Scythe", art.IconFor(PayableShop.ShopType.Scythe), 4, true, false, 2, 2, Button("BUY", false), Button("FILL", false)));
        rows.Add(StandRow("Pike (right)", art.IconFor(PayableShop.ShopType.PikeRight), 8, false, true, 0, 3, Button("BUY", false), Button("FILL", false)));
    }

    private void Buy(ShopGroup group, bool fill)
    {
        var buyer = Coop.Monarch(Ctx.Player);
        if (buyer == null)
        {
            Ctx.SetStatus("No player found. Load into a kingdom first.");
            return;
        }

        var bought = 0;
        string failure;
        do
        {
            failure = Shops.TryBuyOne(group, buyer);
            if (failure == null)
                bought++;
        } while (fill && failure == null && bought < 64);

        if (bought == 0)
            Ctx.SetStatus(failure);
        else if (bought == 1 && !fill)
            Ctx.SetStatus($"Bought a {group.Label.ToLowerInvariant()}.");
        else
            Ctx.SetStatus($"Bought {bought} x {group.Label.ToLowerInvariant()}.");
    }
}
