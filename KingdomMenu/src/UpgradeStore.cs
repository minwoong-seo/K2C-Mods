using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Purchased levels (and a few flags, like which statues have been found), stored per save in
/// kingdommenu.upgrades.json next to the game's own save file (AppData/LocalLow/noio/KingdomTwoCrowns/Release). Steam
/// Cloud syncs every file in that folder for this game, so the upgrades travel with the campaign to other PCs and
/// GameNative. The unmodded game never reads the file.
/// The key is the game's current campaign slot ("campaign0".."campaignN") or challenge island ("challenge3"),
/// so each save keeps its own upgrades. Kingdom-wide upgrades are stored by id; per-player ones (the steed)
/// by id + "_p1"/"_p2".
///
/// Earlier versions (and the old Kingdom Upgrades mod) kept the levels in BepInEx/config/kingdomupgrades.levels.json; it
/// is moved over the first time, unless the save folder already has a file (synced from another device), which wins.
/// </summary>
internal static class UpgradeStore
{
    private const string FileName = "kingdommenu.upgrades.json";
    /// <summary>The game's save folder under Application.persistentDataPath, the one Steam Cloud syncs.</summary>
    private const string SaveFolder = "Release";
    private static readonly string OldPath = Path.Combine(Paths.ConfigPath, "kingdomupgrades.levels.json");

    private static string _path;
    private static Dictionary<string, Dictionary<string, int>> _saves = new();

    /// <summary>Raised whenever any level changes, so stats can be re-applied immediately.</summary>
    public static event Action Changed;

    /// <summary>
    /// Read the levels the first time a kingdom is being played: by then the game (and Steam Cloud) has its save folder
    /// in place, which isn't certain while the plugin loads.
    /// </summary>
    private static void EnsureLoaded()
    {
        if (_path != null)
            return;
        _path = FindPath();

        if (!File.Exists(_path) && _path != OldPath && File.Exists(OldPath))
        {
            try
            {
                File.Copy(OldPath, _path);
                Plugin.Logger.LogInfo($"Moved the upgrade levels to {_path}, where Steam Cloud syncs them.");
                try
                {
                    File.Move(OldPath, OldPath + ".moved", true); // so it's never read (or moved) again
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning($"Couldn't rename {OldPath} (it's no longer used): {e.Message}");
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Couldn't copy {OldPath} to {_path}: {e.Message}");
                _path = OldPath; // keep using the old file rather than starting without upgrades
            }
        }

        try
        {
            if (File.Exists(_path))
                _saves = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(_path)) ?? new();
            Plugin.Logger.LogInfo($"Upgrade levels: {_path}");
        }
        catch (Exception e)
        {
            // Keep the unreadable file: the next save would overwrite it (and Steam Cloud would sync that).
            var bad = _path + ".unreadable";
            try { File.Copy(_path, bad, true); } catch { /* best effort */ }
            Plugin.Logger.LogError($"Couldn't read {_path} (copied to {bad}), starting without upgrades: {e.Message}");
            _saves = new();
        }

        if (MigratePerPlayer())
            Save();
    }

    private static string FindPath()
    {
        try
        {
            var folder = Path.Combine(Application.persistentDataPath, SaveFolder);
            if (Directory.Exists(folder))
                return Path.Combine(folder, FileName);
            Plugin.Logger.LogWarning($"The game's save folder {folder} doesn't exist; upgrade levels stay in {OldPath} and won't sync.");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Couldn't find the game's save folder ({e.Message}); upgrade levels stay in {OldPath} and won't sync.");
        }
        return OldPath;
    }

    /// <summary>
    /// Steed upgrades used to be kingdom-wide and were always paid for by player 1, so their old levels become
    /// player 1's ("steed_speed" -> "steed_speed_p1").
    /// </summary>
    private static bool MigratePerPlayer()
    {
        var changed = false;
        foreach (var levels in _saves.Values)
        {
            foreach (var upgrade in Upgrades.All.Where(u => u.PerPlayer))
            {
                if (!levels.TryGetValue(upgrade.Id, out var old))
                    continue;
                var p1 = upgrade.Key(0);
                levels[p1] = Math.Max(old, levels.TryGetValue(p1, out var existing) ? existing : 0);
                levels.Remove(upgrade.Id);
                changed = true;
            }
        }
        if (changed)
            Plugin.Logger.LogInfo("Moved existing steed upgrades to player 1.");
        return changed;
    }

    private static void Save()
    {
        try
        {
            // Written next to it first, so a crash mid-write can't leave (and sync) a half-written file.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_saves, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Couldn't write {_path}: {e.Message}");
        }
    }

    /// <summary>The save currently being played, or null on the title screen / while loading.</summary>
    public static string CurrentSaveKey()
    {
        if (!GameState.Playing())
            return null;
        EnsureLoaded();
        var global = GlobalSaveData.loaded;
        if (global == null)
            return "global";
        return global.InChallenge ? $"challenge{global.currentChallenge}" : $"campaign{global.currentCampaign}";
    }

    /// <param name="player">0 = player 1, 1 = player 2; only matters for per-player upgrades.</param>
    public static int Level(Upgrade upgrade, int player)
    {
        var key = CurrentSaveKey();
        return key != null && _saves.TryGetValue(key, out var levels) && levels.TryGetValue(upgrade.Key(player), out var level)
            ? Math.Clamp(level, 0, upgrade.MaxLevel)
            : 0;
    }

    public static void SetLevel(Upgrade upgrade, int player, int level)
    {
        var key = CurrentSaveKey();
        if (key == null)
            return;
        if (!_saves.TryGetValue(key, out var levels))
            _saves[key] = levels = new Dictionary<string, int>();
        levels[upgrade.Key(player)] = Math.Clamp(level, 0, upgrade.MaxLevel);
        Save();
        Changed?.Invoke();
    }

    /// <summary>A per-save flag kept next to the levels (e.g. "statue_found_Archer"). RESET leaves flags alone.</summary>
    public static bool Flag(string name)
    {
        var key = CurrentSaveKey();
        return key != null && _saves.TryGetValue(key, out var values) && values.TryGetValue(name, out var value) && value > 0;
    }

    public static void SetFlag(string name)
    {
        var key = CurrentSaveKey();
        if (key == null || Flag(name))
            return;
        if (!_saves.TryGetValue(key, out var values))
            _saves[key] = values = new Dictionary<string, int>();
        values[name] = 1;
        Save();
    }

    /// <summary>
    /// Clear the kingdom-wide upgrades and this player's own (steed) upgrades on the current save.
    /// The other player's steed upgrades are theirs and stay.
    /// </summary>
    public static void ResetCurrentSave(int player)
    {
        var key = CurrentSaveKey();
        if (key == null || !_saves.TryGetValue(key, out var levels))
            return;
        foreach (var upgrade in Upgrades.All)
            levels.Remove(upgrade.Key(player));
        Save();
        Changed?.Invoke();
    }
}
