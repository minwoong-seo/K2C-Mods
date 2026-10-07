using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KingdomMenu;

/// <summary>
/// The "updates available" popup: once the background check (see Updates) finds newer versions of installed mods, it
/// shows them in the middle of the screen a few seconds into play, with UPDATE and NOT NOW. Player 1 answers it with
/// their keyboard keys, controller (A / B, left/right) or the mouse; their monarch doesn't move while it's up. UPDATE
/// downloads and installs them, then says to restart the game. NOT NOW asks again on the next start.
/// </summary>
internal sealed class UpdatePrompt
{
    private enum State { Checking, Waiting, Asking, Installing, Done, Closed }

    private const float ShowAfterPlayingSeconds = 3f;
    /// <summary>Keys and buttons are ignored this long after it appears, so a press meant for the game can't answer it.</summary>
    private const float InputDelaySeconds = 0.5f;
    private const float Width = 250f;
    private const float Inset = 13f;
    private const float LineStep = 18f;
    private static readonly Color ButtonFallback = new(0.78f, 0.45f, 0.20f, 1f);

    private readonly NavReader _nav = new(0);
    private readonly List<(RectTransform Rect, Image Background, Text Label, Action OnClick)> _buttons = new();
    private State _state = State.Checking;
    private LatestRelease _release;
    private List<ModUpdate> _updates;
    private Task<int> _install;
    private float _playingSince = -1f;
    private GameObject _root;
    private CanvasScaler _scaler;
    private RectTransform _panel;
    private GameArt _art;
    private int _focus;
    private int _pressed = -1;
    private float _shownAt;

    public bool Visible => _root != null && _root.activeSelf;

    /// <param name="menuOpen">A Kingdom Menu is open: wait until it's closed.</param>
    public void Tick(bool menuOpen)
    {
        switch (_state)
        {
            case State.Checking:
                if (Updates.Pending)
                    return;
                _state = Updates.TryTakeResult(out _release, out _updates) ? State.Waiting : State.Closed;
                return;

            case State.Waiting:
                // A few seconds into play, never over a menu.
                if (!GameState.Playing())
                {
                    _playingSince = -1f;
                    return;
                }
                if (_playingSince < 0f)
                    _playingSince = Time.unscaledTime;
                if (menuOpen || Time.unscaledTime - _playingSince < ShowAfterPlayingSeconds)
                    return;
                _art = GameArt.Shared;
                ShowAsking();
                break;

            case State.Installing:
                if (_install.IsCompleted)
                    ShowInstalled();
                break;

            case State.Closed:
                return;
        }

        if (!Visible)
            return;
        // Hold the monarch's controls while it's up (and take them again after a cutscene); let go if play stopped.
        if (GameState.Playing())
            MenuFocus.Take(0);
        else
            MenuFocus.Release(0);
        Place();
        HandleInput();
    }

    private void ShowAsking()
    {
        var lines = new List<(string Left, string Right)>();
        foreach (var u in _updates)
            lines.Add((u.Name, $"{u.Installed} to {u.Latest}"));
        Build("UPDATES AVAILABLE", "New versions of these mods are out:", lines,
            ("UPDATE", StartInstall), ("NOT NOW", Close));
        _state = State.Asking;
    }

    private void StartInstall()
    {
        Build("UPDATING", "Downloading the new versions...", null);
        _install = Updates.Install(_release, _updates);
        _state = State.Installing;
    }

    private void ShowInstalled()
    {
        if (_install.IsFaulted)
        {
            var reason = _install.Exception?.GetBaseException().Message ?? "unknown error";
            Plugin.Logger.LogWarning($"Couldn't install the updates: {reason}");
            Build("UPDATE FAILED", "Couldn't install the updates.", new List<(string, string)> { ("Your mods are unchanged.", "") },
                ("OK", Close));
        }
        else
        {
            Build("UPDATED", "Restart the game to use the new versions:", Names(), ("OK", Close));
        }
        _state = State.Done;
    }

    private List<(string, string)> Names()
    {
        var lines = new List<(string, string)>();
        foreach (var u in _updates)
            lines.Add((u.Name, u.Latest));
        return lines;
    }

    private void Close()
    {
        _state = State.Closed;
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        MenuFocus.Release(0);
    }

    // ------------------------------------------------------------------ view

    private void Build(string title, string message, List<(string Left, string Right)> lines, params (string Label, Action OnClick)[] buttons)
    {
        if (_root != null)
            Object.Destroy(_root);
        _buttons.Clear();
        _focus = 0;
        _pressed = -1;
        _shownAt = Time.unscaledTime;

        _root = new GameObject("KingdomMenuUpdatePrompt");
        Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32100; // above the menus
        canvas.pixelPerfect = true;
        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.referencePixelsPerUnit = 32f;
        _root.AddComponent<GraphicRaycaster>(); // so clicks on it don't reach the game's menus underneath

        var y = 9f;
        var count = lines?.Count ?? 0;
        var height = y + 18f + 8f + LineStep + count * LineStep + (buttons.Length > 0 ? 8f + 20f : 0f) + 12f;
        _panel = MenuView.NewRect("Panel", _root.transform, 0f, 0f, Width, height);
        MenuView.AddImage(_panel, _art.Get("menu_panel_background"), true, new Color(0.17f, 0.11f, 0.10f, 0.96f)).raycastTarget = true;

        MenuView.AddText(MenuView.NewRect("Title", _panel, Inset, y, Width - Inset * 2f, 18f), _art.Title, title, MenuView.Gold, TextAnchor.MiddleCenter);
        y += 18f + 8f;
        MenuView.AddText(MenuView.NewRect("Message", _panel, Inset, y, Width - Inset * 2f, 16f), _art.Body, message, MenuView.Cream, TextAnchor.MiddleLeft);
        y += LineStep;
        for (var i = 0; i < count; i++)
        {
            var (left, right) = lines[i];
            MenuView.AddText(MenuView.NewRect($"Name{i}", _panel, Inset + 8f, y, Width - Inset * 2f - 8f, 16f), _art.Body, left, MenuView.Gold, TextAnchor.MiddleLeft);
            MenuView.AddText(MenuView.NewRect($"Version{i}", _panel, Inset, y, Width - Inset * 2f, 16f), _art.Body, right, MenuView.Dim, TextAnchor.MiddleRight);
            y += LineStep;
        }

        if (buttons.Length > 0)
        {
            y += 8f;
            const float buttonWidth = 74f, gap = 8f;
            var x = (Width - (buttons.Length * buttonWidth + (buttons.Length - 1) * gap)) / 2f;
            foreach (var (label, onClick) in buttons)
            {
                var rect = MenuView.NewRect(label, _panel, x, y, buttonWidth, 20f);
                var normal = _art.Get("menu_button_wood_normal");
                var background = MenuView.AddImage(rect, normal, true, normal != null ? Color.white : ButtonFallback);
                var text = MenuView.AddText(MenuView.NewRect("Label", rect, 0f, 0f, buttonWidth, 20f), _art.Body, label, MenuView.Cream, TextAnchor.MiddleCenter);
                _buttons.Add((rect, background, text, onClick));
                x += buttonWidth + gap;
            }
        }
        Place();
        RefreshButtons(-1);
    }

    /// <summary>Centred on the screen, at the menus' pixel scale.</summary>
    private void Place()
    {
        var scale = Mathf.Max(1, Mathf.FloorToInt(Screen.height / 330f * Mathf.Max(0.1f, Plugin.UiScale.Value)));
        while (scale > 1 && Width * scale > Screen.width)
            scale--;
        if (!Mathf.Approximately(_scaler.scaleFactor, scale))
            _scaler.scaleFactor = scale;
        var size = _panel.sizeDelta;
        var x = Mathf.Round((Screen.width / (float)scale - size.x) / 2f);
        var y = Mathf.Round((Screen.height / (float)scale - size.y) / 2f);
        _panel.anchoredPosition = new Vector2(x, -y);
    }

    private void HandleInput()
    {
        var nav = _nav.Read();
        if (Time.unscaledTime - _shownAt < InputDelaySeconds)
            return;
        var input = UnityInput.Current;
        var mouse = (Vector2)input.mousePosition;
        var mouseDown = input.GetMouseButtonDown(0);
        var mouseUp = input.GetMouseButtonUp(0);

        var clicked = -1;
        var hovered = -1;
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_buttons[i].Rect, mouse, null))
                hovered = i;
        }
        if (hovered >= 0)
            _focus = hovered;
        if (mouseDown)
            _pressed = hovered;
        if (mouseUp)
        {
            if (_pressed >= 0 && _pressed == hovered)
                clicked = _pressed;
            _pressed = -1;
        }

        if (_buttons.Count > 0)
        {
            if (nav.Dx != 0)
                _focus = (_focus + nav.Dx + _buttons.Count) % _buttons.Count;
            if (nav.SubmitDown)
                clicked = _focus;
            else if (nav.CancelDown)
                clicked = _buttons.Count - 1; // NOT NOW / OK
        }
        RefreshButtons(_pressed);
        if (clicked >= 0 && clicked < _buttons.Count)
            _buttons[clicked].OnClick();
    }

    private void RefreshButtons(int pressed)
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var (_, background, label, _) = _buttons[i];
            var focused = i == _focus;
            var sprite = _art.Get(i == pressed ? "menu_button_wood_pressed" : focused ? "menu_button_wood_highlight" : "menu_button_wood_normal");
            if (sprite != null)
                background.sprite = sprite;
            else
                background.color = focused ? MenuView.Gold : ButtonFallback;
            label.color = focused ? MenuView.Gold : MenuView.Cream;
        }
    }
}
