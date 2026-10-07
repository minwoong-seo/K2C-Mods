using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace KingdomMenu;

/// <summary>One mod with a newer version in the latest release.</summary>
internal sealed class ModUpdate
{
    public string Guid, Name, Folder, Installed, Latest;
}

/// <summary>The latest release on GitHub: its version, the mods it has (from its mods.json) and its mods-only zip.</summary>
internal sealed class LatestRelease
{
    public string Tag;
    public string ZipUrl;
    public long ZipSize;
    public string ZipDigest;
    public List<(string Guid, string Name, string Version, string Folder)> Mods = new();
}

/// <summary>
/// Checks the public repo's latest release for newer versions of the installed mods, and installs them when the player
/// says yes (see UpdatePrompt). The network work runs off the main thread so the game never waits for it. Installing
/// happens while the game runs: the mods' DLLs are in use, so each is renamed out of the way (Windows allows that for a
/// loaded DLL) and the new one written in its place; the new versions load on the next start, and the renamed old
/// files are deleted then.
/// </summary>
internal static class Updates
{
    /// <summary>The release's zip with just the mods (no BepInEx), and its list of the mods and their versions.</summary>
    private const string ZipPrefix = "KingdomTwoCrowns-Mods-Only-";
    private const string ManifestName = "mods.json";
    private const string OldSuffix = ".updater-old";
    private const string NewSuffix = ".updater-new";
    private const float CheckTimeoutSeconds = 30f;
    private const float DownloadTimeoutSeconds = 120f;

    private static Task<LatestRelease> _check;

    /// <summary>The background check is still running.</summary>
    public static bool Pending => _check != null && !_check.IsCompleted;

    /// <summary>At startup: clear up after the last update, then start checking in the background.</summary>
    public static void Start()
    {
        RemoveLeftovers();
        if (!Plugin.CheckForUpdates.Value)
            return;
        var api = Plugin.UpdateApiUrl.Value.TrimEnd('/');
        var repo = Plugin.UpdateRepository.Value.Trim();
        _check = Task.Run(() => FetchLatest(api, repo));
    }

    /// <summary>
    /// Once the check is done (main thread): the release and the installed mods it has newer versions of. False while
    /// it's still running, and when there's nothing to update or the check failed (logged once).
    /// </summary>
    public static bool TryTakeResult(out LatestRelease release, out List<ModUpdate> updates)
    {
        release = null;
        updates = null;
        if (_check == null || !_check.IsCompleted)
            return false;
        var check = _check;
        _check = null;
        if (check.IsFaulted || check.IsCanceled)
        {
            Plugin.Logger.LogWarning($"Couldn't check for updates: {check.Exception?.GetBaseException().Message ?? "timed out"}");
            return false;
        }
        release = check.Result;
        if (release == null)
            return false;

        // Only mods that are installed (and loaded): one you've removed stays removed.
        var installed = IL2CPPChainloader.Instance.Plugins;
        updates = new List<ModUpdate>();
        foreach (var mod in release.Mods)
        {
            if (!installed.TryGetValue(mod.Guid, out var info))
                continue;
            var current = info.Metadata.Version.ToString();
            if (IsNewer(mod.Version, current))
                updates.Add(new ModUpdate { Guid = mod.Guid, Name = mod.Name, Folder = mod.Folder, Installed = current, Latest = mod.Version });
        }
        if (updates.Count == 0)
        {
            Plugin.Logger.LogInfo($"The mods are up to date (latest release {release.Tag}).");
            return false;
        }
        Plugin.Logger.LogInfo($"Updates in {release.Tag}: {string.Join(", ", updates.Select(u => $"{u.Name} {u.Installed} to {u.Latest}"))}");
        return true;
    }

    /// <summary>Download the release's zip and install those mods' files (off the main thread); returns how many files changed.</summary>
    public static Task<int> Install(LatestRelease release, List<ModUpdate> updates) => Task.Run(() => DownloadAndInstall(release, updates));

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // each request has its own deadline
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"KingdomMenu/{Plugin.Version}");
        return http;
    }

    private static LatestRelease FetchLatest(string api, string repo)
    {
        using var http = NewClient();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(CheckTimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{api}/repos/{repo}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = http.SendAsync(request, cancel.Token).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Plugin.Logger.LogInfo($"{repo} has no release yet; nothing to update.");
            return null;
        }
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(response.Content.ReadAsStringAsync(cancel.Token).GetAwaiter().GetResult());

        var release = new LatestRelease { Tag = json.RootElement.GetProperty("tag_name").GetString() };
        string manifestUrl = null;
        foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
                manifestUrl = url;
            else if (name.StartsWith(ZipPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                release.ZipUrl = url;
                release.ZipSize = asset.TryGetProperty("size", out var size) ? size.GetInt64() : -1;
                release.ZipDigest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            }
        }
        if (manifestUrl == null || release.ZipUrl == null)
        {
            Plugin.Logger.LogWarning($"Release {release.Tag} has no {ManifestName} or {ZipPrefix}*.zip; nothing to update.");
            return null;
        }

        using var manifest = JsonDocument.Parse(http.GetStringAsync(manifestUrl, cancel.Token).GetAwaiter().GetResult());
        foreach (var mod in manifest.RootElement.GetProperty("mods").EnumerateArray())
        {
            release.Mods.Add((mod.GetProperty("guid").GetString(), mod.GetProperty("name").GetString(),
                mod.GetProperty("version").GetString(), mod.GetProperty("folder").GetString()));
        }
        return release;
    }

    private static int DownloadAndInstall(LatestRelease release, List<ModUpdate> updates)
    {
        byte[] bytes;
        using (var http = NewClient())
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds)))
            bytes = http.GetByteArrayAsync(release.ZipUrl, cancel.Token).GetAwaiter().GetResult();
        if (release.ZipSize >= 0 && release.ZipSize != bytes.Length)
            throw new InvalidDataException($"the download is {bytes.Length} bytes, the release says {release.ZipSize}");
        // GitHub lists a SHA-256 for each release file; check it when it's there.
        if (release.ZipDigest != null && release.ZipDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(release.ZipDigest.Substring(7), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("the download doesn't match the release's checksum");

        // Only the folders of the mods being updated, and never anything outside BepInEx/plugins.
        var root = Path.GetFullPath(Paths.GameRootPath);
        var plugins = Path.GetFullPath(Paths.PluginPath) + Path.DirectorySeparatorChar;
        var folders = updates.Select(u => Path.GetFullPath(Path.Combine(root, u.Folder)) + Path.DirectorySeparatorChar).ToArray();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = new List<(string Target, ZipArchiveEntry Entry)>();
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/"))
                continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (target.StartsWith(plugins, StringComparison.OrdinalIgnoreCase) && folders.Any(f => target.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                files.Add((target, entry));
        }
        if (files.Count == 0)
            throw new InvalidDataException("the release zip doesn't have those mods");

        // Unpack everything next to its target first, so a broken zip leaves the installed mods alone; then swap.
        foreach (var (target, entry) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            entry.ExtractToFile(target + NewSuffix, overwrite: true);
        }
        var changed = 0;
        foreach (var (target, _) in files)
        {
            var fresh = target + NewSuffix;
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(fresh)))
            {
                File.Delete(fresh);
                continue;
            }
            Swap(target, fresh);
            changed++;
            Plugin.Logger.LogInfo($"Updated {Path.GetRelativePath(root, target)}");
        }
        Plugin.Logger.LogInfo($"Installed {release.Tag}: {changed} file(s); the new versions load on the next start.");
        return changed;
    }

    private static void Swap(string target, string fresh)
    {
        if (!File.Exists(target))
        {
            File.Move(fresh, target);
            return;
        }
        try
        {
            File.Move(fresh, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // In use (a loaded mod): it can't be overwritten, but it can be renamed.
            File.Move(target, target + OldSuffix, overwrite: true);
            File.Move(fresh, target);
        }
    }

    /// <summary>"1.0.2" vs "1.0.1" (a leading v is fine); false when either isn't a version.</summary>
    private static bool IsNewer(string latest, string installed) =>
        System.Version.TryParse(latest?.TrimStart('v', 'V'), out var a) && System.Version.TryParse(installed?.TrimStart('v', 'V'), out var b) && a > b;

    /// <summary>Delete the old files a previous update renamed (no longer loaded now) and any it didn't get to swap in.</summary>
    private static void RemoveLeftovers()
    {
        if (!Directory.Exists(Paths.PluginPath))
            return;
        foreach (var file in Directory.EnumerateFiles(Paths.PluginPath, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase) && !file.EndsWith(NewSuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogDebug($"Couldn't delete {file} yet: {e.Message}");
            }
        }
    }
}
