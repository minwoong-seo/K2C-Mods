using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Keeps the upgrades applied to the kingdom's units (new recruits, spawned steeds, rebuilt walls are picked up by a
/// scan every second) and runs one menu per player: a stick click on a player's own controller opens theirs, and
/// F6 / F9 open player 1's / player 2's from the keyboard.
/// </summary>
public class MenuHost : MonoBehaviour
{
    private const float ApplyInterval = 1f;

    // Create BepInEx/config/kingdommenu.preview to show sample rows outside a kingdom (for checking the layout;
    // nothing can be bought while it's on).
    private static readonly bool Preview = File.Exists(Path.Combine(Paths.ConfigPath, "kingdommenu.preview"));

    private readonly StatApplier _applier = new();
    private readonly MenuSession[] _sessions = new MenuSession[Coop.MaxPlayers];
    private readonly UpdatePrompt _updatePrompt = new();
    private GameArt _art;
    private float _nextApply;
    private bool _applyNow;
    private bool _anyOpen;
    private bool _prevCursorVisible;
    private CursorLockMode _prevCursorLock;
    private bool _loggedError;

    public MenuHost(IntPtr ptr) : base(ptr) { }

    private void Awake()
    {
        UpgradeStore.Changed += () => _applyNow = true;
        StatApplier.Current = _applier;
    }

    private void Update()
    {
        try
        {
            if (_applyNow || Time.unscaledTime >= _nextApply)
            {
                _applyNow = false;
                _nextApply = Time.unscaledTime + ApplyInterval;
                // Coins the banker is still carrying when he hides go into the bank, then wages (paying off debt changes
                // how strong the soldiers are), then the stats.
                Guard("banker", BankerSafekeeping.Tick);
                Guard("wages", Wages.Tick);
                _applier.Apply();
                StatuesPage.TrackFound();
            }

            PurseSize.Tick();
            CoinMagnet.Tick();
            TickMenus();
            _updatePrompt.Tick(AnyMenuOpen());
            UpdateCursor();
        }
        catch (Exception e)
        {
            if (!_loggedError)
            {
                Plugin.Logger.LogError($"Kingdom Menu error: {e}");
                _loggedError = true;
            }
        }
    }

    private readonly System.Collections.Generic.HashSet<string> _loggedParts = new();

    /// <summary>Runs one part of the per-second work so that an error in it doesn't stop the stats being applied.</summary>
    private void Guard(string part, Action tick)
    {
        try
        {
            tick();
        }
        catch (Exception e)
        {
            if (_loggedParts.Add(part))
                Plugin.Logger.LogError($"Kingdom Menu {part} error: {e}");
        }
    }

    private void TickMenus()
    {
        var input = UnityInput.Current;
        var twoPlayers = Coop.TwoPlayers;

        for (var i = 0; i < Coop.MaxPlayers; i++)
        {
            var key = i == 0 ? Plugin.ToggleKey.Value : Plugin.Player2ToggleKey.Value;
            var toggled = (i == 0 || twoPlayers) && input.GetKeyDown(key);
            if (Plugin.OpenWithStickClick.Value)
            {
                // Playing alone, a stick click from any controller opens player 1's menu (Rewired may file the pad under
                // player 2); in co-op each player's own controller opens their own.
                var stick = StickClick(i) || (i == 0 && !twoPlayers && StickClick(1));
                toggled |= stick && (i == 0 || twoPlayers);
            }

            // Opens only while playing (or in the layout preview), never over the game's own menus and dialogs; it can
            // always be closed.
            // Not over the update popup.
            if (toggled && !_updatePrompt.Visible && (_sessions[i] is { Open: true } || GameState.Playing() || Preview))
            {
                _art ??= GameArt.Shared;
                _sessions[i] ??= new MenuSession(i, _art, Preview);
                _sessions[i].Toggle();
            }
            _sessions[i]?.Tick(input, keyboard: i == 0);
        }
    }

    private static bool StickClick(int player) => Coop.StickClickDown(player, right: false) || Coop.StickClickDown(player, right: true);

    private bool AnyMenuOpen()
    {
        foreach (var s in _sessions)
        {
            if (s != null && s.Open)
                return true;
        }
        return false;
    }

    /// <summary>Show the mouse cursor while any menu (or the update popup) is open; put it back as it was when the last one closes.</summary>
    private void UpdateCursor()
    {
        var anyOpen = AnyMenuOpen() || _updatePrompt.Visible;
        if (anyOpen == _anyOpen)
            return;
        _anyOpen = anyOpen;

        if (anyOpen)
        {
            _prevCursorVisible = Cursor.visible;
            _prevCursorLock = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;
        }
    }
}
