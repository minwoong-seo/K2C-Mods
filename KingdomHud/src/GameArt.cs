using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace KingdomHud;

/// <summary>
/// Looks up the game's own pixel fonts and sprites by name, so the HUD is drawn with the game's art.
/// Everything is optional: a missing sprite is skipped or replaced by a flat colour, a missing font by any loaded font.
/// </summary>
internal class GameArt
{
    private readonly Dictionary<string, Sprite> _sprites = new();

    public Font Body { get; private set; }
    public Font Title { get; private set; }

    public static GameArt Load(IEnumerable<string> spriteNames)
    {
        var art = new GameArt();

        Font anyFont = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Font>()))
        {
            var font = obj.TryCast<Font>();
            if (font == null)
                continue;
            anyFont ??= font;
            if (font.name == "Kingdom")
                art.Body = font;
            else if (font.name == "KingdomMenu")
                art.Title = font;
        }
        art.Body ??= anyFont;
        art.Title ??= art.Body;

        var wanted = new HashSet<string>(spriteNames);
        foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Sprite>()))
        {
            var name = obj.name;
            if (!wanted.Contains(name) || art._sprites.ContainsKey(name))
                continue;
            var sprite = obj.TryCast<Sprite>();
            if (sprite != null)
                art._sprites[name] = sprite;
        }

        Plugin.Logger.LogInfo($"HUD art: fonts {art.Body?.name}/{art.Title?.name}, {art._sprites.Count}/{wanted.Count} sprites found.");
        return art;
    }

    public Sprite Get(string name) => name != null && _sprites.TryGetValue(name, out var s) && s != null ? s : null;
}
