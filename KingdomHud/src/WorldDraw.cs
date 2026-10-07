using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomHud;

/// <summary>
/// Helpers for things drawn inside the game world (not on the screen overlay), so they render through the game's own
/// low-resolution pixel pipeline, follow the right camera in split-screen, and look like the game's own markers.
/// </summary>
internal static class WorldDraw
{
    public const float PixelsPerUnit = 32f; // the game's sprites are 32 px per world unit

    private static Sprite _pixel;
    private static TextMesh _textTemplate;
    private static bool _searchedTemplate;

    public static float Snap(float v) => Mathf.Round(v * PixelsPerUnit) / PixelsPerUnit;

    /// <summary>A 1x1 white sprite at the game's pixel density: scale it to N x M to get an N x M pixel rectangle.</summary>
    public static Sprite Pixel
    {
        get
        {
            if (_pixel != null)
                return _pixel;
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            _pixel.hideFlags = HideFlags.HideAndDontSave;
            return _pixel;
        }
    }

    /// <summary>Copy sorting, layer and material from a game renderer so our objects draw in the same place.</summary>
    public static void MatchRenderer(Renderer ours, Renderer game, int orderOffset)
    {
        if (ours == null || game == null)
            return;
        ours.gameObject.layer = game.gameObject.layer;
        ours.sortingLayerID = game.sortingLayerID;
        ours.sortingOrder = game.sortingOrder + orderOffset;
    }

    /// <summary>
    /// One of the game's own in-world hint texts (InfoText / GhostHintObjectText), used as a template so our labels get
    /// the same font, size, material and sorting. Null if none is loaded.
    /// </summary>
    public static TextMesh TextTemplate
    {
        get
        {
            if (_searchedTemplate && _textTemplate != null)
                return _textTemplate;
            _searchedTemplate = true;

            foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<GhostHintObjectText>()))
            {
                var hint = obj.TryCast<GhostHintObjectText>();
                if (hint != null && hint.textMesh != null && hint.textMesh.font != null)
                    return _textTemplate = hint.textMesh;
            }
            foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<InfoText>()))
            {
                var info = obj.TryCast<InfoText>();
                if (info != null && info._label != null && info._label.font != null)
                    return _textTemplate = info._label;
            }
            return null;
        }
    }

    public static GameObject NewObject(string name)
    {
        var go = new GameObject(name);
        Object.DontDestroyOnLoad(go);
        return go;
    }
}

/// <summary>A small stamina bar drawn in the world just under a player's mount.</summary>
internal class StaminaBar
{
    private const int Width = 22;   // inner width in game pixels
    private const int Height = 2;   // inner height in game pixels

    private static readonly Color Back = new(0.07f, 0.05f, 0.04f, 0.9f);
    private static readonly Color High = new(0.56f, 0.80f, 0.38f);
    private static readonly Color Mid = new(0.98f, 0.73f, 0.27f);
    private static readonly Color Low = new(0.88f, 0.30f, 0.20f);
    private static readonly Color WellFed = new(1f, 0.86f, 0.36f);

    private readonly GameObject _root;
    private readonly SpriteRenderer _back;
    private readonly SpriteRenderer _fill;
    private float _alpha;
    private float _fullSince = -1f;

    public StaminaBar(string name)
    {
        _root = WorldDraw.NewObject(name);
        _back = AddPart("Back");
        _fill = AddPart("Fill");
        _back.transform.localScale = new Vector3(Width + 2, Height + 2, 1f);
        _fill.transform.localPosition = new Vector3(1f / WorldDraw.PixelsPerUnit, 0f, 0f);
        _root.SetActive(false);
    }

    private SpriteRenderer AddPart(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = WorldDraw.Pixel;
        return sr;
    }

    public void Hide()
    {
        _alpha = 0f;
        _fullSince = -1f;
        if (_root.activeSelf)
            _root.SetActive(false);
    }

    private string _lastTuning;
    private float _nextTuningCheck;

    /// <summary>
    /// Log the mount's real stamina tuning (per second; the game does Stamina += rate * dt and clamps to 0..1) whenever
    /// it changes, e.g. on a new mount or after a Kingdom Menu steed stamina upgrade, so the bar's behaviour can be checked.
    /// </summary>
    private void LogTuning(Steed steed, float time)
    {
        if (time < _nextTuningCheck)
            return;
        _nextTuningCheck = time + 2f;

        var gallop = steed.runStaminaRate;
        var text = $"Mount '{Names.SteedName(steed.steedType)}' stamina per second: gallop {gallop:+0.###;-0.###}, " +
                   $"walk {steed.walkStaminaRate:+0.###;-0.###}, stand {steed.standStaminaRate:+0.###;-0.###}, glide {steed.glideStaminaRate:+0.###;-0.###}" +
                   (gallop < 0f ? $" (full gallop lasts {1f / -gallop:0.#} s)" : "") +
                   $"; second wind {steed.reserveProbability * 100f:0}% chance, refills to {Mathf.Min(1f, steed.reserveStamina) * 100f:0}%" +
                   $"; well fed {steed.wellFedDuration:0.#} s; tired {steed.tiredDuration:0.#} s";
        if (text == _lastTuning)
            return;
        _lastTuning = text;
        Plugin.Logger.LogInfo(text);
    }

    private string _lastDiag;
    private Steed _detailsFor;
    private Steed _cachedSteed;
    private SpriteRenderer _cachedRenderer;

    /// <summary>The sprite renderer that draws the ridden mount: the steed's own (root first), else the player's mount slot.</summary>
    private SpriteRenderer MountRenderer(Player player, Steed steed)
    {
        if (steed == null)
            return null;
        if (steed == _cachedSteed && _cachedRenderer != null)
            return _cachedRenderer;

        _cachedSteed = steed;
        _cachedRenderer = steed.GetComponent<SpriteRenderer>();
        if (_cachedRenderer == null)
        {
            // Largest child renderer is the mount's body (others are saddles, rider visuals, effects).
            float best = 0f;
            foreach (var sr in steed.GetComponentsInChildren<SpriteRenderer>())
            {
                var size = sr.bounds.size;
                if (size.x * size.y > best)
                {
                    best = size.x * size.y;
                    _cachedRenderer = sr;
                }
            }
        }
        _cachedRenderer ??= player._steedRenderer;
        return _cachedRenderer;
    }

    private void Diag(string message)
    {
        if (message == _lastDiag)
            return;
        _lastDiag = message;
        Plugin.Logger.LogDebug($"Stamina bar: {message}");
    }

    public void Update(Player player, float time, float deltaTime)
    {
        var steed = player != null ? player.steed : null;
        var renderer = MountRenderer(player, steed);
        if (steed == null || renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            Diag(steed == null ? "no mount" : renderer == null ? "no mount renderer" : "mount renderer hidden");
            Hide();
            return;
        }

        LogTuning(steed, time);

        var stamina = Mathf.Clamp01(steed.Stamina);
        var tired = steed.IsTired;
        // After grazing the mount is "well fed": the game skips the stamina drain until WellFedTimer runs out
        // (Player.UpdateActionState), so the bar shows that countdown in gold instead.
        var wellFed = steed.WellFedTimer > 0f;
        var wellFedLeft = wellFed && steed.wellFedDuration > 0f ? Mathf.Clamp01(steed.WellFedTimer / steed.wellFedDuration) : 0f;

        // Fade out once the horse has been fully rested for a while; any use of stamina brings the bar back.
        var target = 1f;
        if (Plugin.AutoHideStaminaBar.Value && stamina >= 0.999f && !tired && !wellFed)
        {
            if (_fullSince < 0f)
                _fullSince = time;
            if (time - _fullSince > 2.5f)
                target = 0f;
        }
        else
            _fullSince = -1f;

        _alpha = Mathf.MoveTowards(_alpha, target, deltaTime * 3f);
        if (_alpha <= 0f)
        {
            if (_root.activeSelf)
                _root.SetActive(false);
            return;
        }
        if (!_root.activeSelf)
            _root.SetActive(true);

        // Same layer and sorting layer as the mount (so the right camera draws it). The mount's own material is a
        // palette-recolor shader, so the bar keeps the default sprite material to show its real colours.
        WorldDraw.MatchRenderer(_back, renderer, 0);
        WorldDraw.MatchRenderer(_fill, renderer, 0);
        _back.sortingOrder = renderer.sortingOrder + 1;
        _fill.sortingOrder = renderer.sortingOrder + 2;

        // Centered on the mount, just under its hooves (the transform sits at the feet; the sprite bounds include
        // transparent padding), snapped to the game's pixel grid.
        var feet = steed.transform.position;
        var px = 1f / WorldDraw.PixelsPerUnit;
        var x = WorldDraw.Snap(renderer.bounds.center.x - (Width + 2) * px / 2f);
        var y = WorldDraw.Snap(feet.y - 3f * px);
        // The game layers its world by depth (z), not sorting order: the ground strip sits at z ~0, in front of the
        // mount, so put the bar just in front of the ground.
        _root.transform.position = new Vector3(x, y, Mathf.Min(feet.z, 0f) - 0.05f);

        if (_detailsFor != steed)
        {
            _detailsFor = steed;
            Diag($"showing under '{renderer.name}' (layer {renderer.gameObject.layer}, sorting '{SortingLayer.IDToName(renderer.sortingLayerID)}' " +
             $"#{renderer.sortingOrder}, mount mat '{(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none")}', " +
             $"bar mat '{(_back.sharedMaterial != null ? _back.sharedMaterial.name : "none")}', feet y {feet.y:F2}, bounds y {renderer.bounds.min.y:F2})");
        }

        var filled = Mathf.Round((wellFed ? wellFedLeft : stamina) * Width);
        _fill.transform.localScale = new Vector3(filled, Height, 1f);
        _fill.enabled = filled > 0f;

        Color color;
        if (wellFed)
            color = wellFedLeft > 0.15f || Mathf.Repeat(time, 0.4f) < 0.2f ? WellFed : Back; // gold countdown, blinks near the end
        else if (tired)
            color = Mathf.Repeat(time, 0.6f) < 0.3f ? Low : Back; // blink while exhausted
        else
            color = stamina >= 0.5f ? High : stamina >= 0.25f ? Mid : Low;

        color.a *= _alpha;
        _fill.color = color;
        var back = Back;
        back.a *= _alpha;
        _back.color = back;
    }
}

/// <summary>An in-world text label (with the game's drop shadow style) shown above the thing a player has selected.</summary>
internal class NearbyLabel
{
    private static readonly Color TextColor = new(0.96f, 0.91f, 0.78f);
    private static readonly Color ShadowColor = new(0.07f, 0.04f, 0.03f, 0.85f);

    private readonly GameObject _root;
    private readonly TextMesh _text;
    private readonly TextMesh _shadow;
    private readonly MeshRenderer _textRenderer;
    private readonly MeshRenderer _shadowRenderer;
    private float _alpha;
    private Payable _current;
    private string _currentText;
    private bool _calibrated;

    public NearbyLabel(string name)
    {
        _root = WorldDraw.NewObject(name);
        (_shadow, _shadowRenderer) = AddText("Shadow");
        (_text, _textRenderer) = AddText("Text");
        _shadow.transform.localPosition = new Vector3(1f / WorldDraw.PixelsPerUnit, -1f / WorldDraw.PixelsPerUnit, 0f);
        _root.SetActive(false);
    }

    private (TextMesh, MeshRenderer) AddText(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        var tm = go.AddComponent<TextMesh>();
        var mr = go.GetComponent<MeshRenderer>();
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;

        var template = WorldDraw.TextTemplate;
        if (template != null)
        {
            tm.font = template.font;
            tm.fontSize = template.fontSize;
            tm.characterSize = template.characterSize;
            var templateRenderer = template.GetComponent<MeshRenderer>();
            if (templateRenderer != null)
                mr.sharedMaterial = templateRenderer.sharedMaterial;
        }
        else
        {
            // Fallback: the game's pixel font at 1 font pixel = 1 game pixel.
            tm.font = Hud.Art?.Body;
            tm.fontSize = 16;
            tm.characterSize = 0.1f; // calibrated to the game's pixel size on first display
            if (tm.font != null)
                mr.sharedMaterial = tm.font.material;
        }
        return (tm, mr);
    }

    public void Hide()
    {
        _alpha = 0f;
        _current = null;
        if (_root.activeSelf)
            _root.SetActive(false);
    }

    private void SetLayer(int layer)
    {
        _root.layer = layer;
        _text.gameObject.layer = layer;
        _shadow.gameObject.layer = layer;
    }

    /// <summary>
    /// Without a game template, scale the text so one line is as tall as the game's own pixel font line (17 game pixels
    /// at size 16), i.e. one font pixel = one game pixel.
    /// </summary>
    private void CalibrateFallbackSize()
    {
        if (_calibrated)
            return;
        var height = _textRenderer.bounds.size.y / Mathf.Max(0.0001f, _root.transform.localScale.y);
        if (height <= 0f)
            return;
        var scale = 17f / WorldDraw.PixelsPerUnit / height;
        _root.transform.localScale = new Vector3(scale, scale, 1f);
        _calibrated = true;
    }

    public void Update(Player player, float deltaTime)
    {
        var payable = player != null ? player.selectedPayable : null;
        if (payable == null || !payable.isActiveAndEnabled)
        {
            _alpha = Mathf.MoveTowards(_alpha, 0f, deltaTime * 4f);
            if (_alpha <= 0f)
            {
                _current = null;
                if (_root.activeSelf)
                    _root.SetActive(false);
                return;
            }
        }
        else
        {
            if (payable != _current)
            {
                _current = payable;
                _currentText = Names.For(payable);
                Plugin.Logger.LogDebug($"Near: {payable.GetIl2CppType().Name} '{payable.gameObject.name}' -> '{_currentText}'");
                _text.text = _currentText;
                _shadow.text = _currentText;
                _alpha = 0f;
            }
            _alpha = Mathf.MoveTowards(_alpha, 1f, deltaTime * 4f);
        }

        if (_current == null || string.IsNullOrEmpty(_currentText))
        {
            Hide();
            return;
        }
        if (!_root.activeSelf)
            _root.SetActive(true);

        // Sit just above the coin indicators the game shows over the selected payable.
        var payableRenderer = _current.GetComponentInChildren<SpriteRenderer>();
        var anchor = _current.GetCurrencyIndicatorPosition();
        var px = 1f / WorldDraw.PixelsPerUnit;
        _root.transform.position = new Vector3(WorldDraw.Snap(_current.transform.position.x), WorldDraw.Snap(anchor.y + 10f * px),
            Mathf.Min(_current.transform.position.z, 0f) - 0.3f); // in front of the payable and the ground (depth-sorted)

        var template = WorldDraw.TextTemplate;
        var templateRenderer = template != null ? template.GetComponent<MeshRenderer>() : null;
        if (templateRenderer != null)
        {
            _textRenderer.sortingLayerID = templateRenderer.sortingLayerID;
            _textRenderer.sortingOrder = templateRenderer.sortingOrder + 1;
            _shadowRenderer.sortingLayerID = templateRenderer.sortingLayerID;
            _shadowRenderer.sortingOrder = templateRenderer.sortingOrder;
            SetLayer(templateRenderer.gameObject.layer);
            _root.transform.localScale = template.transform.lossyScale;
        }
        else if (payableRenderer != null)
        {
            WorldDraw.MatchRenderer(_shadowRenderer, payableRenderer, 60);
            WorldDraw.MatchRenderer(_textRenderer, payableRenderer, 61);
            SetLayer(payableRenderer.gameObject.layer);
            CalibrateFallbackSize();
        }

        var c = TextColor;
        c.a = _alpha;
        _text.color = c;
        var s = ShadowColor;
        s.a *= _alpha;
        _shadow.color = s;
    }
}
