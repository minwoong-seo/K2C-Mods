using System;
using System.Collections.Generic;

namespace KingdomMenu;

/// <summary>What a page needs from the menu it's in.</summary>
internal sealed class PageContext
{
    /// <summary>0 = player 1, 1 = player 2.</summary>
    public int Player;
    public GameArt Art;
    /// <summary>The BepInEx/config/kingdommenu.preview flag: sample data outside a kingdom, nothing can be bought.</summary>
    public bool Preview;
    public Action<string> SetStatus;
}

/// <summary>One tab of the menu. It fills the rows; the session and view do the rest.</summary>
internal abstract class Page
{
    protected readonly PageContext Ctx;

    protected Page(PageContext ctx)
    {
        Ctx = ctx;
    }

    public abstract string Title { get; }

    /// <summary>Labels of a second tab row (null = none) and which one is selected.</summary>
    public virtual IReadOnlyList<string> SubTabs => null;
    public int SubTab;

    /// <summary>Header buttons: label of PRICE / RESET, or null to hide them on this page.</summary>
    public virtual string PriceLabel => null;
    public virtual string ResetLabel => null;
    public virtual void OnReset() { }

    /// <summary>Shown when the page has no rows.</summary>
    public virtual string EmptyMessage => "";

    /// <summary>Footer hint when no status message is showing.</summary>
    public virtual string Hint(int player) => null;

    public abstract void Fill(List<RowModel> rows);

    protected static string FreeLabel => Plugin.FreePurchases.Value ? "PRICE: FREE" : "PRICE: NORMAL";

    /// <summary>Like dropping coins one by one in the game: dearer things take longer to hold.</summary>
    protected static float HoldSecondsForPrice(int price) => Math.Clamp(0.25f + 0.12f * price, 0.4f, 1.6f);
}
