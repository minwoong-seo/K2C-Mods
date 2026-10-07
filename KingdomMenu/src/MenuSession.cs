using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// One player's menu: their own view (canvas), open state, input focus, navigation, tab and status line, with purchases
/// paid from their own purse. Player 1 and player 2 each have one, so both can use the menu at once in local co-op.
/// </summary>
internal sealed class MenuSession
{
    private const float RefreshInterval = 0.25f;
    private const float StatusSeconds = 4f;

    private readonly int _player;
    private readonly GameArt _art;
    private readonly bool _preview;
    private readonly NavReader _nav;
    private readonly Page[] _pages;
    private readonly List<RowModel> _rows = new();
    private MenuView _view;
    private int _tab;
    private bool _openedInGame;
    private float _nextRefresh;
    private string _status = "";
    private float _statusUntil;

    public MenuSession(int player, GameArt art, bool preview)
    {
        _player = player;
        _art = art;
        _preview = preview;
        _nav = new NavReader(player);
        var ctx = new PageContext { Player = player, Art = art, Preview = preview, SetStatus = SetStatus };
        _pages = new Page[] { new ShopPage(ctx), new UpgradesPage(ctx), new UnitsPage(ctx), new StatuesPage(ctx) };
    }

    public bool Open { get; private set; }

    public void Toggle() => SetOpen(!Open);

    public void SetOpen(bool open)
    {
        if (open == Open)
            return;
        Open = open;

        if (open)
        {
            _openedInGame = GameState.Playing();
            MenuFocus.Take(_player);
            EnsureView();
            Refresh();
            _view.FocusDefault();
        }
        else
        {
            MenuFocus.Release(_player);
        }

        _view?.SetVisible(open);
    }

    /// <param name="keyboard">This player owns the number keys (hold 1-9 to buy from that row, Shift for FILL).</param>
    public void Tick(IInputSystem input, bool keyboard)
    {
        if (!Open)
            return;

        // The kingdom ended under us (title, loading, a cutscene) or this player left: close and hand control back.
        var playing = GameState.Playing();
        if ((_openedInGame && !playing) || (playing && Coop.Monarch(_player) == null))
        {
            SetOpen(false);
            return;
        }

        // Take the monarch's controls whenever they're free to take (e.g. after a cutscene); give them back if the
        // game stopped being playable while we held them.
        if (playing && !MenuFocus.Held(_player))
            MenuFocus.Take(_player);
        else if (!playing && MenuFocus.Held(_player))
            MenuFocus.Release(_player);

        EnsureView();
        var (left, top, width, height) = Coop.Region(_player);
        // Sharing one screen in co-op: player 1's menu on the left half, player 2's on the right, so both fit.
        if (Coop.TwoPlayers && !Coop.Split)
            (left, width) = (_player == 0 ? 0f : Screen.width / 2f, Screen.width / 2f);
        // Sized by the whole screen's height (1080p = 3x), then fitted into this player's part of it.
        var scale = Mathf.Max(1, Mathf.FloorToInt(Screen.height / 330f * Mathf.Max(0.1f, Plugin.UiScale.Value)));
        _view.Place(left, top, width, height, scale);

        var now = Time.unscaledTime;
        var nav = _nav.Read();
        if (nav.CancelDown)
        {
            SetOpen(false);
            return;
        }

        if (nav.TabStep != 0)
        {
            SelectTab((_tab + nav.TabStep + _pages.Length) % _pages.Length);
            if (_view.FocusOnTabs)
                _view.FocusSelectedTab(sub: false);
        }
        else if (nav.Dy != 0)
            _view.MoveFocus(0, nav.Dy);
        else if (nav.Dx != 0)
            _view.MoveFocus(nav.Dx, 0);

        if (now >= _nextRefresh)
            Refresh();

        // Number keys (keyboard player only): hold 1-9 for that row's first button, Shift+1-9 for its second (FILL).
        UiButton keyHold = null;
        if (keyboard)
        {
            var second = input.GetKey(KeyCode.LeftShift) || input.GetKey(KeyCode.RightShift);
            for (var i = 0; i < 9 && i < _rows.Count && keyHold == null; i++)
            {
                if (input.GetKey(KeyCode.Alpha1 + i) || input.GetKey(KeyCode.Keypad1 + i))
                    keyHold = _view.RowButton(i, second ? 1 : 0) ?? _view.RowButton(i, 0);
            }
        }
        var focused = _view.Focused;
        if (keyHold == null && nav.SubmitHeld && focused != null && focused.HoldToActivate)
            keyHold = focused;
        var keyClick = nav.SubmitDown && focused != null && !focused.HoldToActivate ? focused : null;

        var mouse = input.mousePosition;
        _view.HandleInput(new Vector2(mouse.x, mouse.y), input.GetMouseButtonDown(0), input.GetMouseButton(0), input.GetMouseButtonUp(0),
            keyHold, keyClick, Time.unscaledDeltaTime);
        _view.Wheel(new Vector2(mouse.x, mouse.y), input.mouseScrollDelta.y);
        _view.Tick(now);

        var showStatus = !string.IsNullOrEmpty(_status) && now < _statusUntil;
        _view.SetFooter(showStatus ? _status : Hint(), showStatus);
    }

    private string Hint()
    {
        var pageHint = _pages[_tab].Hint(_player);
        if (pageHint != null)
            return pageHint;
        var (_, cancel, tabs) = Coop.KeyNames(_player);
        return $"{tabs}: tabs  {cancel}: close";
    }

    private void EnsureView()
    {
        if (_view != null && _view.Alive)
            return;

        _view = new MenuView(_art)
        {
            OnTab = SelectTab,
            OnSubTab = SelectSubTab,
            OnTogglePrice = () =>
            {
                Plugin.FreePurchases.Value = !Plugin.FreePurchases.Value;
                Refresh();
            },
            OnReset = () =>
            {
                _pages[_tab].OnReset();
                Refresh();
            },
        };
        var names = new string[_pages.Length];
        for (var i = 0; i < _pages.Length; i++)
            names[i] = _pages[i].Title;
        _view.Build(names, _player);
        _view.SetVisible(Open);
        Refresh();
    }

    private void SelectTab(int tab)
    {
        if (tab == _tab)
            return;
        _tab = tab;
        _status = "";
        _view?.ResetScroll();
        Refresh();
    }

    private void SelectSubTab(int sub)
    {
        var page = _pages[_tab];
        if (page.SubTabs == null || sub == page.SubTab)
            return;
        page.SubTab = sub;
        _view?.ResetScroll();
        Refresh();
    }

    private void Refresh()
    {
        _nextRefresh = Time.unscaledTime + RefreshInterval;
        if (_view == null)
            return;

        var page = _pages[_tab];
        _rows.Clear();
        page.Fill(_rows);

        _view.SetTab(_tab);
        _view.SetSubTabs(page.SubTabs, page.SubTab);
        _view.SetHeaderButtons(page.PriceLabel, page.ResetLabel);
        _view.SetEmptyMessage(page.EmptyMessage);
        _view.SetRows(_rows);

        var wallet = GameState.Playing() ? Coop.Monarch(_player)?.wallet : null;
        var coins = wallet != null ? wallet.Coins : (_preview ? 30 : (int?)null);
        var gems = wallet != null ? wallet.Gems : 0;
        var title = Coop.TwoPlayers || _preview ? $"KINGDOM  P{_player + 1}" : "KINGDOM";
        var key = _player == 0 ? Plugin.ToggleKey.Value : Plugin.Player2ToggleKey.Value;
        _view.SetHeader(title, Plugin.OpenWithStickClick.Value ? $"{key} or L3/R3 to close" : $"{key} to close", coins, gems);
    }

    private void SetStatus(string message)
    {
        _status = message ?? "";
        _statusUntil = Time.unscaledTime + StatusSeconds;
        _nextRefresh = 0f; // something happened (a purchase, usually): show it right away
        if (!string.IsNullOrEmpty(message))
            Plugin.Logger.LogInfo($"P{_player + 1}: {message}");
    }
}
