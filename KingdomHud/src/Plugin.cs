using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace KingdomHud;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "kingdomhud.kingdomtwocrowns";
    public const string Name = "Kingdom HUD";
    public const string Version = "1.1.0";

    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    internal static ManualLogSource Logger;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<KeyCode> ControllerButton;
    internal static ConfigEntry<bool> ShowStaminaBar;
    internal static ConfigEntry<bool> AutoHideStaminaBar;
    internal static ConfigEntry<bool> ShowUnitCounter;
    internal static ConfigEntry<bool> ShowKingdomInfo;
    internal static ConfigEntry<bool> ShowPurse;
    internal static ConfigEntry<bool> ShowNearbyLabels;
    internal static ConfigEntry<Corner> CounterCorner;
    internal static ConfigEntry<float> UiScale;
    internal static ConfigEntry<bool> Player1OnTop;

    public override void Load()
    {
        Logger = Log;

        ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F7, "Key that shows/hides the whole HUD.");
        ControllerButton = Config.Bind("General", "ControllerButton", KeyCode.None,
            "Controller button that shows/hides the HUD (e.g. JoystickButton6 = Back/View on an Xbox-layout pad). " +
            "None by default; L3 and R3 open the Kingdom Menu.");
        ShowStaminaBar = Config.Bind("Stamina", "ShowStaminaBar", true, "Show a stamina bar under your horse.");
        AutoHideStaminaBar = Config.Bind("Stamina", "AutoHide", true,
            "Fade the stamina bar out when the horse is fully rested; it comes back as soon as stamina is used.");
        ShowUnitCounter = Config.Bind("Units", "ShowUnitCounter", true, "Show how many of each job your kingdom has.");
        ShowKingdomInfo = Config.Bind("Units", "ShowKingdomInfo", true,
            "Under the unit counter, show the banker's stash with the interest it pays at dawn, villagers waiting in cottages, and days until the next blood moon.");
        ShowPurse = Config.Bind("Units", "ShowPurse", true, "Show how many coins (and gems) are in your purse.");
        ShowNearbyLabels = Config.Bind("Labels", "ShowNearbyLabels", true,
            "Show the name of whatever you're standing at (walls, towers, shops, hermits, statues, mounts...).");
        // New key (was "Corner", default TopRight) so existing configs move off the game's coin purse.
        CounterCorner = Config.Bind("Units", "Position", Corner.TopLeft,
            "Screen corner for the unit counter. The game shows your coin purse at the top right, so that corner is best avoided.");
        UiScale = Config.Bind("Units", "UiScale", 1f,
            "Size multiplier for the unit counter. It already scales with screen height in whole pixel steps.");

        Player1OnTop = Config.Bind("General", "Player1OnTop", true,
            "Split screen: player 1's view is the top half (the left half when the split is side by side). Turn off if the panels appear on the wrong player's half.");

        AddComponent<Hud>();
        Log.LogInfo($"{Name} {Version} loaded. Press {ShortcutLabel()} to show/hide the HUD.");
    }

    /// <summary>Short name for a shortcut: L3/R3 and pad buttons as players know them (Xbox layout, as Steam Input and Remote Play present pads).</summary>
    internal static string ButtonName(KeyCode key) => key switch
    {
        KeyCode.JoystickButton0 => "A",
        KeyCode.JoystickButton1 => "B",
        KeyCode.JoystickButton2 => "X",
        KeyCode.JoystickButton3 => "Y",
        KeyCode.JoystickButton4 => "LB",
        KeyCode.JoystickButton5 => "RB",
        KeyCode.JoystickButton6 => "Back",
        KeyCode.JoystickButton7 => "Start",
        KeyCode.JoystickButton8 => "L3",
        KeyCode.JoystickButton9 => "R3",
        _ => key.ToString(),
    };

    /// <summary>"F6" or "F6 or L3" for on-screen hints.</summary>
    internal static string ShortcutLabel() =>
        ControllerButton.Value == KeyCode.None ? ToggleKey.Value.ToString() : $"{ToggleKey.Value} or {ButtonName(ControllerButton.Value)}";

    /// <summary>Whether the keyboard key or the controller button for this mod was pressed this frame.</summary>
    internal static bool TogglePressed(BepInEx.IInputSystem input) =>
        input.GetKeyDown(ToggleKey.Value) || (ControllerButton.Value != KeyCode.None && input.GetKeyDown(ControllerButton.Value));
}
