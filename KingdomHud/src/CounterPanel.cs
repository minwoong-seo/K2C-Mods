using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KingdomHud;

internal class ColumnData
{
    public Sprite Icon;
    public int Count;
    /// <summary>Capacity shown as "count/max" (e.g. farmers vs. farm slots), or null.</summary>
    public int? Max;
}

/// <summary>One text line under the unit row, optionally with a small icon in front.</summary>
internal class InfoLine
{
    public Sprite Icon;
    public Color IconTint = Color.white;
    public string Text;
    public Color Color;

    // Optional extras, drawn after the text in this order:
    /// <summary>A small icon + text in gold, e.g. the sun and "+3" for tomorrow's bank interest.</summary>
    public Sprite BadgeIcon;
    public string Badge;
    public Color BadgeColor = CounterPanel.Gold;
    /// <summary>Pips, e.g. one per cottage slot with the ready ones full.</summary>
    public int Pips;
    public int PipsFull;
    /// <summary>A small progress bar (0..1), or -1 for none.</summary>
    public float Bar = -1f;
    public Color BarColor = CounterPanel.Gold;
    /// <summary>Text at the end, dim unless coloured, e.g. "98s".</summary>
    public string Note;
    public Color NoteColor = CounterPanel.Dim;
}

/// <summary>
/// Screen overlay in the game's menu style: a row of unit sprites with counts, plus info lines (purse, bank,
/// cottages, blood moon).
/// Layout is in art pixels (1 unit = 1 sprite pixel) and the canvas scales by a whole number to keep pixels crisp.
/// </summary>
internal class CounterPanel
{
    private class ColumnView
    {
        public RectTransform Rect;
        public RectTransform IconRect;
        public Image Icon;
        public Text Count;
    }

    private class LineView
    {
        public RectTransform Rect;
        public RectTransform IconRect;
        public Image Icon;
        public Text Text;
        public RectTransform BadgeIconRect;
        public Image BadgeIcon;
        public Text Badge;
        public readonly Image[] Pips = new Image[MaxPips];
        public RectTransform BarRect;
        public Image BarFill;
        public Text Note;
    }

    private const int MaxPips = 8;
    private const float IconBox = 12f;
    private const float BarWidth = 26f;
    private static readonly Color PipEmpty = new(0.09f, 0.06f, 0.05f, 0.9f);
    private static readonly Color BarBack = new(0.09f, 0.06f, 0.05f, 0.9f);

    private const float Pad = 11f;
    private const float ColWidth = 24f;
    private const float ColGap = 2f;
    private const float IconHeight = 30f;
    private const float CountHeight = 13f;
    private const float LineHeight = 14f;

    internal static readonly Color Cream = new(0.96f, 0.91f, 0.78f);
    internal static readonly Color Gold = new(1f, 0.82f, 0.36f);
    internal static readonly Color Dim = new(0.62f, 0.55f, 0.46f);
    internal static readonly Color Danger = new(0.95f, 0.38f, 0.28f);
    private static readonly Color TextShadow = new(0.07f, 0.04f, 0.03f, 0.9f);
    private static readonly Color PanelFallback = new(0.17f, 0.11f, 0.10f, 0.92f);

    private readonly GameArt _art;
    private readonly List<ColumnView> _columns = new();
    private GameObject _root;
    private CanvasScaler _scaler;
    private RectTransform _panel;
    private readonly List<LineView> _lines = new();

    public CounterPanel(GameArt art)
    {
        _art = art;
    }

    public bool Alive => _root != null;

    public void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    public void SetScale(int scale)
    {
        if (_scaler != null && !Mathf.Approximately(_scaler.scaleFactor, scale))
            _scaler.scaleFactor = scale;
    }

    public void Build()
    {
        _root = new GameObject("KingdomHudCanvas");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000; // under the Kingdom Menu (32000)
        canvas.pixelPerfect = true;

        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.referencePixelsPerUnit = 32f;
        _scaler.scaleFactor = 2f;

        _panel = NewRect("Panel", _root.transform, 0f, 0f, 100f, 60f);
        var bg = AddImage(_panel, _art.Get("menu_panel_background"), true, PanelFallback);
        bg.color = new Color(1f, 1f, 1f, 0.92f);
    }

    private LineView BuildLine(int index)
    {
        var line = new LineView { Rect = NewRect($"Line{index}", _panel, Pad, 0f, 240f, LineHeight) };
        line.IconRect = NewRect("Icon", line.Rect, 1f, 2f, 10f, 10f);
        line.Icon = AddImage(line.IconRect, null, false, Color.white);
        line.Icon.preserveAspect = true;
        line.Text = AddText(NewRect("Text", line.Rect, 0f, 0f, 230f, LineHeight), _art.Body, "", Cream, TextAnchor.MiddleLeft);
        line.BadgeIconRect = NewRect("BadgeIcon", line.Rect, 0f, 2f, 10f, 10f);
        line.BadgeIcon = AddImage(line.BadgeIconRect, null, false, Color.white);
        line.BadgeIcon.preserveAspect = true;
        line.Badge = AddText(NewRect("Badge", line.Rect, 0f, 0f, 60f, LineHeight), _art.Body, "", Gold, TextAnchor.MiddleLeft);
        for (var i = 0; i < MaxPips; i++)
            line.Pips[i] = AddImage(NewRect($"Pip{i}", line.Rect, 0f, 4f, 3f, 6f), null, false, PipEmpty);
        line.BarRect = NewRect("Bar", line.Rect, 0f, 5f, BarWidth, 4f);
        AddImage(line.BarRect, null, false, BarBack);
        line.BarFill = AddImage(NewRect("Fill", line.BarRect, 1f, 1f, 0f, 2f), null, false, Gold);
        line.Note = AddText(NewRect("Note", line.Rect, 0f, 0f, 60f, LineHeight), _art.Body, "", Dim, TextAnchor.MiddleLeft);
        return line;
    }

    /// <summary>Show a sprite in a small square slot: native size if it fits, otherwise scaled down to fit (aspect kept).</summary>
    private static float FitIcon(RectTransform rect, Image image, Sprite sprite, float x)
    {
        if (image.sprite != sprite)
            image.sprite = sprite;
        var size = sprite.rect.size * (32f / sprite.pixelsPerUnit);
        var scale = Mathf.Min(1f, IconBox / Mathf.Max(size.x, size.y));
        if (scale < 1f)
            scale = 1f / Mathf.Ceil(1f / scale); // whole-number shrink (1/2, 1/3...) keeps the pixel art even
        size = new Vector2(Mathf.Round(size.x * scale), Mathf.Round(size.y * scale));
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(x, -Mathf.Floor((LineHeight - size.y) / 2f));
        return size.x;
    }

    private ColumnView BuildColumn(int index)
    {
        var col = new ColumnView { Rect = NewRect($"Col{index}", _panel, 0f, 0f, ColWidth, IconHeight + CountHeight) };

        // Clip tall sprites (e.g. a pikeman's pike) to the slot so every column is the same height.
        var slot = NewRect("Slot", col.Rect, 0f, 0f, ColWidth, IconHeight);
        slot.gameObject.AddComponent<RectMask2D>();

        col.IconRect = NewRect("Icon", slot, 0f, 0f, 16f, 16f);
        col.IconRect.anchorMin = new Vector2(0.5f, 0f);
        col.IconRect.anchorMax = new Vector2(0.5f, 0f);
        col.IconRect.pivot = new Vector2(0.5f, 0f);
        col.IconRect.anchoredPosition = Vector2.zero;
        col.Icon = AddImage(col.IconRect, null, false, Color.white);

        col.Count = AddText(NewRect("Count", col.Rect, 0f, IconHeight, ColWidth, CountHeight), _art.Body, "", Cream, TextAnchor.MiddleCenter);
        return col;
    }

    public void SetData(IReadOnlyList<ColumnData> columns, bool showUnits, IReadOnlyList<InfoLine> lines)
    {
        while (_columns.Count < columns.Count)
            _columns.Add(BuildColumn(_columns.Count));

        var visibleCols = showUnits ? columns.Count : 0;
        for (var i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            var visible = i < visibleCols;
            col.Rect.gameObject.SetActive(visible);
            if (!visible)
                continue;

            Place(col.Rect, Pad + i * (ColWidth + ColGap), Pad - 2f, ColWidth, IconHeight + CountHeight);
            var max = columns[i].Max;
            col.Count.text = max.HasValue ? $"{columns[i].Count}/{max.Value}" : columns[i].Count.ToString();
            col.Count.color = columns[i].Count > 0 ? Cream : Dim;

            var sprite = columns[i].Icon;
            col.Icon.enabled = sprite != null;
            if (sprite != null && col.Icon.sprite != sprite)
            {
                col.Icon.sprite = sprite;
                col.IconRect.sizeDelta = sprite.rect.size * (32f / sprite.pixelsPerUnit); // native pixel size
            }
        }

        var y = Pad - 2f + (visibleCols > 0 ? IconHeight + CountHeight + 2f : 0f);
        var width = visibleCols > 0 ? visibleCols * ColWidth + (visibleCols - 1) * ColGap : 0f;

        while (_lines.Count < lines.Count)
            _lines.Add(BuildLine(_lines.Count));
        for (var i = 0; i < _lines.Count; i++)
        {
            var view = _lines[i];
            var visible = i < lines.Count;
            view.Rect.gameObject.SetActive(visible);
            if (!visible)
                continue;

            var line = lines[i];
            var x = 0f;

            // Icon, then text, then whatever extras the line has, left to right.
            view.Icon.enabled = line.Icon != null;
            if (line.Icon != null)
            {
                x = FitIcon(view.IconRect, view.Icon, line.Icon, 1f) + 5f;
                view.Icon.color = line.IconTint;
            }

            view.Text.text = line.Text ?? "";
            view.Text.color = line.Color;
            view.Text.rectTransform.anchoredPosition = new Vector2(x, 0f);
            if (!string.IsNullOrEmpty(line.Text))
                x += Mathf.Ceil(view.Text.preferredWidth) + 5f;

            view.BadgeIcon.enabled = line.BadgeIcon != null;
            if (line.BadgeIcon != null)
                x += FitIcon(view.BadgeIconRect, view.BadgeIcon, line.BadgeIcon, x) + 2f;
            view.Badge.text = line.Badge ?? "";
            view.Badge.color = line.BadgeColor;
            view.Badge.rectTransform.anchoredPosition = new Vector2(x, 0f);
            if (!string.IsNullOrEmpty(line.Badge))
                x += Mathf.Ceil(view.Badge.preferredWidth) + 5f;

            var pips = Mathf.Min(line.Pips, MaxPips);
            for (var p = 0; p < MaxPips; p++)
            {
                var pip = view.Pips[p];
                pip.enabled = p < pips;
                if (!pip.enabled)
                    continue;
                pip.rectTransform.anchoredPosition = new Vector2(x + p * 5f, -4f);
                pip.color = p < line.PipsFull ? Cream : PipEmpty;
            }
            if (pips > 0)
                x += pips * 5f + 3f;

            view.BarRect.gameObject.SetActive(line.Bar >= 0f);
            if (line.Bar >= 0f)
            {
                view.BarRect.anchoredPosition = new Vector2(x, -5f);
                view.BarFill.rectTransform.sizeDelta = new Vector2(Mathf.Round((BarWidth - 2f) * Mathf.Clamp01(line.Bar)), 2f);
                view.BarFill.color = line.BarColor;
                x += BarWidth + 5f;
            }

            view.Note.text = line.Note ?? "";
            view.Note.color = line.NoteColor;
            view.Note.rectTransform.anchoredPosition = new Vector2(x, 0f);
            if (!string.IsNullOrEmpty(line.Note))
                x += Mathf.Ceil(view.Note.preferredWidth) + 5f;

            view.Rect.anchoredPosition = new Vector2(Pad, -y);
            width = Mathf.Max(width, x - 5f);
            y += LineHeight;
        }

        var empty = width <= 0f;
        _panel.gameObject.SetActive(!empty);
        if (empty)
            return;

        _panel.sizeDelta = new Vector2(Mathf.Ceil(width) + Pad * 2f, Mathf.Ceil(y) + Pad - 2f);
    }

    /// <summary>
    /// Put the panel in a corner of a screen region (screen pixels from the top-left corner), e.g. one player's half in
    /// split screen, at a whole-number scale, made smaller if it wouldn't fit the region's width.
    /// </summary>
    public void Place(float regionLeft, float regionTop, float regionWidth, float regionHeight, int scale, Plugin.Corner corner)
    {
        while (scale > 1 && _panel.sizeDelta.x * scale + 12f > regionWidth)
            scale--;
        SetScale(scale);
        var right = corner is Plugin.Corner.TopRight or Plugin.Corner.BottomRight;
        var top = corner is Plugin.Corner.TopLeft or Plugin.Corner.TopRight;
        // Anchored to the canvas's top-left; the pivot is the panel's corner nearest the region's corner.
        _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
        _panel.pivot = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        var x = right ? (regionLeft + regionWidth) / scale - 6f : regionLeft / scale + 6f;
        var y = top ? -(regionTop / scale + 6f) : -((regionTop + regionHeight) / scale - 6f);
        _panel.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
    }

    // ------------------------------------------------------------------ helpers

    private static RectTransform NewRect(string name, Transform parent, float x, float y, float w, float h)
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

    private static Image AddImage(RectTransform rt, Sprite sprite, bool sliced, Color fallbackColor)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.sprite = sprite;
        img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = sprite != null ? Color.white : fallbackColor;
        return img;
    }

    private static Text AddText(RectTransform rt, Font font, string text, Color color, TextAnchor anchor)
    {
        var t = rt.gameObject.AddComponent<Text>();
        t.raycastTarget = false;
        t.font = font;
        t.fontSize = 16; // the game's pixel fonts at 16 -> 1 font pixel = 1 art pixel
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
