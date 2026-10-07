using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Rewired;
using UnityEngine;
using RewiredPlayer = Rewired.Player;

namespace KingdomMenu;

/// <summary>
/// Local co-op plumbing shared by the menus: which monarch, controller and screen strip belong to player 1 / 2.
/// Player index 0 = player 1 (Rewired player 0, Kingdom.playerOne, Game._currentControllable);
/// index 1 = player 2 (Rewired player 1, Kingdom.playerTwo, Game._secondaryControllable).
/// </summary>
internal static class Coop
{
    public const int MaxPlayers = 2;

    private static readonly Dictionary<(int controllerId, bool right), int> StickButtonIds = new();

    public static Kingdom Kingdom => Managers.InstExists && Managers.Inst != null ? Managers.Inst.kingdom : null;

    /// <summary>The player's monarch if they're in the game.</summary>
    public static Player Monarch(int index)
    {
        var kingdom = Kingdom;
        var player = kingdom == null ? null : index == 0 ? kingdom.playerOne : kingdom.playerTwo;
        return player != null && player.isActiveAndEnabled ? player : null;
    }

    public static bool TwoPlayers => Monarch(1) != null;

    public static RewiredPlayer Rewired(int index)
    {
        if (!ReInput.isReady)
            return null;
        try { return ReInput.players.GetPlayer(index); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Whether the player's own controller clicked a stick this frame (L3 or R3). Found by button name on each of
    /// their joysticks ("Left Stick Button", "L3", ...), so player 2's click never opens player 1's menu.
    /// </summary>
    public static bool StickClickDown(int index, bool right)
    {
        var rp = Rewired(index);
        if (rp == null)
            return false;
        var joysticks = rp.controllers.Joysticks;
        var count = rp.controllers.joystickCount;
        for (var j = 0; j < count; j++)
        {
            var joystick = joysticks[j];
            if (joystick == null)
                continue;
            var id = StickButtonId(joystick, right);
            if (id >= 0 && joystick.GetButtonDownById(id))
                return true;
        }
        return false;
    }

    private static int StickButtonId(Controller controller, bool right)
    {
        var key = (controller.id, right);
        if (StickButtonIds.TryGetValue(key, out var cached))
            return cached;

        var found = -1;
        var names = new List<string>();
        var elements = controller.ButtonElementIdentifiers;
        for (var i = 0; i < controller.buttonCount && i < 64; i++)
        {
            ControllerElementIdentifier element;
            try { element = elements[i]; }
            catch (Exception) { break; }
            if (element == null)
                continue;
            var name = (element.name ?? "").ToLowerInvariant();
            names.Add(element.name);
            var side = right ? "right" : "left";
            if (name == (right ? "r3" : "l3") || (name.Contains(side + " stick") && (name.Contains("button") || name.Contains("press") || name.Contains("click"))))
            {
                found = element.id;
                break;
            }
        }
        StickButtonIds[key] = found;
        if (found < 0)
            Plugin.Logger.LogWarning($"No {(right ? "right" : "left")} stick button found on '{controller.name}'. Buttons: {string.Join(", ", names)}");
        return found;
    }

    /// <summary>
    /// The part of the screen showing this player, in screen pixels from the top-left corner. Shared view: the whole
    /// screen. Split screen: their own half, top/bottom or (if the split was switched to side by side) left/right.
    /// </summary>
    public static (float left, float top, float width, float height) Region(int index)
    {
        float w = Screen.width, h = Screen.height;
        if (!Split)
            return (0f, 0f, w, h);

        // If the game splits by camera viewports, that player's viewport is exactly their part of the screen.
        try
        {
            var camera = CameraMarshaller.Inst.GetCameraForPlayerID(index)?._camera;
            if (camera != null)
            {
                var r = camera.rect;
                if (r.width < 0.98f || r.height < 0.98f)
                    return (r.x * w, (1f - r.y - r.height) * h, r.width * w, r.height * h);
            }
        }
        catch (Exception)
        {
            // Fall back to the halves below.
        }

        var first = index == (Plugin.Player1OnTop.Value ? 0 : 1);
        if (SideBySide)
            return first ? (0f, 0f, w / 2f, h) : (w / 2f, 0f, w / 2f, h);
        return first ? (0f, 0f, w, h / 2f) : (0f, h / 2f, w, h / 2f);
    }

    public static bool Split =>
        TwoPlayers && CameraMarshaller.InstExists && CameraMarshaller.Inst != null && CameraMarshaller.Inst.camerasCurrentlySplit;

    /// <summary>The game's split screen can be switched from top/bottom (the default) to side by side.</summary>
    private static bool SideBySide
    {
        get
        {
            try { return !CameraMarshaller.Inst.IsSplitOrientationHorizontal(); }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Keys one player uses inside the menus.</summary>
    internal sealed class KeySet
    {
        public KeyCode[] Up, Down, Left, Right, Submit, Cancel, PrevTab, NextTab;
        /// <summary>On-screen names: submit, cancel (or the menu key) and tab keys.</summary>
        public string SubmitName, CancelName, TabsName;
    }

    // The game gives both players keys on one keyboard: player 1 plays on WASD and the arrows (Space/Enter, Esc),
    // player 2 on H/J/K/U (with G and Right Shift to gallop, ` to pause). Each player's menu keys stay on their own
    // side, so one player playing never moves the other's menu.
    private static readonly KeySet PlayerOneKeys = new()
    {
        Up = new[] { KeyCode.W, KeyCode.UpArrow }, Down = new[] { KeyCode.S, KeyCode.DownArrow },
        Left = new[] { KeyCode.A, KeyCode.LeftArrow }, Right = new[] { KeyCode.D, KeyCode.RightArrow },
        Submit = new[] { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter }, Cancel = new[] { KeyCode.Escape, KeyCode.Backspace },
        PrevTab = new[] { KeyCode.Q, KeyCode.PageUp }, NextTab = new[] { KeyCode.E, KeyCode.PageDown, KeyCode.Tab },
        SubmitName = "Space", CancelName = "Esc", TabsName = "Q/E",
    };

    private static readonly KeySet PlayerTwoKeys = new()
    {
        Up = new[] { KeyCode.U }, Down = new[] { KeyCode.J }, Left = new[] { KeyCode.H }, Right = new[] { KeyCode.K },
        Submit = new[] { KeyCode.G }, Cancel = new[] { KeyCode.BackQuote },
        PrevTab = new[] { KeyCode.Y }, NextTab = new[] { KeyCode.I },
        SubmitName = "G", TabsName = "Y/I",
    };

    /// <summary>The menu keys for a player (player 2's only count in co-op).</summary>
    public static KeySet KeysFor(int index) => index == 0 ? PlayerOneKeys : TwoPlayers ? PlayerTwoKeys : null;

    /// <summary>
    /// Short on-screen names for a player's submit, cancel and tab controls, keyboard and pad together,
    /// e.g. ("Space/A", "Esc/B", "Q/E/LB/RB").
    /// </summary>
    public static (string submit, string cancel, string tabs) KeyNames(int index)
    {
        var keys = KeysFor(index) ?? PlayerOneKeys;
        // The pixel font may not have a glyph for `, so player 2's close key is named by their menu key (F9).
        var cancel = keys.CancelName ?? (index == 0 ? Plugin.ToggleKey.Value : Plugin.Player2ToggleKey.Value).ToString();
        return ($"{keys.SubmitName}/A", $"{cancel}/B", $"{keys.TabsName}/LB/RB");
    }
}

/// <summary>
/// Gives one player's menu input focus the way the game's own menus get it: that player's monarch stops receiving
/// input (Game.SendInputTo / SendSecondaryInputTo), their Rewired "Menu" controls are switched on, and their pause button
/// is held back so Esc/Start closes the menu instead of pausing. The other player keeps playing.
/// </summary>
internal static class MenuFocus
{
    // Rewired action ids, from the game's own RewiredAxis table and the names Rewired reports for them (logged once below).
    // Action 1 is Pay, the "down" that drops coins, not Submit: the old menus read it as Submit, so pressing down in a
    // menu also held its BUY button. RewiredAxis has the two tab ids the wrong way round; Rewired names 22 "MenuPrevTab".
    public const int ActHorizontal = 0;
    public const int ActVertical = 3;
    public const int ActSubmit = 4;
    public const int ActPause = 5;
    public const int ActCancel = 6;
    public const int ActPrevTab = 22;
    public const int ActNextTab = 23;

    private static readonly IControllable[] Previous = new IControllable[Coop.MaxPlayers];
    private static readonly bool[] HeldBy = new bool[Coop.MaxPlayers];
    private static readonly bool[] MapsEnabled = new bool[Coop.MaxPlayers];
    private static readonly int[] ReleasedFrame = { -100, -100 };
    private static bool _loggedActions;

    public static bool Held(int index) => HeldBy[index];

    /// <summary>Hold back that player's pause check while focused, and for a couple of frames after (the closing press).</summary>
    public static bool BlockPause(int index) =>
        index >= 0 && index < Coop.MaxPlayers && (HeldBy[index] || Time.frameCount - ReleasedFrame[index] <= 2);

    private static Game GameManager => Managers.InstExists && Managers.Inst != null ? Managers.Inst.game : null;

    public static void Take(int index)
    {
        if (HeldBy[index])
            return;
        var game = GameManager;
        if (game == null || !game.InPlayableState)
            return;

        // Only while that player's monarch has their controls (not over the game's own menus or cutscenes).
        var current = index == 0 ? game._currentControllable : game._secondaryControllable;
        if (current == null || current.TryCast<Player>() == null)
            return;

        Previous[index] = current;
        if (index == 0)
            game.SendInputTo(null);
        else
            game.SendSecondaryInputTo(null);

        var rp = Coop.Rewired(index);
        MapsEnabled[index] = rp != null;
        rp?.controllers.maps.SetMapsEnabled(true, "Menu");

        HeldBy[index] = true;
        LogActionsOnce();
    }

    public static void Release(int index)
    {
        if (!HeldBy[index])
            return;
        HeldBy[index] = false;
        ReleasedFrame[index] = Time.frameCount;

        var game = GameManager;
        // Only to the monarch we took them from, if they're still in the game (player 2 may have left co-op meanwhile).
        var monarch = Coop.Monarch(index);
        var previous = Previous[index]?.TryCast<Player>();
        if (game != null && monarch != null && previous != null && previous.Pointer == monarch.Pointer)
        {
            // Only give the controls back if nothing else has taken them in the meantime.
            if (index == 0 && game._currentControllable == null)
                game.SendInputTo(Previous[index]);
            else if (index == 1 && game._secondaryControllable == null)
                game.SendSecondaryInputTo(Previous[index]);
        }
        if (MapsEnabled[index])
            Coop.Rewired(index)?.controllers.maps.SetMapsEnabled(false, "Menu");
        Previous[index] = null;
    }

    private static void LogActionsOnce()
    {
        if (_loggedActions || !ReInput.isReady)
            return;
        _loggedActions = true;
        try
        {
            var names = new List<string>();
            foreach (var id in new[] { ActHorizontal, ActVertical, ActSubmit, ActPause, ActCancel, ActNextTab, ActPrevTab })
            {
                var action = ReInput.mapping.GetAction(id);
                names.Add($"{id}={(action != null ? action.name : "?")}");
            }
            Plugin.Logger.LogInfo($"Menu controls (Rewired actions): {string.Join(", ", names)}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Couldn't list Rewired actions: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.CheckForPause))]
internal static class BlockPausePatch
{
    // Game.Update calls CheckForPause(rewiredPlayer, idx) for each player; skip it for a player whose menu is open.
    private static bool Prefix(int idx) => !MenuFocus.BlockPause(idx);
}

/// <summary>One frame of menu navigation for one player.</summary>
internal struct NavInput
{
    public int Dx;
    public int Dy; // +1 = down the list
    public bool SubmitHeld;
    public bool SubmitDown;
    public bool CancelDown;
    public int TabStep; // -1 = previous tab (LB / Q), +1 = next tab (RB / E)
}

/// <summary>
/// Turns one player's stick/d-pad (their Rewired menu actions) and their side of the keyboard (Coop.KeysFor) into single
/// steps. One push is one step, with no repeat while held: the stick has to come back near the centre (or the key be
/// let go) before the next step.
/// </summary>
internal class NavReader
{
    /// <summary>How far the stick must be pushed to step.</summary>
    private const float PushThreshold = 0.5f;
    /// <summary>How close to the centre it must come back before the next step (a gap, so a wobbly stick can't double-step).</summary>
    private const float CentreThreshold = 0.25f;

    private readonly int _player;
    private bool _armed = true;

    public NavReader(int player)
    {
        _player = player;
    }

    public NavInput Read()
    {
        float h = 0f, v = 0f;
        bool held = false, down = false, cancel = false;
        var tab = 0;

        var keys = Coop.KeysFor(_player);
        var input = UnityInput.Current;
        // The game binds player 2's J to both "down" (pay) and Submit; while a menu direction key is held, its
        // Rewired Submit is ignored so moving down never starts buying (they buy with their own submit key).
        var moveKeyHeld = keys != null && (Any(input, keys.Up, held: true) || Any(input, keys.Down, held: true) ||
                                           Any(input, keys.Left, held: true) || Any(input, keys.Right, held: true));

        if (MenuFocus.Held(_player))
        {
            var rp = Coop.Rewired(_player);
            if (rp != null)
            {
                h = rp.GetAxis(MenuFocus.ActHorizontal);
                v = rp.GetAxis(MenuFocus.ActVertical);
                held = !moveKeyHeld && rp.GetButton(MenuFocus.ActSubmit);
                down = !moveKeyHeld && rp.GetButtonDown(MenuFocus.ActSubmit);
                cancel = rp.GetButtonDown(MenuFocus.ActCancel) || rp.GetButtonDown(MenuFocus.ActPause);
                tab = rp.GetButtonDown(MenuFocus.ActNextTab) ? 1 : rp.GetButtonDown(MenuFocus.ActPrevTab) ? -1 : 0;
            }
        }

        if (keys != null)
        {
            if (Any(input, keys.Up, held: true)) v = 1f;
            if (Any(input, keys.Down, held: true)) v = -1f;
            if (Any(input, keys.Left, held: true)) h = -1f;
            if (Any(input, keys.Right, held: true)) h = 1f;
            held |= Any(input, keys.Submit, held: true);
            down |= Any(input, keys.Submit, held: false);
            cancel |= Any(input, keys.Cancel, held: false);
            if (Any(input, keys.NextTab, held: false))
                tab = input.GetKey(KeyCode.LeftShift) && input.GetKeyDown(KeyCode.Tab) ? -1 : 1;
            else if (Any(input, keys.PrevTab, held: false))
                tab = -1;
        }

        var nav = new NavInput { SubmitHeld = held, SubmitDown = down, CancelDown = cancel, TabStep = tab };
        var push = Mathf.Max(Mathf.Abs(h), Mathf.Abs(v));
        if (!_armed)
        {
            if (push < CentreThreshold)
                _armed = true;
        }
        else if (push > PushThreshold)
        {
            // One step along whichever axis is pushed further, then wait for the stick to return to the centre.
            if (Mathf.Abs(h) >= Mathf.Abs(v))
                nav.Dx = Math.Sign(h);
            else
                nav.Dy = -Math.Sign(v);
            _armed = false;
        }
        return nav;
    }

    private static bool Any(IInputSystem input, KeyCode[] keys, bool held)
    {
        foreach (var key in keys)
        {
            if (held ? input.GetKey(key) : input.GetKeyDown(key))
                return true;
        }
        return false;
    }
}
