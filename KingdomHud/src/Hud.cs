using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace KingdomHud;

/// <summary>
/// Drives the HUD: the unit counter / kingdom info overlay, a stamina bar under each player's mount, and a label over
/// whatever each player is standing at.
/// </summary>
public class Hud : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;

    /// <summary>How long (game seconds) the wages line says what this morning's payday took.</summary>
    private const float PaydayNoteSeconds = 90f;

    // Create BepInEx/config/kingdomhud.preview to show the HUD outside gameplay (title screen) with sample numbers.
    private static readonly bool Preview = File.Exists(Path.Combine(Paths.ConfigPath, "kingdomhud.preview"));

    internal static GameArt Art;

    private readonly List<ColumnData> _columns = new();
    private readonly List<InfoLine> _lines = new();
    private readonly List<InfoLine> _shared = new();
    private readonly Dictionary<UnitKind, Sprite> _liveIcons = new();
    private readonly HashSet<UnitKind> _unlocked = new();
    private readonly StaminaBar[] _bars = new StaminaBar[2];
    private readonly NearbyLabel[] _labels = new NearbyLabel[2];
    private readonly CounterPanel[] _panels = new CounterPanel[2];
    private bool _visible = true;
    private float _nextRefresh;
    private IntPtr _lastKingdom;
    private bool _loggedError;

    public Hud(IntPtr ptr) : base(ptr) { }

    private bool Active => _visible && (Preview || KingdomInfo.InKingdom());

    private bool? _wasInKingdom;

    private void Update()
    {
        try
        {
            if (Plugin.TogglePressed(UnityInput.Current))
                _visible = !_visible;

            var inKingdom = KingdomInfo.InKingdom();
            if (_wasInKingdom != inKingdom)
            {
                _wasInKingdom = inKingdom;
                Plugin.Logger.LogInfo(inKingdom ? "Gameplay started: HUD on." : "Not in gameplay (menu/loading): HUD hidden.");
            }

            if (!Active)
            {
                foreach (var p in _panels)
                    p?.SetVisible(false);
                return;
            }

            // Shared view: one panel. Split screen: each player gets their own in their own strip.
            EnsureBuilt();
            var split = KingdomInfo.Split;
            for (var i = 0; i < _panels.Length; i++)
            {
                var show = i == 0 || split;
                _panels[i].SetVisible(show);
                if (!show)
                    continue;
                var (left, top, width, height) = KingdomInfo.Region(i);
                var scale = Mathf.Max(1, Mathf.RoundToInt(height / 400f * Mathf.Max(0.1f, Plugin.UiScale.Value)));
                _panels[i].Place(left, top, width, height, scale, Plugin.CounterCorner.Value);
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }
        catch (Exception e)
        {
            LogOnce(e);
        }
    }

    private void LateUpdate()
    {
        try
        {
            var kingdom = Active ? KingdomInfo.GetKingdom() : null;
            for (var i = 0; i < 2; i++)
            {
                var player = kingdom == null ? null : i == 0 ? kingdom.playerOne : kingdom.playerTwo;
                if (player != null && !player.isActiveAndEnabled)
                    player = null;

                _bars[i] ??= new StaminaBar($"KingdomHud.StaminaBar.P{i + 1}");
                if (player != null && Plugin.ShowStaminaBar.Value)
                    _bars[i].Update(player, Time.time, Time.deltaTime);
                else
                    _bars[i].Hide();

                if (player != null && Plugin.ShowNearbyLabels.Value)
                {
                    EnsureArt();
                    _labels[i] ??= new NearbyLabel($"KingdomHud.Label.P{i + 1}");
                    _labels[i].Update(player, Time.deltaTime);
                }
                else
                    _labels[i]?.Hide();
            }
        }
        catch (Exception e)
        {
            LogOnce(e);
        }
    }

    private void EnsureArt()
    {
        if (Art != null && Art.Body != null)
            return;
        var names = Units.Kinds.Select(k => k.FallbackSprite)
            .Concat(new[] { "menu_panel_background", "coin_spin_0", "baggem", "map_icon_market_crate_coin", "sun", "menu_sun", "moon" });
        Art = GameArt.Load(names);
    }

    private void EnsureBuilt()
    {
        EnsureArt();
        for (var i = 0; i < _panels.Length; i++)
        {
            if (_panels[i] != null && _panels[i].Alive)
                continue;
            _panels[i] = new CounterPanel(Art);
            _panels[i].Build();
            _nextRefresh = 0f;
        }
    }

    private void Refresh()
    {
        var kingdom = KingdomInfo.GetKingdom();

        // A different island/kingdom may be a different biome: re-sample unit sprites.
        var kingdomPtr = kingdom != null ? kingdom.Pointer : IntPtr.Zero;
        if (kingdomPtr != _lastKingdom)
        {
            _lastKingdom = kingdomPtr;
            _liveIcons.Clear();
            _unlocked.Clear();
        }

        _columns.Clear();
        if (kingdom != null)
        {
            HashSet<PayableShop.ShopType> stands = null;
            foreach (var kind in Units.Kinds)
            {
                var count = SafeCount(kind, kingdom);
                var max = SafeMax(kind, kingdom);
                // Once a job is unlocked its column stays, even when the count drops back to 0.
                if (!kind.AlwaysShow && !_unlocked.Contains(kind))
                {
                    if (count <= 0 && !(max > 0) && !kind.Stands.Any((stands ??= Units.PlacedStands()).Contains))
                        continue;
                    _unlocked.Add(kind);
                }
                _columns.Add(new ColumnData { Count = count, Icon = IconFor(kind, count), Max = max });
            }
        }

        var previewNumbers = Preview && _columns.All(c => c.Count == 0);
        if (previewNumbers)
            FillPreview();

        // Shared kingdom lines first; then each panel gets its own purse line(s) on top of them.
        _lines.Clear();
        if (Plugin.ShowKingdomInfo.Value)
        {
            // Bank: "Bank 27", tomorrow's interest by the sun ("+3/day"), and a bar filling up to the stash that earns the
            // most interest. The words are there so it reads at a glance.
            var hasBank = KingdomInfo.TryGetBank(out var stash, out var interest, out var maxInterest, out var fullAt);
            if (Preview && !hasBank)
                (hasBank, stash, interest, maxInterest, fullAt) = (true, 45, 5, 8, 71);
            if (hasBank && Plugin.ShowBank.Value)
            {
                var full = interest >= maxInterest;
                _lines.Add(new InfoLine
                {
                    Icon = Art.Get("map_icon_market_crate_coin") ?? Art.Get("coin_spin_0"),
                    Text = $"Bank {stash}",
                    Color = CounterPanel.Cream,
                    BadgeIcon = Art.Get("menu_sun") ?? Art.Get("sun"),
                    Badge = $"+{interest}/day",
                    BadgeColor = interest > 0 ? CounterPanel.Gold : CounterPanel.Dim,
                    Bar = fullAt > 0 ? Mathf.Clamp01(stash / (float)fullAt) : -1f,
                    Note = full ? "max" : null,
                });
            }

            // Wages (Kingdom Menu's Soldier wages): what the soldiers cost a day at dawn, then (most important first) any
            // debt in red, a new bank's grace period counting down, or what this morning's payday took.
            var hasWages = MenuLink.TryGetWages(out var w);
            if (Preview && !hasWages)
                (hasWages, w) = (true, new MenuLink.Wages { PerDay = 6, Debt = 12, PaidAgo = -1f });
            if (hasWages && Plugin.ShowWages.Value)
            {
                var (note, noteColor) =
                    w.Debt > 0 ? ($"debt {w.Debt}", CounterPanel.Danger)
                    : w.StartsIn > 1 ? ($"in {w.StartsIn} days", CounterPanel.Gold)
                    : w.StartsIn == 1 ? ("tomorrow", CounterPanel.Gold)
                    : w.LastPaid > 0 && w.PaidAgo >= 0f && w.PaidAgo < PaydayNoteSeconds ? ($"paid {w.LastPaid}", CounterPanel.Gold)
                    : ((string)null, CounterPanel.Dim);
                _lines.Add(new InfoLine
                {
                    Icon = Art.Get("archer_idle1_0"),
                    Text = "Wages",
                    Color = CounterPanel.Cream,
                    // No bank on this island: nothing is paid here, so no daily amount.
                    BadgeIcon = hasBank ? Art.Get("menu_sun") ?? Art.Get("sun") : null,
                    Badge = hasBank ? $"-{w.PerDay}/day" : null,
                    BadgeColor = w.PerDay > 0 && w.StartsIn == 0 ? CounterPanel.Cream : CounterPanel.Dim,
                    Note = note,
                    NoteColor = noteColor,
                });
            }

            // Cottages: one pip per villager slot (full = ready to hire), and a bar filling up to the next one.
            if (Plugin.ShowCottages.Value && KingdomInfo.TryGetCottages(out var ready, out var max, out var nextIn, out var cooldown))
            {
                _lines.Add(new InfoLine
                {
                    Icon = Art.Get("peasant_idle_0"),
                    Pips = max,
                    PipsFull = ready,
                    Bar = nextIn >= 0f && cooldown > 0f ? 1f - nextIn / cooldown : -1f,
                    BarColor = CounterPanel.Cream,
                    Note = nextIn >= 0f ? $"{Mathf.CeilToInt(nextIn)}s" : "all ready",
                    Color = CounterPanel.Cream,
                });
            }

            // Blood moon: a red moon and the days left, brighter as it gets closer.
            var moon = Plugin.ShowBloodMoon.Value ? KingdomInfo.DaysUntilBloodMoon() ?? (Preview ? 3 : (int?)null) : null;
            if (moon.HasValue)
            {
                var d = moon.Value;
                var color = d == 0 ? CounterPanel.Danger : d == 1 ? CounterPanel.Gold : CounterPanel.Dim;
                _lines.Add(new InfoLine
                {
                    Icon = Art.Get("moon"),
                    IconTint = d <= 1 ? CounterPanel.Danger : new Color(0.75f, 0.35f, 0.3f),
                    Text = d == 0 ? "Blood moon tonight!" : d == 1 ? "Blood moon tomorrow" : $"Blood moon in {d} days",
                    Color = color,
                });
            }
        }

        _shared.Clear();
        _shared.AddRange(_lines);

        var split = KingdomInfo.Split;
        for (var i = 0; i < _panels.Length; i++)
        {
            if (i > 0 && !split)
                continue;
            _lines.Clear();
            if (Plugin.ShowPurse.Value)
                AddPurseLines(kingdom, split ? i : -1);
            _lines.AddRange(_shared);
            _panels[i].SetData(_columns, Plugin.ShowUnitCounter.Value, _lines);
        }
    }

    /// <summary>Coins (and gems, if any) in one player's purse (onlyPlayer 0/1), or in both on a shared view (-1).</summary>
    private void AddPurseLines(Kingdom kingdom, int onlyPlayer)
    {
        var p1 = kingdom != null && onlyPlayer != 1 ? kingdom.playerOne : null;
        var p2 = kingdom != null && onlyPlayer != 0 ? kingdom.playerTwo : null;
        var w1 = p1 != null && p1.isActiveAndEnabled ? p1.wallet : null;
        var w2 = p2 != null && p2.isActiveAndEnabled ? p2.wallet : null;
        if (w1 == null && w2 == null)
        {
            if (Preview)
                AddLine(Art.Get("coin_spin_0"), "12", CounterPanel.Gold);
            return;
        }

        var coins = w2 == null ? $"{w1.Coins}" : w1 == null ? $"{w2.Coins}" : $"P1 {w1.Coins}   P2 {w2.Coins}";
        AddLine(Art.Get("coin_spin_0"), coins, CounterPanel.Gold);

        var gems1 = w1 != null ? w1.Gems : 0;
        var gems2 = w2 != null ? w2.Gems : 0;
        if (gems1 > 0 || gems2 > 0)
            AddLine(Art.Get("baggem"), w2 == null ? $"{gems1}" : w1 == null ? $"{gems2}" : $"P1 {gems1}   P2 {gems2}", CounterPanel.Cream);
    }

    [HideFromIl2Cpp]
    private void AddLine(Sprite icon, string text, Color color) =>
        _lines.Add(new InfoLine { Icon = icon, Text = text, Color = color });

    [HideFromIl2Cpp]
    private static int? SafeMax(UnitKind kind, Kingdom kingdom)
    {
        try
        {
            return kind.Max?.Invoke(kingdom);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [HideFromIl2Cpp]
    private static int SafeCount(UnitKind kind, Kingdom kingdom)
    {
        try
        {
            return kind.Count(kingdom);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [HideFromIl2Cpp]
    private Sprite IconFor(UnitKind kind, int count)
    {
        if (_liveIcons.TryGetValue(kind, out var live) && live != null)
            return live;
        if (count > 0)
        {
            var sampled = Units.SampleIdleSprite(kind);
            if (sampled != null)
            {
                _liveIcons[kind] = sampled;
                return sampled;
            }
        }
        return Art.Get(kind.FallbackSprite);
    }

    private void FillPreview()
    {
        _columns.Clear();
        var sample = new[] { 3, 5, 4, 9, 6, 2, 3 };
        for (var i = 0; i < sample.Length && i < Units.Kinds.Count; i++)
            _columns.Add(new ColumnData { Count = sample[i], Icon = Art.Get(Units.Kinds[i].FallbackSprite) });
    }

    [HideFromIl2Cpp]
    private void LogOnce(Exception e)
    {
        if (_loggedError)
            return;
        _loggedError = true;
        Plugin.Logger.LogError($"Kingdom HUD error: {e}");
    }
}
