using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// SETTINGS: the mods' options worth changing while playing, in three groups: what Kingdom HUD shows, Kingdom Menu's
/// gameplay helpers, and the hardships. Each row changes the BepInEx config entry itself, so it's saved to the mod's
/// .cfg at once and the mods use it straight away. Kingdom HUD's options are found through BepInEx's plugin list, so
/// they only appear when the HUD is installed (and only the ones its version has). Rows about the bank stay hidden
/// until the kingdom has one, like the rest of the menu.
/// </summary>
internal sealed class SettingsPage : Page
{
    private const string HudGuid = "kingdomhud.kingdomtwocrowns";
    private const float HoldSeconds = 0.2f;
    private const float ResetConfirmSeconds = 3f;

    private sealed class Setting
    {
        public string Name;
        public bool Hud;
        public string Section;
        public string Key;
        /// <summary>The second line, given the current value.</summary>
        public Func<object, string> Detail;
        /// <summary>Numbers: how much one press of - or + changes it, and the limits if the entry has none.</summary>
        public float Step = 1f;
        public float Min;
        public float Max = 10f;
        /// <summary>Hidden until this is true (e.g. bank options before the kingdom has a bank).</summary>
        public bool NeedsBank;
        /// <summary>
        /// On/off settings that only count while this other one (same file) is on too: the HUD's info lines need its
        /// ShowKingdomInfo. The row shows them on only when both are, and switching one on switches that on as well.
        /// </summary>
        public string MasterSection;
        public string MasterKey;
    }

    private static readonly string[] GroupNames = { "HUD", "GAMEPLAY", "HARDSHIP" };

    private static readonly Setting[][] Groups =
    {
        new[]
        {
            Show("Unit counter", "Units", "ShowUnitCounter", "How many of each job you have"),
            Show("Purse", "Units", "ShowPurse", "Your coins (and gems)"),
            Info("Bank", "ShowBank", "Savings and tomorrow's interest", needsBank: true),
            Info("Wages", "ShowWages", "Soldier wages and any debt", needsBank: true),
            Info("Cottages", "ShowCottages", "Villagers waiting to be hired"),
            Info("Blood moon", "ShowBloodMoon", "Days until the next blood moon"),
            Show("Stamina bar", "Stamina", "ShowStaminaBar", "A bar under your steed"),
            Show("Hide when rested", "Stamina", "AutoHide", "Stamina bar fades out at full stamina"),
            Show("Nearby labels", "Labels", "ShowNearbyLabels", "Names of what you're standing at"),
            new Setting { Name = "Corner", Hud = true, Section = "Units", Key = "Position", Detail = v => $"Counter at the {Words(v)}" },
            new Setting
            {
                Name = "HUD size", Hud = true, Section = "Units", Key = "UiScale", Step = 0.25f, Min = 0.5f, Max = 3f,
                Detail = _ => "Times the usual size",
            },
        },
        new[]
        {
            new Setting { Name = "Hold to drop coins", Section = "Gameplay", Key = "HoldToDropCoins", Detail = _ => "Keep dropping coins while the key is held" },
            new Setting
            {
                Name = "Banker keeps collecting", Section = "Gameplay", Key = "BankerKeepsCollecting", NeedsBank = true,
                Detail = _ => "Takes all you give, banks even a few",
            },
            new Setting
            {
                Name = "Menu size", Section = "General", Key = "UiScale", Step = 0.25f, Min = 0.5f, Max = 3f,
                Detail = _ => "Times the usual size",
            },
            new Setting { Name = "Check for updates", Section = "Updates", Key = "CheckForUpdates", Detail = _ => "Offer new versions when the game starts" },
        },
        new[]
        {
            new Setting
            {
                Name = "Courier fee", Section = "Hardships", Key = "CourierFee",
                Detail = v => Convert.ToInt32(v) > 0 ? "Extra per remote shop item" : "Off: stand prices",
            },
            new Setting
            {
                Name = "Soldier wages", Section = "Hardships", Key = "SoldierWages", NeedsBank = true,
                Detail = _ => "Army upgrades come with daily wages",
            },
            new Setting
            {
                Name = "Wage rate", Section = "Hardships", Key = "SoldiersPerCoin", NeedsBank = true,
                Detail = v => $"1 coin a day per {Convert.ToInt32(v)} soldiers",
            },
            new Setting
            {
                Name = "Grace period", Section = "Hardships", Key = "WageGraceDays", NeedsBank = true,
                Detail = v => Convert.ToInt32(v) <= 1 ? "A new bank pays the next dawn" : $"A new bank pays on day {Convert.ToInt32(v)}",
            },
            new Setting
            {
                Name = "Debt penalty", Section = "Hardships", Key = "DebtPenaltyPerCoin", Step = 0.01f, NeedsBank = true,
                Detail = _ => "Strength lost per coin owed",
            },
            new Setting
            {
                Name = "Penalty limit", Section = "Hardships", Key = "MaxDebtPenalty", Step = 0.1f, NeedsBank = true,
                Detail = v => $"Strength never below x{N(1f - Convert.ToSingle(v))}",
            },
        },
    };

    private float _resetConfirmUntil;

    public SettingsPage(PageContext ctx) : base(ctx) { }

    public override string Title => "SETTINGS";
    public override IReadOnlyList<string> SubTabs => GroupNames;
    public override string ResetLabel => Time.unscaledTime < _resetConfirmUntil ? "SURE?" : "DEFAULTS";
    public override string EmptyMessage => SubTab == 0 ? "Kingdom HUD isn't installed." : "";

    public override string Hint(int player)
    {
        var (submit, cancel, tabs) = Coop.KeyNames(player);
        return $"Hold {submit}: change  {tabs}: tabs  {cancel}: close";
    }

    public override void Fill(List<RowModel> rows)
    {
        bool? bank = null;
        foreach (var setting in Groups[SubTab])
        {
            var entry = Entry(setting);
            if (entry == null)
                continue;
            if (setting.NeedsBank && !(bank ??= BankKnown()))
                continue;
            rows.Add(Row(setting, entry));
        }
    }

    private RowModel Row(Setting setting, ConfigEntryBase entry)
    {
        var value = entry.BoxedValue;
        var row = new RowModel { Name = setting.Name, Detail = setting.Detail?.Invoke(value) };

        if (value is bool set)
        {
            var master = Master(setting, entry.ConfigFile);
            var on = set && (master == null || (bool)master.BoxedValue);
            row.NameColor = on ? MenuView.Gold : MenuView.Cream;
            row.Buttons = new[]
            {
                Button(on ? "ON" : "OFF", true, () =>
                {
                    if (!on && master != null && !(bool)master.BoxedValue)
                    {
                        // The whole section was off: show just this line, not every line whose own switch was on.
                        foreach (var other in Groups[SubTab])
                        {
                            if (other != setting && other.MasterKey == setting.MasterKey && other.MasterSection == setting.MasterSection)
                            {
                                var otherEntry = Entry(other);
                                if (otherEntry != null)
                                    otherEntry.BoxedValue = false;
                            }
                        }
                        master.BoxedValue = true;
                    }
                    Set(setting, entry, !on);
                }),
            };
        }
        else if (value is Enum)
        {
            row.Buttons = new[] { Button("MOVE", true, () => Set(setting, entry, NextEnum(value))) };
        }
        else
        {
            var number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            var (min, max) = Limits(setting, entry);
            row.PipText = N(number);
            row.Buttons = new[]
            {
                Button("-", number > min + 0.0001f, () => Set(setting, entry, Clamp(entry, number - setting.Step, min, max))),
                Button("+", number < max - 0.0001f, () => Set(setting, entry, Clamp(entry, number + setting.Step, min, max))),
            };
        }
        return row;
    }

    private static RowButton Button(string label, bool enabled, Action activate) =>
        new() { Label = label, Enabled = enabled, HoldSeconds = HoldSeconds, Activate = activate };

    private void Set(Setting setting, ConfigEntryBase entry, object value)
    {
        entry.BoxedValue = value; // saved to the .cfg straight away (SaveOnConfigSet)
        Ctx.SetStatus($"{setting.Name}: {Describe(entry.BoxedValue)}");
    }

    public override void OnReset()
    {
        // Two presses, like RESET on the upgrades: the first arms it, the second (within a few seconds) resets this group.
        if (Time.unscaledTime >= _resetConfirmUntil)
        {
            _resetConfirmUntil = Time.unscaledTime + ResetConfirmSeconds;
            Ctx.SetStatus($"Press again to reset the {GroupNames[SubTab]} settings.");
            return;
        }
        _resetConfirmUntil = 0f;
        foreach (var setting in Groups[SubTab])
        {
            var entry = Entry(setting);
            if (entry == null)
                continue;
            entry.BoxedValue = entry.DefaultValue;
            var master = Master(setting, entry.ConfigFile);
            if (master != null)
                master.BoxedValue = master.DefaultValue;
        }
        Ctx.SetStatus($"{GroupNames[SubTab]} settings back to their defaults.");
    }

    private static Setting Show(string name, string section, string key, string detail) =>
        new() { Name = name, Hud = true, Section = section, Key = key, Detail = _ => detail };

    /// <summary>A line under the HUD's unit counter; ShowKingdomInfo still hides them all.</summary>
    private static Setting Info(string name, string key, string detail, bool needsBank = false) => new()
    {
        Name = name, Hud = true, Section = "Info", Key = key, NeedsBank = needsBank, Detail = _ => detail,
        MasterSection = "Units", MasterKey = "ShowKingdomInfo",
    };

    private static ConfigEntryBase Master(Setting setting, ConfigFile config)
    {
        if (setting.MasterKey == null || config == null)
            return null;
        var key = new ConfigDefinition(setting.MasterSection, setting.MasterKey);
        return config.ContainsKey(key) && config[key].BoxedValue is bool ? config[key] : null;
    }

    private static ConfigEntryBase Entry(Setting setting)
    {
        var config = setting.Hud ? HudConfig() : Plugin.ToggleKey.ConfigFile;
        var key = new ConfigDefinition(setting.Section, setting.Key);
        return config != null && config.ContainsKey(key) ? config[key] : null;
    }

    private static ConfigFile HudConfig() =>
        IL2CPPChainloader.Instance.Plugins.TryGetValue(HudGuid, out var info) && info.Instance is BasePlugin plugin ? plugin.Config : null;

    /// <summary>
    /// Bank options only once the campaign has had a bank (so wages can still be turned off on a new island without one
    /// yet), and in the layout preview.
    /// </summary>
    private bool BankKnown() =>
        GameState.Playing() ? UpgradeStore.Flag(Wages.BankSeenFlag) || Wages.BankHere || Unlocks.HasBanker : Ctx.Preview;

    private static (float Min, float Max) Limits(Setting setting, ConfigEntryBase entry) => entry.Description?.AcceptableValues switch
    {
        AcceptableValueRange<int> r => (r.MinValue, r.MaxValue),
        AcceptableValueRange<float> r => (r.MinValue, r.MaxValue),
        _ => (setting.Min, setting.Max),
    };

    private static object Clamp(ConfigEntryBase entry, float value, float min, float max)
    {
        value = Math.Clamp(value, min, max);
        return entry.SettingType == typeof(int) ? (int)Math.Round(value) : (object)(float)Math.Round(value, 2);
    }

    private static object NextEnum(object value)
    {
        var values = Enum.GetValues(value.GetType());
        var index = Array.IndexOf(values, value);
        return values.GetValue((index + 1) % values.Length);
    }

    private static string Describe(object value) => value switch
    {
        bool b => b ? "on" : "off",
        Enum e => Words(e),
        float f => N(f),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    /// <summary>"TopLeft" -> "top left".</summary>
    private static string Words(object value)
    {
        var name = value.ToString();
        var words = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && words.Length > 0)
                words.Append(' ');
            words.Append(char.ToLowerInvariant(c));
        }
        return words.ToString();
    }

    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
