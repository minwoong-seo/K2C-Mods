using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace KingdomMenu;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "kingdommenu.kingdomtwocrowns";
    public const string Name = "Kingdom Menu";
    public const string Version = "1.2.0";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<KeyCode> Player2ToggleKey;
    internal static ConfigEntry<bool> OpenWithStickClick;
    internal static ConfigEntry<bool> Player1OnTop;
    internal static ConfigEntry<bool> FreePurchases;
    internal static ConfigEntry<float> UiScale;
    internal static ConfigEntry<bool> HoldToDropCoins;
    internal static ConfigEntry<float> HoldToDropInterval;
    internal static ConfigEntry<bool> CheckForUpdates;
    internal static ConfigEntry<string> UpdateRepository;
    internal static ConfigEntry<string> UpdateApiUrl;

    public override void Load()
    {
        Logger = Log;

        ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F6,
            "Keyboard key that opens/closes player 1's menu (shop, upgrades, units, statues).");
        Player2ToggleKey = Config.Bind("General", "Player2ToggleKey", KeyCode.F9,
            "Keyboard key that opens/closes player 2's menu in local co-op. On the keyboard player 2 moves in it with U/H/J/K (their own keys), holds G to buy, switches tabs with Y/I and closes it with ` or this key.");
        OpenWithStickClick = Config.Bind("General", "OpenWithStickClick", true,
            "Click a stick (L3 or R3) on your own controller to open/close your menu. In co-op each player opens their own.");
        Player1OnTop = Config.Bind("General", "Player1OnTop", true,
            "Split screen: player 1's view is the top half (the left half when the split is side by side). Turn off if the menus appear on the wrong player's half.");
        FreePurchases = Config.Bind("General", "FreePurchases", false,
            "If true, remote purchases and upgrades cost nothing. If false, the normal price is taken from your purse.");
        UiScale = Config.Bind("General", "UiScale", 1f,
            "Size multiplier for the menu. It already scales with screen height in whole pixel steps (1080p = 3x, 1440p = 4x); try 1.25 or 1.5 for bigger.");
        HoldToDropCoins = Config.Bind("Gameplay", "HoldToDropCoins", true,
            "Holding the drop key (down) with nothing to pay for in reach keeps dropping coins until you let go, instead of one coin per press. Paying for buildings is unchanged, and it never drops gems.");
        HoldToDropInterval = Config.Bind("Gameplay", "HoldToDropInterval", 0.2f,
            "Seconds between coins while the drop key is held (they start after holding it for half a second).");
        CheckForUpdates = Config.Bind("Updates", "CheckForUpdates", true,
            "Check GitHub for newer versions of the mods when the game starts. If there are any, a popup lists them and asks before installing.");
        UpdateRepository = Config.Bind("Updates", "Repository", "minwoong-seo/K2C-Mods",
            "The GitHub repository (owner/name) whose latest release is checked.");
        UpdateApiUrl = Config.Bind("Updates", "ApiUrl", "https://api.github.com",
            "GitHub's API address. Only change this to test against a mirror.");

        // (Upgrade levels load the first time a kingdom is played, from the game's Steam Cloud save folder.)
        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(DamagePatch));
        // Esc/Start closes the menu instead of also opening the game's pause menu (see MenuFocus).
        harmony.PatchAll(typeof(BlockPausePatch));
        // Pauses the coin magnet when a full purse spills a coin.
        harmony.PatchAll(typeof(PurseSpillPatch));
        // Drops new coins into a bigger purse from above its rim.
        harmony.PatchAll(typeof(PurseSpawnPatch));
        // Holding the drop key keeps dropping coins.
        harmony.PatchAll(typeof(HoldToDropPatch));
        // The Gallop toggle upgrade: tap to gallop, tap again to stop.
        harmony.PatchAll(typeof(GallopTogglePatch));

        // Checks for newer versions of the mods in the background; MenuHost shows the popup.
        Updates.Start();

        AddComponent<MenuHost>();
        var stick = OpenWithStickClick.Value ? " (or L3/R3 on a controller)" : "";
        Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value}{stick} in-game to open the menu.");
    }
}
