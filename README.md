# Kingdom Two Crowns mods

BepInEx 6 (IL2CPP) mods for **Kingdom Two Crowns** v2.4.2 on Steam (Windows). They're drawn with the game's own
pixel fonts and sprites, and work with keyboard, mouse and controller, including local co-op.

| Folder | What it is | Keys |
|---|---|---|
| [`KingdomMenu/`](KingdomMenu/) | One menu with tabs: a remote shop, coin upgrades (saved per campaign), unit stats and statues. It shows nothing you haven't unlocked yet. Also: hold the drop key to keep dropping coins, and a popup when updates are out. | F6 / L3 / R3 |
| [`KingdomHud/`](KingdomHud/) | Stamina bar under your horse, unit counter, banker interest, blood-moon countdown, labels for nearby things | F7 |
| [`BepInEx-Fix/`](BepInEx-Fix/) | Patched Cpp2IL. Without it, BepInEx can't load **any** plugin on this game version. | |

Each folder has a `README.md`, its C# source in `src/`, and a ready-to-install build in `package/` (laid out like
the game folder).

## Install

1. Install **BepInEx 6.0.0-be.785, Unity IL2CPP, Windows x64** into the game folder (the one with
   `KingdomTwoCrowns.exe`). It's build #785 on [BepInEx's bleeding-edge builds](https://builds.bepinex.dev/projects/bepinex_be).
   Other BepInEx builds won't work with the fix.
2. Download `KingdomTwoCrowns-Mods-Only-v<version>.zip` from [**Releases**](../../releases/latest) and extract it into
   the game folder, merging folders and replacing files.
3. Start the game from Steam. The first start takes a minute or two while BepInEx generates its interop files
   (`BepInEx/interop`); later starts are quick.

F6 (or L3/R3 on a controller) opens the menu in a kingdom, F7 toggles the HUD.

## Updates

When the game starts, Kingdom Menu checks this repo's latest release in the background. If it has newer versions of
mods you have installed, a popup lists them and asks: **UPDATE** installs them (they load the next time you start the
game), **NOT NOW** asks again next time. If GitHub can't be reached, nothing happens. To turn the check off, set
`CheckForUpdates = false` in `BepInEx/config/kingdommenu.kingdomtwocrowns.cfg`. See
[`KingdomMenu/README.md`](KingdomMenu/README.md#updates).

## Android (GameNative)

GameNative runs the Windows game through Wine, so the same setup works there:

1. Set everything up on a PC first and start the game once, so BepInEx generates `BepInEx/interop` (generating it on
   the phone is very slow).
2. Copy these from the PC's game folder into the GameNative game folder: `winhttp.dll`, `doorstop_config.ini`,
   `.doorstop_version`, `dotnet/` and `BepInEx/`.
3. In the game's container settings, turn on **Load mods** (General tab), and add the environment variable
   `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` = `1`. Without it, BepInEx's .NET runtime shuts the game down at start.

## Building

Each `src/` folder builds with `dotnet build -c Release` (.NET SDK 6 or newer) against a game folder where BepInEx
has already generated `BepInEx/interop`. See each folder's README.

Not affiliated with or endorsed by Raw Fury or the makers of Kingdom Two Crowns.
