using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Looks up the game's own pixel fonts and menu/item/unit sprites, so the menu is drawn with the same art as the game's
/// menus. Everything is optional: a missing sprite falls back to a flat colour and a missing font to any loaded font.
/// </summary>
internal class GameArt
{
    public static readonly string[] CoinFrames =
        { "coin_spin_0", "coin_spin_1", "coin_spin_2", "coin_spin_3", "coin_spin_4", "coin_spin_5", "coin_spin_6", "coin_spin_7" };

    private static readonly string[] Ui =
    {
        "menu_panel_background", "menu_crown", "baggem",
        "menu_button_wood_normal", "menu_button_wood_highlight", "menu_button_wood_pressed", "menu_button_wood_disabled",
        "menu_button_frame_highlight",
        "tools_bow", "tools_hammer", "tools_scythe", "tools_pike", "tools_npcshield_norselands",
        "map_icon_archerstatue", "map_icon_archerstatue_locked", "map_icon_workerstatue", "map_icon_workerstatue_locked",
        "map_icon_knightstatue", "map_icon_knightstatue_locked", "map_icon_farmerstatue", "map_icon_farmerstatue_locked",
        "map_icon_pikeman_statue_greece", "map_icon_pikeman_statue_locked_greece",
    };

    private readonly Dictionary<string, Sprite> _sprites = new();
    private readonly System.IntPtr[] _steedOf = new System.IntPtr[Coop.MaxPlayers];
    private readonly Sprite[] _steedSprite = new Sprite[Coop.MaxPlayers];

    /// <summary>Regular mixed-case pixel font ("KINGDOM Main v2").</summary>
    public Font Body { get; private set; }

    /// <summary>Blocky all-caps menu font ("KINGDOM Menu v2"), as used for "PRESS TO SELECT".</summary>
    public Font Title { get; private set; }

    private static GameArt _shared;

    /// <summary>The art, loaded the first time something needs it (the menus and the update popup share it).</summary>
    public static GameArt Shared => _shared ??= Load();

    public static GameArt Load()
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

        var wanted = new HashSet<string>(Ui);
        wanted.UnionWith(CoinFrames);
        foreach (var upgrade in Upgrades.All)
            wanted.UnionWith(upgrade.Icons);
        wanted.UnionWith(Unlocks.IconNames);
        foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Sprite>()))
        {
            var name = obj.name;
            if (!wanted.Contains(name) || art._sprites.ContainsKey(name))
                continue;
            var sprite = obj.TryCast<Sprite>();
            if (sprite != null)
                art._sprites[name] = sprite;
        }

        Plugin.Logger.LogInfo($"Menu art: fonts {art.Body?.name}/{art.Title?.name}, {art._sprites.Count}/{wanted.Count} sprites found.");
        return art;
    }

    public Sprite Get(string name) => name != null && _sprites.TryGetValue(name, out var s) && s != null ? s : null;

    /// <summary>The item a stand sells, for stands whose item prefab has no sprite.</summary>
    public Sprite IconFor(PayableShop.ShopType? type) => type switch
    {
        PayableShop.ShopType.Bow => Get("tools_bow"),
        PayableShop.ShopType.Hammer or PayableShop.ShopType.WorkshopLeft or PayableShop.ShopType.WorkshopRight => Get("tools_hammer"),
        PayableShop.ShopType.Scythe => Get("tools_scythe"),
        PayableShop.ShopType.Pike_OLDHANDLE or PayableShop.ShopType.PikeLeft or PayableShop.ShopType.PikeRight => Get("tools_pike"),
        PayableShop.ShopType.ShieldShopLeft or PayableShop.ShopType.ShieldShopRight => Get("tools_npcshield_norselands"),
        _ => null,
    };

    /// <summary>
    /// First available icon. "steed" is that player's current mount as it looks right now, "soldier" the first soldier
    /// kind the kingdom has unlocked, and "wall" its best wall, so icons never show something the player hasn't reached.
    /// </summary>
    public Sprite IconFor(Upgrade upgrade, int player)
    {
        foreach (var name in upgrade.Icons)
        {
            var sprite = name switch
            {
                "steed" => SteedSprite(player),
                "soldier" => Get(Unlocks.SoldierIcon()),
                "wall" => Unlocks.BestWallSprite(),
                _ => Get(name),
            };
            if (sprite != null)
                return sprite;
        }
        return Get("menu_crown");
    }

    /// <summary>One frame of the player's steed, taken again only when they change mounts (so the icon doesn't flicker).</summary>
    public Sprite SteedSprite(int player)
    {
        var steed = Coop.Monarch(player)?.steed;
        if (steed == null)
            return null;
        if (_steedOf[player] != steed.Pointer || _steedSprite[player] == null)
        {
            var renderer = steed.GetComponentInChildren<SpriteRenderer>(true);
            _steedSprite[player] = renderer != null ? renderer.sprite : null;
            _steedOf[player] = steed.Pointer;
        }
        return _steedSprite[player];
    }
}
