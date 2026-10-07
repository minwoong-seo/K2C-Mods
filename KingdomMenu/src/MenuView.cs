using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>A hold-to-activate button on a row (BUY, FILL).</summary>
internal class RowButton
{
    public string Label;
    public bool Enabled;
    public float HoldSeconds = 0.5f;
    public Action Activate;
}

/// <summary>
/// One line of a page. Every part is optional: the view lays out whatever is set, right to left (buttons, price, pips),
/// and gives the name and detail lines the rest of the width.
/// </summary>
internal class RowModel
{
    public Sprite Icon;
    public string Name;
    public Color NameColor = MenuView.Cream;
    /// <summary>Second, dimmer line under the name.</summary>
    public string Detail;
    public Color DetailColor = MenuView.Dim;
    /// <summary>Short text at the right end of the name line (info rows), e.g. a count or a status.</summary>
    public string Right;
    public Color RightColor = MenuView.Cream;

    /// <summary>Pips: how many to draw (0 = none) and how many are full. Gold for levels, cream for stock.</summary>
    public int Pips;
    public int PipsFull;
    public bool PipsGold;
    /// <summary>Shown instead of pips when there are too many (e.g. "7/20").</summary>
    public string PipText;

    /// <summary>Coin price (null = no price). Up to 5 coins are drawn like the game's cost indicators.</summary>
    public int? Price;
    public bool CanAfford = true;
    /// <summary>Replaces the price, e.g. "MAX".</summary>
    public string PriceLabel;

    public RowButton[] Buttons = Array.Empty<RowButton>();

    /// <summary>A tooltip: shown in the footer, instead of the page's hint, while the row has the focus (or the mouse).</summary>
    public string Tip;
}

internal class UiButton
{
    public RectTransform Rect;
    public Image Background;
    public Text Label;
    public Sprite Normal, Hover, Pressed, Disabled;
    public bool Enabled = true;
    public bool Selected;
    public bool Hovered;
    public Action OnClick;

    // Which group the button belongs to: 0 = header buttons, 1 = main tabs, 2 = sub-tabs, 10+ = page rows (one per row).
    public int GridRow;
    public bool IsTab;

    // Hold-to-activate buttons (purchases) fill from left to right while held, like paying in the game.
    public bool HoldToActivate;
    public float HoldSeconds = 0.5f;
    public float Progress;
    public RectTransform FillRect;
    public Image FillImage;
}

/// <summary>
/// The Kingdom Menu panel, built with uGUI from the game's own menu sprites and pixel fonts: a header with the purse,
/// a row of tabs (and sub-tabs for some pages), the page's rows and a footer line.
/// Layout is in "art pixels" (1 unit = 1 sprite pixel); the canvas scales everything by a whole number so the pixel art
/// stays crisp. Each player has their own view (own canvas), placed inside that player's part of the screen.
/// </summary>
internal class MenuView
{
    private class RowView
    {
        public RectTransform Rect;
        public Image Frame;
        public Text Key;
        public RectTransform IconRect;
        public Image Icon;
        public Text Name;
        public Text Detail;
        public Text Right;
        public readonly Image[] Pips = new Image[MaxPips];
        public Text PipText;
        public readonly Image[] Coins = new Image[MaxCoins];
        public Text PriceText;
        public readonly UiButton[] Buttons = new UiButton[MaxButtons];
        /// <summary>Invisible focus target covering the row, for rows without buttons (so a controller can scroll them).</summary>
        public UiButton Item;
    }

    private const int MaxPips = 8;
    private const int MaxCoins = 5;
    private const int MaxButtons = 2;
    private const float Inset = 13f;
    private const float SidebarWidth = 72f;
    private const float ContentX = Inset + SidebarWidth + 8f;
    private const float ContentWidth = 274f;
    private const float PanelWidth = ContentX + ContentWidth + Inset;
    private const float HeaderWidth = PanelWidth - Inset * 2f;
    private const float RowWidth = ContentWidth + 8f;
    private const float BodyTop = 63f;
    private const float TabStep = 24f;
    private const float RowStep = 34f;
    private const float RowHeight = 32f;
    private const float IconSlot = 24f;
    private const float FillInset = 2f;
    private const int FirstRowGrid = 10;
    /// <summary>Rows shown at once; longer pages scroll. Fewer are shown (down to MinVisibleRows) before the menu shrinks.</summary>
    private const int MaxVisibleRows = 6;
    private const int MinVisibleRows = 4;
    /// <summary>Art pixels kept clear above the panel (the game's coin purse sits at the top of each view).</summary>
    private const float TopMargin = 56f;
    /// <summary>The mods' versions, one per line at the foot of the tab column.</summary>
    private const float VersionLineStep = 13f;

    internal static readonly Color Cream = new(0.96f, 0.91f, 0.78f);
    internal static readonly Color Gold = new(1f, 0.82f, 0.36f);
    internal static readonly Color Dim = new(0.62f, 0.55f, 0.46f);
    internal static readonly Color Danger = new(0.95f, 0.38f, 0.28f);
    private static readonly Color TextShadow = new(0.07f, 0.04f, 0.03f, 0.9f);
    private static readonly Color PanelFallback = new(0.17f, 0.11f, 0.10f, 0.96f);
    private static readonly Color ButtonFallback = new(0.78f, 0.45f, 0.20f, 1f);
    private static readonly Color Trim = new(0.78f, 0.47f, 0.23f, 0.55f);
    private static readonly Color PipEmpty = new(0.09f, 0.06f, 0.05f, 0.9f);
    private static readonly Color CoinUnaffordable = new(0.45f, 0.40f, 0.36f, 0.9f);
    private static readonly Color HoldFill = new(1f, 0.88f, 0.45f, 0.6f);

    private readonly GameArt _art;
    private readonly List<RowView> _rows = new();
    private readonly List<UiButton> _buttons = new();
    private readonly List<UiButton> _tabs = new();
    private readonly List<UiButton> _subTabs = new();
    private readonly List<RowModel> _models = new();

    private GameObject _root;
    private CanvasScaler _scaler;
    private RectTransform _panel;
    private Image _purseCoin;
    private Text _purseText;
    private Image _gemIcon;
    private Text _gemText;
    private Text _title;
    private Text _closeHint;
    private UiButton _resetButton;
    private UiButton _priceButton;
    private Text _empty;
    private Text _footer;
    private RectTransform _sideDivider;
    private readonly List<Text> _versions = new();
    private RectTransform _scrollThumb;
    private int _scroll;
    private int _visibleRows = MaxVisibleRows;
    private int _layoutShown = -1;
    private int _layoutTotal = -1;
    private UiButton _pressed;
    private UiButton _holding;
    private UiButton _focused;
    private bool _holdConsumed;
    private Vector2 _lastMouse = new(-1f, -1f);
    /// <summary>The mouse moved last (rather than the keys or stick), so tooltips follow the row under it.</summary>
    private bool _tipFromMouse;
    private bool _subTabsShown;

    public Action<int> OnTab;
    public Action<int> OnSubTab;
    public Action OnTogglePrice;
    public Action OnReset;

    public MenuView(GameArt art)
    {
        _art = art;
    }

    public bool Alive => _root != null;

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.SetActive(visible);
    }

    // ------------------------------------------------------------------ construction

    public void Build(IReadOnlyList<string> tabNames, int player)
    {
        _root = new GameObject($"KingdomMenuCanvas_P{player + 1}");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000 + player;
        canvas.pixelPerfect = true;

        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.referencePixelsPerUnit = 32f; // the game's sprites are 32 px per unit -> 1 sprite pixel = 1 canvas unit
        _scaler.scaleFactor = 3f;

        // Lets the game's EventSystem see the panel, so clicks on it don't fall through to game menus underneath.
        _root.AddComponent<GraphicRaycaster>();

        _panel = NewRect("Panel", _root.transform, 0f, 0f, PanelWidth, 200f);
        _panel.pivot = new Vector2(0.5f, 1f); // positioned by Place(), by its top-centre
        AddImage(_panel, _art.Get("menu_panel_background"), true, PanelFallback).raycastTarget = true;

        // Header: crown + title, close hint
        var crown = _art.Get("menu_crown");
        if (crown != null)
            AddImage(NewRect("Crown", _panel, Inset, 9f, 19f, 18f), crown, false, Color.white).color = Gold;
        _title = AddText(NewRect("Title", _panel, Inset + 24f, 9f, 160f, 18f), _art.Title, "KINGDOM", Gold, TextAnchor.MiddleLeft);
        _closeHint = AddText(NewRect("CloseHint", _panel, Inset, 9f, HeaderWidth, 18f), _art.Body, "", Dim, TextAnchor.MiddleRight);

        // Purse (coins, gems) + the page's header buttons
        _purseCoin = AddImage(NewRect("PurseCoin", _panel, Inset + 3f, 38f, 10f, 10f), _art.Get(GameArt.CoinFrames[0]), false, Gold);
        _purseText = AddText(NewRect("Purse", _panel, Inset + 17f, 33f, 40f, 20f), _art.Body, "", Cream, TextAnchor.MiddleLeft);
        _gemIcon = AddImage(NewRect("Gem", _panel, Inset + 52f, 37f, 12f, 12f), _art.Get("baggem"), false, Cream);
        _gemIcon.preserveAspect = true;
        _gemText = AddText(NewRect("Gems", _panel, Inset + 67f, 33f, 40f, 20f), _art.Body, "", Cream, TextAnchor.MiddleLeft);
        _resetButton = AddButton(_panel, "Reset", PanelWidth - Inset - 92f - 4f - 56f, 33f, 56f, 20f, "RESET", () => OnReset?.Invoke());
        _priceButton = AddButton(_panel, "PriceToggle", PanelWidth - Inset - 92f, 33f, 92f, 20f, "", () => OnTogglePrice?.Invoke());

        AddImage(NewRect("Divider", _panel, Inset, 58f, HeaderWidth, 1f), null, false, Trim);

        // Main tabs: a column down the left side; the page fills the rest.
        for (var i = 0; i < tabNames.Count; i++)
        {
            var index = i;
            var b = AddButton(_panel, $"Tab{i}", Inset, BodyTop + i * TabStep, SidebarWidth, 20f, tabNames[i], () => OnTab?.Invoke(index));
            b.GridRow = 1;
            b.IsTab = true;
            _tabs.Add(b);
        }
        _sideDivider = NewRect("SideDivider", _panel, Inset + SidebarWidth + 4f, BodyTop, 1f, 20f);
        AddImage(_sideDivider, null, false, Trim);
        // On long pages a gold thumb on the divider shows where the visible rows are.
        _scrollThumb = NewRect("ScrollThumb", _panel, Inset + SidebarWidth + 3f, BodyTop, 3f, 10f);
        AddImage(_scrollThumb, null, false, Gold);

        _empty = AddText(NewRect("Empty", _panel, ContentX + 4f, 0f, ContentWidth - 8f, RowHeight), _art.Body, "", Dim, TextAnchor.MiddleLeft);
        _footer = AddText(NewRect("Footer", _panel, Inset + 4f, 0f, HeaderWidth - 8f, 16f), _art.Body, "", Dim, TextAnchor.MiddleLeft);
    }

    /// <summary>A row of tabs across the top of the page area (e.g. the upgrade categories).</summary>
    private List<UiButton> BuildSubTabRow(IReadOnlyList<string> labels, Action<int> onSelect)
    {
        var list = new List<UiButton>();
        var gap = 4f;
        var width = (ContentWidth - gap * (labels.Count - 1)) / labels.Count;
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var b = AddButton(_panel, $"SubTab{i}", ContentX + i * (width + gap), BodyTop, width, 20f, labels[i], () => onSelect(index));
            b.GridRow = 2;
            b.IsTab = true;
            list.Add(b);
        }
        return list;
    }

    private RowView BuildRow(int index)
    {
        var row = new RowView();
        row.Rect = NewRect($"Row{index}", _panel, ContentX - 4f, 0f, RowWidth, RowHeight);

        row.Frame = AddImage(row.Rect, _art.Get("menu_button_frame_highlight"), true, new Color(1f, 1f, 1f, 0.15f));
        row.Frame.enabled = false;

        row.Item = AddButton(row.Rect, "Item", 0f, 0f, RowWidth, RowHeight, "", null);
        row.Item.Background.enabled = false;
        row.Item.GridRow = FirstRowGrid + index;

        row.Key = AddText(NewRect("Key", row.Rect, 5f, 0f, 9f, RowHeight), _art.Body, index < 9 ? (index + 1).ToString() : "", Dim, TextAnchor.MiddleCenter);

        row.IconRect = NewRect("Icon", row.Rect, 0f, 0f, 16f, 16f);
        row.IconRect.pivot = new Vector2(0.5f, 0.5f);
        row.IconRect.anchoredPosition = new Vector2(16f + IconSlot / 2f, -RowHeight / 2f);
        row.Icon = AddImage(row.IconRect, null, false, Color.white);

        row.Name = AddText(NewRect("Name", row.Rect, 44f, 1f, 130f, 16f), _art.Body, "", Cream, TextAnchor.MiddleLeft);
        row.Detail = AddText(NewRect("Detail", row.Rect, 44f, 15f, 130f, 16f), _art.Body, "", Dim, TextAnchor.MiddleLeft);
        row.Detail.supportRichText = true; // the Units page colours stat changes
        row.Right = AddText(NewRect("Right", row.Rect, 44f, 1f, RowWidth - 48f, 16f), _art.Body, "", Cream, TextAnchor.MiddleRight);

        for (var i = 0; i < MaxPips; i++)
            row.Pips[i] = AddImage(NewRect($"Pip{i}", row.Rect, 0f, 12f, 3f, 8f), null, false, PipEmpty);
        row.PipText = AddText(NewRect("PipText", row.Rect, 0f, 0f, 34f, RowHeight), _art.Body, "", Cream, TextAnchor.MiddleCenter);

        for (var i = 0; i < MaxCoins; i++)
            row.Coins[i] = AddImage(NewRect($"Coin{i}", row.Rect, 0f, 11f, 10f, 10f), _art.Get("coin_spin_0"), false, Gold);
        row.PriceText = AddText(NewRect("Price", row.Rect, 0f, 0f, 30f, RowHeight), _art.Body, "", Cream, TextAnchor.MiddleLeft);

        for (var i = 0; i < MaxButtons; i++)
        {
            var b = row.Buttons[i] = AddButton(row.Rect, $"Button{i}", 0f, 6f, 30f, 20f, "", null, hold: true);
            b.GridRow = FirstRowGrid + index;
            var (r, c) = (index, i);
            b.OnClick = () => ActivateRowButton(r, c);
        }
        return row;
    }

    private void ActivateRowButton(int row, int col)
    {
        var index = _scroll + row;
        if (index < _models.Count && col < _models[index].Buttons.Length)
            _models[index].Buttons[col].Activate?.Invoke();
    }

    // ------------------------------------------------------------------ updates

    /// <summary>
    /// Put the panel in one player's part of the screen (screen pixels from the top-left: a split-screen half, half of a
    /// shared screen, or the whole screen), centred across it. The pixel scale is the largest whole number up to
    /// <paramref name="desiredScale"/> at which the panel fits; a short region shows fewer rows (down to
    /// MinVisibleRows, the rest scroll) before the scale goes down. The fit is worked out for the tallest page, so
    /// switching tabs never changes the scale or moves the panel's top. It sits up to TopMargin art pixels below the
    /// region's top (clear of the coin purse) when there's room.
    /// </summary>
    public void Place(float leftPx, float topPx, float widthPx, float heightPx, int desiredScale)
    {
        var scale = Mathf.Max(1, desiredScale);
        var rows = MaxVisibleRows;
        for (; scale > 1; scale--)
        {
            if (PanelWidth * scale > widthPx)
                continue;
            rows = MaxVisibleRows;
            while (rows > MinVisibleRows && TallestPanel(rows) + 8f > heightPx / scale)
                rows--;
            if (TallestPanel(rows) + 8f <= heightPx / scale)
                break;
        }
        if (scale == 1)
        {
            rows = MaxVisibleRows;
            while (rows > MinVisibleRows && TallestPanel(rows) + 8f > heightPx)
                rows--;
        }

        if (rows != _visibleRows)
        {
            _visibleRows = rows;
            _layoutShown = -1;
            Layout();
        }
        if (!Mathf.Approximately(_scaler.scaleFactor, scale))
            _scaler.scaleFactor = scale;

        var margin = Mathf.Min(TopMargin, Mathf.Max(0f, heightPx / scale - TallestPanel(rows)));
        var half = PanelWidth / 2f;
        var x = Mathf.Clamp((leftPx + widthPx / 2f) / scale, half, Mathf.Max(half, Screen.width / (float)scale - half));
        var position = new Vector2(Mathf.Round(x), -Mathf.Round(topPx / scale + margin));
        if (_panel.anchoredPosition != position)
            _panel.anchoredPosition = position;
    }

    /// <summary>Panel height (art pixels) of a page with sub-tabs and this many rows; matches Layout().</summary>
    private float TallestPanel(int rows) =>
        Mathf.Max(SidebarBottom(), BodyTop + TabStep + rows * RowStep - 2f) + 32f;

    /// <summary>Where the tab column ends: the tabs, then the version lines under them.</summary>
    private float SidebarBottom() =>
        BodyTop + _tabs.Count * TabStep - 4f + (_versions.Count > 0 ? 4f + _versions.Count * VersionLineStep : 0f);

    /// <summary>Small dim lines at the foot of the tab column, e.g. "Menu 1.2.0" and "HUD 1.2.0".</summary>
    public void SetVersions(IReadOnlyList<string> lines)
    {
        foreach (var t in _versions)
            Object.Destroy(t.gameObject);
        _versions.Clear();
        foreach (var line in lines)
            _versions.Add(AddText(NewRect("Version", _panel, Inset + 2f, 0f, SidebarWidth, VersionLineStep), _art.Body, line, Dim, TextAnchor.MiddleLeft));
        _layoutShown = -1; // re-layout
        Layout();
    }

    public void SetHeader(string title, string closeHint, int? coins, int gems)
    {
        _title.text = title;
        _closeHint.text = closeHint;
        _purseText.text = coins.HasValue ? coins.Value.ToString() : "-";
        _gemIcon.enabled = gems > 0;
        _gemText.text = gems > 0 ? gems.ToString() : "";
    }

    /// <summary>The page's header buttons: PRICE (shop, upgrades) and RESET (upgrades). Null hides a button.</summary>
    public void SetHeaderButtons(string priceLabel, string resetLabel)
    {
        _priceButton.Rect.gameObject.SetActive(priceLabel != null);
        _resetButton.Rect.gameObject.SetActive(resetLabel != null);
        // Without the price toggle (SETTINGS' DEFAULTS), the reset button takes its wider place at the right end.
        var resetWidth = priceLabel != null ? 56f : 92f;
        Place(_resetButton.Rect, PanelWidth - Inset - 92f - (priceLabel != null ? 4f + 56f : 0f), 33f, resetWidth, 20f);
        _resetButton.Label.rectTransform.sizeDelta = new Vector2(resetWidth, 20f);
        if (priceLabel != null)
            _priceButton.Label.text = priceLabel;
        if (resetLabel != null)
            _resetButton.Label.text = resetLabel;
    }

    public void SetTab(int tab)
    {
        for (var i = 0; i < _tabs.Count; i++)
            _tabs[i].Selected = i == tab;
    }

    /// <summary>Show a second row of tabs under the main ones (null hides it).</summary>
    public void SetSubTabs(IReadOnlyList<string> labels, int selected)
    {
        var show = labels != null && labels.Count > 0;
        if (show && (_subTabs.Count != labels.Count || _subTabs[0].Label.text != labels[0]))
        {
            foreach (var b in _subTabs)
            {
                _buttons.Remove(b);
                Object.Destroy(b.Rect.gameObject);
                if (_focused == b)
                    _focused = null;
                if (_pressed == b)
                    _pressed = null;
            }
            _subTabs.Clear();
            _subTabs.AddRange(BuildSubTabRow(labels, i => OnSubTab?.Invoke(i)));
        }
        foreach (var b in _subTabs)
            b.Rect.gameObject.SetActive(show);
        for (var i = 0; show && i < _subTabs.Count; i++)
            _subTabs[i].Selected = i == selected;

        if (_subTabsShown != show)
        {
            _subTabsShown = show;
            _layoutShown = -1; // re-layout
        }
    }

    public void SetEmptyMessage(string text)
    {
        if (_empty.text != text)
            _empty.text = text;
    }

    public void SetRows(IReadOnlyList<RowModel> models)
    {
        _models.Clear();
        _models.AddRange(models);
        Layout();
    }

    /// <summary>Back to the top of the page (after switching tabs).</summary>
    public void ResetScroll() => _scroll = 0;

    /// <summary>Scroll the page by whole rows; false if it can't go further that way.</summary>
    public bool ScrollBy(int rows)
    {
        var max = Math.Max(0, _models.Count - _visibleRows);
        var target = Math.Clamp(_scroll + rows, 0, max);
        if (target == _scroll)
            return false;
        _scroll = target;
        if (_holding != null)
            _holding.Progress = 0f;
        _holding = null; // the held button now shows another row
        Layout();
        return true;
    }

    /// <summary>Mouse wheel over this panel scrolls it.</summary>
    public void Wheel(Vector2 mouse, float delta)
    {
        if (delta != 0f && _panel != null && RectTransformUtility.RectangleContainsScreenPoint(_panel, mouse, null))
            ScrollBy(delta > 0f ? -1 : 1);
    }

    private void Layout()
    {
        var total = _models.Count;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - _visibleRows));
        var shown = Math.Min(total, _visibleRows);
        while (_rows.Count < shown)
            _rows.Add(BuildRow(_rows.Count));

        var rowsTop = BodyTop + (_subTabsShown ? TabStep : 0f);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var visible = i < shown;
            row.Rect.gameObject.SetActive(visible);
            if (!visible)
                continue;
            Place(row.Rect, ContentX - 4f, rowsTop + i * RowStep, RowWidth, RowHeight);
            FillRow(row, _models[_scroll + i]);
        }

        if (_layoutShown != shown || _layoutTotal != total)
        {
            _layoutShown = shown;
            _layoutTotal = total;
            _empty.gameObject.SetActive(total == 0);
            Place(_empty.rectTransform, ContentX + 4f, rowsTop, ContentWidth - 8f, RowHeight);
            // The body is as tall as the longer of the tab column and the page.
            var bodyBottom = Mathf.Max(SidebarBottom(), rowsTop + Math.Max(shown, 1) * RowStep - 2f);
            for (var i = 0; i < _versions.Count; i++)
                Place(_versions[i].rectTransform, Inset + 2f, bodyBottom - (_versions.Count - i) * VersionLineStep, SidebarWidth, VersionLineStep);
            _sideDivider.sizeDelta = new Vector2(1f, bodyBottom - BodyTop);
            var footerY = bodyBottom + 5f;
            Place(_footer.rectTransform, Inset + 4f, footerY, HeaderWidth - 8f, 16f);
            _panel.sizeDelta = new Vector2(PanelWidth, footerY + 16f + 11f);
        }

        // Scroll thumb: the visible share of the page, along the rows' height.
        var scrolls = total > _visibleRows;
        _scrollThumb.gameObject.SetActive(scrolls);
        if (scrolls)
        {
            var track = shown * RowStep - 2f;
            var length = Mathf.Max(6f, Mathf.Round(track * shown / total));
            var offset = Mathf.Round((track - length) * _scroll / (total - shown));
            Place(_scrollThumb, Inset + SidebarWidth + 3f, rowsTop + offset, 3f, length);
        }
    }

    private void FillRow(RowView row, RowModel m)
    {
        // Right to left: buttons, price, pips; the name gets what's left.
        var buttons = Math.Min(m.Buttons.Length, MaxButtons);
        var left = RowWidth - 4f;
        for (var i = MaxButtons - 1; i >= 0; i--)
        {
            var b = row.Buttons[i];
            var shown = i < buttons;
            b.Rect.gameObject.SetActive(shown);
            if (!shown)
                continue;
            var model = m.Buttons[i];
            var width = buttons == 1 ? 34f : 31f;
            left -= width + (i < buttons - 1 ? 2f : 0f);
            Place(b.Rect, left, 6f, width, 20f);
            b.Label.rectTransform.sizeDelta = new Vector2(width, 20f);
            b.Label.text = model.Label;
            b.Enabled = model.Enabled;
            b.HoldSeconds = model.HoldSeconds;
        }
        if (buttons > 0)
            left -= 4f;
        row.Key.enabled = buttons > 0;
        row.Item.Rect.gameObject.SetActive(buttons == 0);

        // Price: up to five coins like the in-game cost indicators, otherwise one coin and "x12".
        var hasPrice = m.Price.HasValue || m.PriceLabel != null;
        var coins = m.PriceLabel == null && m.Price is > 0 ? (m.Price.Value <= MaxCoins ? m.Price.Value : 1) : 0;
        if (hasPrice)
            left -= 36f;
        for (var i = 0; i < MaxCoins; i++)
        {
            var c = row.Coins[i];
            c.enabled = i < coins;
            if (!c.enabled)
                continue;
            c.rectTransform.anchoredPosition = new Vector2(left + i * 6f, -11f);
            c.color = c.sprite != null ? (m.CanAfford ? Color.white : CoinUnaffordable) : (m.CanAfford ? Gold : CoinUnaffordable);
        }
        row.PriceText.enabled = hasPrice;
        if (hasPrice)
        {
            var textX = coins == 1 ? left + 12f : left;
            Place(row.PriceText.rectTransform, textX, 0f, 36f, RowHeight);
            row.PriceText.text = m.PriceLabel ?? (m.Price == 0 ? "free" : m.Price > MaxCoins ? $"x{m.Price}" : "");
            row.PriceText.color = m.PriceLabel != null ? Gold : m.CanAfford ? Cream : Dim;
            left -= 4f;
        }

        // Pips (level or stock), or text when there would be too many.
        var usePips = m.Pips > 0 && m.Pips <= MaxPips && m.PipText == null;
        if (usePips || m.PipText != null)
            left -= 34f;
        for (var i = 0; i < MaxPips; i++)
        {
            var p = row.Pips[i];
            p.enabled = usePips && i < m.Pips;
            if (!p.enabled)
                continue;
            p.rectTransform.anchoredPosition = new Vector2(left + i * 4f, -12f);
            p.color = i < m.PipsFull ? (m.PipsGold ? Gold : Cream) : PipEmpty;
        }
        row.PipText.enabled = m.PipText != null;
        if (m.PipText != null)
        {
            Place(row.PipText.rectTransform, left, 0f, 34f, RowHeight);
            row.PipText.text = m.PipText;
        }
        if (usePips || m.PipText != null)
            left -= 4f;

        // Name and detail lines; the name is centred vertically when there's no detail.
        var nameWidth = Mathf.Max(20f, left - 44f);
        var twoLines = !string.IsNullOrEmpty(m.Detail);
        Place(row.Name.rectTransform, 44f, twoLines ? 1f : 8f, nameWidth, 16f);
        Place(row.Detail.rectTransform, 44f, 15f, nameWidth, 16f);
        Place(row.Right.rectTransform, 44f, twoLines ? 1f : 8f, left - 48f, 16f);
        row.Name.text = m.Name;
        row.Name.color = m.NameColor;
        row.Detail.text = m.Detail ?? "";
        row.Detail.color = m.DetailColor;
        row.Right.text = m.Right ?? "";
        row.Right.color = m.RightColor;

        // Icon: native pixel size when it fits (tiny sprites at 2x), otherwise shrunk to fit; very tall items laid diagonally.
        row.Icon.enabled = m.Icon != null;
        if (m.Icon != null && row.Icon.sprite != m.Icon)
        {
            row.Icon.sprite = m.Icon;
            var size = m.Icon.rect.size * (32f / m.Icon.pixelsPerUnit);
            var rotate = size.y > IconSlot && size.y >= size.x * 3f;
            float scale;
            if (rotate)
                scale = Mathf.Min(1f, IconSlot * 1.35f / size.y);
            else
            {
                var fit = Mathf.Min(IconSlot / size.x, (RowHeight - 4f) / size.y);
                scale = fit >= 1f ? Mathf.Clamp(Mathf.Floor(fit), 1f, 2f) : fit;
            }
            row.IconRect.sizeDelta = size * scale;
            row.IconRect.localRotation = Quaternion.Euler(0f, 0f, rotate ? -45f : 0f);
        }
    }

    /// <param name="highlight">A status message (gold).</param>
    /// <param name="tip">A row's tooltip (cream); otherwise the page's hint (dim).</param>
    public void SetFooter(string text, bool highlight, bool tip = false)
    {
        _footer.text = text;
        _footer.color = highlight ? Gold : tip ? Cream : Dim;
    }

    /// <summary>
    /// The tooltip of the row under the mouse, if the mouse moved last, or else of the row that has the focus (moved to
    /// with the keys or stick); null if that row has none.
    /// </summary>
    public string FocusedTip
    {
        get
        {
            var index = -1;
            if (_tipFromMouse)
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].Rect.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_rows[i].Rect, _lastMouse, null))
                        index = _scroll + i;
                }
            }
            else if (_focused != null && _focused.GridRow >= FirstRowGrid && IsVisible(_focused))
                index = _scroll + _focused.GridRow - FirstRowGrid;
            return index >= 0 && index < _models.Count ? _models[index].Tip : null;
        }
    }

    public void Tick(float time)
    {
        var frame = _art.Get(GameArt.CoinFrames[(int)(time * 10f) % GameArt.CoinFrames.Length]);
        if (frame != null)
            _purseCoin.sprite = frame;
    }

    // ------------------------------------------------------------------ input

    /// <summary>
    /// Hover/press visuals, clicks and hold-to-activate, using our own hit testing so no EventSystem is needed.
    /// Hold buttons must be held until they fill up (mouse, a number key, or submit on the focused one); letting go
    /// early cancels, and after one fires the input must be released before the next one starts.
    /// </summary>
    /// <param name="keyHold">Hold button held from the keyboard/controller this frame, or null.</param>
    /// <param name="keyClick">Ordinary button pressed from the keyboard/controller this frame, or null.</param>
    public void HandleInput(Vector2 mouse, bool mouseDown, bool mouseHeld, bool mouseUp, UiButton keyHold, UiButton keyClick, float deltaTime)
    {
        var clicked = keyClick != null && keyClick.Enabled && IsVisible(keyClick) ? keyClick : null;

        if (_focused != null && !IsVisible(_focused))
            _focused = Refocus(_focused);

        var moved = (mouse - _lastMouse).sqrMagnitude > 0.25f;
        if (moved && _lastMouse.x >= 0f)
            _tipFromMouse = true;
        _lastMouse = mouse;
        foreach (var b in _buttons)
        {
            b.Hovered = IsVisible(b) && RectTransformUtility.RectangleContainsScreenPoint(b.Rect, mouse, null);
            if (b.Hovered && (moved || mouseDown))
                _focused = b;
            if (mouseDown && b.Hovered && b.Enabled)
                _pressed = b;
            if (mouseUp && _pressed == b && b.Hovered && b.Enabled && !b.HoldToActivate)
                clicked = b;
        }

        if (mouseUp || !mouseHeld)
            _pressed = null;

        // Which hold button is being held this frame? Keyboard/controller wins over the mouse.
        UiButton holding = null;
        if (keyHold != null && keyHold.HoldToActivate && IsVisible(keyHold))
            holding = keyHold;
        else if (_pressed != null && _pressed.HoldToActivate && _pressed.Hovered)
            holding = _pressed;
        if (holding != null && !holding.Enabled)
            holding = null;

        if (holding != _holding)
        {
            if (_holding != null)
                _holding.Progress = 0f;
            _holding = holding;
            _holdConsumed = false;
        }

        if (_holding != null && !_holdConsumed)
        {
            _holding.Progress += deltaTime / Mathf.Max(0.05f, _holding.HoldSeconds);
            if (_holding.Progress >= 1f)
            {
                _holding.Progress = 0f;
                _holdConsumed = true;
                clicked = _holding;
            }
        }

        foreach (var row in _rows)
        {
            if (row.Rect.gameObject.activeSelf)
                row.Frame.enabled = _focused != null && _focused.GridRow == FirstRowGrid + _rows.IndexOf(row);
        }
        foreach (var b in _buttons)
            RefreshButton(b);

        clicked?.OnClick?.Invoke();
    }

    private void RefreshButton(UiButton b)
    {
        var focused = _focused == b;
        var down = b.Enabled && (b == _holding || (_pressed == b && b.Hovered));
        var sprite = !b.Enabled ? (focused ? b.Hover ?? b.Disabled : b.Disabled)
            : down ? b.Pressed
            : focused ? b.Hover
            : b.Selected ? b.Pressed
            : b.Normal;
        if (sprite != null && b.Background.sprite != sprite)
            b.Background.sprite = sprite;
        if (sprite == null)
            b.Background.color = !b.Enabled ? PipEmpty : focused ? Gold : ButtonFallback;
        b.Label.color = !b.Enabled ? Dim : b.Selected || focused ? Gold : Cream;

        // Push the label down a pixel while pressed (or selected, for tabs), like the game's buttons.
        b.Label.rectTransform.anchoredPosition = new Vector2(0f, down || b.Selected ? -1f : 0f);

        if (b.FillImage != null)
        {
            if (!b.Enabled)
                b.Progress = 0f;
            var full = b.Rect.sizeDelta - new Vector2(FillInset * 2f, FillInset * 2f);
            b.FillImage.enabled = b.Progress > 0f;
            b.FillRect.sizeDelta = new Vector2(Mathf.Round(full.x * Mathf.Clamp01(b.Progress)), full.y);
        }
    }

    // ------------------------------------------------------------------ keyboard / controller focus

    public UiButton Focused => _focused;

    /// <summary>A row's hold button (for the number keys), or null.</summary>
    public UiButton RowButton(int row, int col)
    {
        if (row < 0 || row >= _rows.Count || !_rows[row].Rect.gameObject.activeSelf)
            return null;
        var b = _rows[row].Buttons[col];
        return IsVisible(b) ? b : null;
    }

    /// <summary>Focus the first row's first button, or the selected tab when the page has none.</summary>
    public void FocusDefault()
    {
        _focused = RowButton(0, 0) ?? _tabs.Find(t => t.Selected) ?? (_tabs.Count > 0 ? _tabs[0] : null);
    }

    /// <summary>Focus the selected tab of the main row (after switching tabs from the keyboard or shoulder buttons).</summary>
    public void FocusSelectedTab(bool sub)
    {
        var list = sub && _subTabsShown ? _subTabs : _tabs;
        _focused = list.Find(t => t.Selected) ?? _focused;
    }

    public bool FocusOnTabs => _focused != null && _focused.IsTab;

    /// <summary>
    /// The focused button went away (its row scrolled to an item with fewer buttons, the page got shorter, a stand
    /// sold out): stay on that row if it's still there, else the nearest row above, so the focus doesn't jump to the top.
    /// </summary>
    private UiButton Refocus(UiButton lost)
    {
        for (var r = Math.Min(lost.GridRow - FirstRowGrid, _rows.Count - 1); r >= 0; r--)
        {
            var row = _rows[r];
            if (!row.Rect.gameObject.activeSelf)
                continue;
            foreach (var b in row.Buttons)
            {
                if (IsVisible(b))
                    return b;
            }
            if (IsVisible(row.Item))
                return row.Item;
        }
        return null;
    }

    /// <summary>
    /// Move focus one step by position on screen. Up/down in the page goes one line at a time (header buttons,
    /// sub-tabs, each row) to the button nearest across; up/down in the tab column, and left/right, go to the nearest
    /// visible button in that direction (within 45 degrees, preferring ones straight ahead), so left from a row goes back
    /// to the tabs and right from the tabs goes into the page. Moving onto a tab selects it, like the game's menus;
    /// arriving at a group of tabs from elsewhere lands on its selected tab.
    /// </summary>
    public void MoveFocus(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return;
        _tipFromMouse = false;
        if (_focused == null || !IsVisible(_focused))
        {
            FocusDefault();
            return;
        }

        if (dy != 0 && _focused.GridRow >= FirstRowGrid)
        {
            var viewRow = _focused.GridRow - FirstRowGrid;
            var lastRow = Math.Min(_models.Count, _visibleRows) - 1;
            if ((dy > 0 && viewRow == lastRow) || (dy < 0 && viewRow == 0))
            {
                if (ScrollBy(dy))
                    return;
            }
        }

        // Up/down in the page: line by line. (A direction search can't reach the rows from a sub-tab on the left: the
        // rows' buttons sit at the far right, further to the side than below.)
        if (dy != 0 && !_tabs.Contains(_focused))
        {
            var next = NextLine(dy);
            if (next != null)
                Land(next);
            return;
        }

        var from = Center(_focused);
        var dir = new Vector2(dx, -dy); // screen y grows upwards; dy +1 means down
        UiButton best = null;
        var bestCost = float.MaxValue;
        foreach (var b in _buttons)
        {
            if (b == _focused || !IsVisible(b))
                continue;
            var d = Center(b) - from;
            var ahead = Vector2.Dot(d, dir);
            var side = Mathf.Abs(dir.x != 0 ? d.y : d.x);
            if (ahead <= 0.5f || side > ahead)
                continue;
            var cost = ahead + side * 2f;
            if (cost < bestCost)
            {
                best = b;
                bestCost = cost;
            }
        }

        if (best != null)
            Land(best);
    }

    /// <summary>The next line of page buttons above (dy -1) or below (dy +1) the focused one, at the button nearest across.</summary>
    private UiButton NextLine(int dy)
    {
        var from = Center(_focused);
        var sameLine = 4f * _scaler.scaleFactor; // a line's buttons share a centre height
        var nearest = float.MaxValue;
        foreach (var b in _buttons)
        {
            var ahead = (from.y - Center(b).y) * dy; // screen y grows upwards
            if (IsPageButton(b) && ahead > sameLine)
                nearest = Mathf.Min(nearest, ahead);
        }

        UiButton best = null;
        var bestSide = float.MaxValue;
        foreach (var b in _buttons)
        {
            if (!IsPageButton(b))
                continue;
            var c = Center(b);
            var ahead = (from.y - c.y) * dy;
            var side = Mathf.Abs(c.x - from.x);
            if (ahead > sameLine && ahead <= nearest + sameLine && side < bestSide)
            {
                best = b;
                bestSide = side;
            }
        }
        return best;
    }

    private bool IsPageButton(UiButton b) => b != _focused && IsVisible(b) && !_tabs.Contains(b);

    private void Land(UiButton best)
    {
        if (best.IsTab && best.GridRow != _focused.GridRow)
            best = _buttons.Find(b => b.IsTab && b.GridRow == best.GridRow && b.Selected && IsVisible(b)) ?? best;
        _focused = best;
        if (best.IsTab && !best.Selected)
            best.OnClick?.Invoke();
    }

    private static bool IsVisible(UiButton b) => b != null && b.Rect.gameObject.activeInHierarchy;

    private static Vector2 Center(UiButton b) => b.Rect.TransformPoint(b.Rect.rect.center);

    // ------------------------------------------------------------------ helpers

    private UiButton AddButton(Transform parent, string name, float x, float y, float w, float h, string label, Action onClick, bool hold = false)
    {
        var rect = NewRect(name, parent, x, y, w, h);
        var b = new UiButton
        {
            Rect = rect,
            Normal = _art.Get("menu_button_wood_normal"),
            Hover = _art.Get("menu_button_wood_highlight"),
            Pressed = _art.Get("menu_button_wood_pressed"),
            Disabled = _art.Get("menu_button_wood_disabled"),
            OnClick = onClick,
            HoldToActivate = hold,
        };
        b.Background = AddImage(rect, b.Normal, true, b.Normal != null ? Color.white : ButtonFallback);

        if (hold)
        {
            // Grows from the left edge inside the wooden frame while the button is held.
            b.FillRect = NewRect("HoldFill", rect, FillInset, FillInset, 0f, h - FillInset * 2f);
            b.FillImage = AddImage(b.FillRect, null, false, HoldFill);
            b.FillImage.enabled = false;
        }

        b.Label = AddText(NewRect("Label", rect, 0f, 0f, w, h), _art.Body, label, Cream, TextAnchor.MiddleCenter);

        _buttons.Add(b);
        return b;
    }

    internal static RectTransform NewRect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name);
        go.layer = 5; // UI
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        Place(rt, x, y, w, h);
        return rt;
    }

    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    internal static Image AddImage(RectTransform rt, Sprite sprite, bool sliced, Color fallbackColor)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.sprite = sprite;
        img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = sprite != null ? Color.white : fallbackColor;
        return img;
    }

    internal static Text AddText(RectTransform rt, Font font, string text, Color color, TextAnchor anchor)
    {
        var t = rt.gameObject.AddComponent<Text>();
        t.raycastTarget = false;
        t.font = font;
        t.fontSize = 16; // the game's pixel fonts are drawn at 16 -> 1 font pixel = 1 art pixel
        t.supportRichText = false;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;

        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = TextShadow;
        shadow.effectDistance = new Vector2(1f, -1f);
        return t;
    }
}
