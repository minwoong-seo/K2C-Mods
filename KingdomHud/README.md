# Kingdom HUD (Kingdom Two Crowns, BepInEx 6 IL2CPP)

An information HUD drawn with the game's own sprites and fonts:

- **Stamina bar under your horse.** It's drawn in the game world, so it follows your mount (in split-screen too) and
  uses the game's pixel grid. Colors:
  - green: plenty of stamina
  - amber: getting low
  - red: almost empty
  - blinking: exhausted
  - gold: well fed. After grazing, the game doesn't drain stamina at all for a while (`WellFedTimer`). The gold bar
    counts that time down and blinks just before it ends. This is why stamina sometimes doesn't go down.
  It fades away once the horse is fully rested.
- **Unit counter** in a corner (top-left by default, clear of the game's coin purse), with each job's in-game sprite and count. Vagrants, villagers, builders and archers
  are always shown. Other jobs stay hidden until you unlock them, so the HUD doesn't spoil them: farmers once a farm
  is built, knights, pikemen and ninjas once you have one or their stand is placed, the rest once you have one. A
  job stays listed after it's unlocked, even at 0. Icons switch to the current biome's look once a unit of that kind is around.
- **Farmers show used/slots**, e.g. `3/6`. That's your farmers against the farmer slots your built farms provide:
  each farmhouse's farmland limit (`Farmhouse.CurrentMaxFarmlands`), including the farmer statue's bonus once it's
  active. Stables don't count.
- **Purse:** the coins (and gems, if any) in your bag, for both players in co-op.
- **Kingdom info** under the counter, drawn with the game's own icons:
  - **Bank:** a coin crate with the banker's stash, a sun with the interest it adds at the next dawn (computed exactly
    like the game: `min(maxInterest, ceil(stash x dailyInterest))`), and a gold bar filling up to the stash that
    earns the most interest ("max" once it does).
  - **Cottages:** a villager with one pip per cottage slot (lit = ready to hire), a bar filling up to the next refill,
    and the seconds left. Each cottage refills one villager on a timer up to its slots, and paying it hands one over.
  - **Blood moon:** a red moon and the days until the next one, turning gold the day before and red on the night.
- **Nearby labels.** When you stand at something you can pay for or use, its name appears above its coin markers in
  the game's in-world hint style, e.g. "Bow shop", "Town center (3)", "Hermit of the horn", "Griffin (mount)",
  "Gem chest".

**Local co-op:**
- Each monarch gets their own stamina bar and nearby label.
- On a shared view, one panel shows both purses ("P1 12   P2 7").
- When the screen splits (top/bottom, or side by side if you switch the game's split), each player gets their own
  panel in the corner of their own half, with their own purse and the shared kingdom info. If they show up on the
  wrong halves, turn off `Player1OnTop`.

**F7** shows or hides everything. For a controller, set `ControllerButton` (e.g. `JoystickButton6` = Back/View), or bind a button to F7 with Steam Input. Each part can be turned off in `BepInEx/config/kingdomhud.kingdomtwocrowns.cfg`,
or with F1 in Configuration Manager:
- `ShowStaminaBar`, `AutoHide`
- `ShowUnitCounter`, `ShowPurse`, `ShowKingdomInfo`, `Position` (TopLeft by default; TopRight sits over the game's coin purse;
  BottomLeft and BottomRight also work), `UiScale` (the counter scales with screen height in whole
  pixels: 720p 2x, 1080p 3x, 1440p 4x, and UiScale multiplies that)
- `ShowNearbyLabels`.

## What's in this folder

```
KingdomHud/
  README.md
  package/BepInEx/plugins/KingdomHud/KingdomHud.dll   the mod
  src/                                               source code (C#)
```

## Install (this PC or another one)

Same requirements as the other mods: BepInEx 6.0.0-be.785 IL2CPP plus the fix in `BepInEx-Fix/` (see its README).
Then copy everything inside `KingdomHud/package/` into the game folder and start the game.

To uninstall, delete `BepInEx/plugins/KingdomHud`.

## Build

```
cd src
dotnet build -c Release
```

From outside the game folder, add `-p:GameDir="path\to\Kingdom Two Crowns"`. The build updates `package/` and,
when the game isn't running, the game's `BepInEx/plugins/KingdomHud/`.

To check the HUD on the title screen without loading a save, create an empty file
`BepInEx/config/kingdomhud.preview` and restart the game. It then shows with sample numbers. Delete the file
afterwards.

## Notes

- Counts come from the kingdom's own lists (`Kingdom.Archers`, `Workers`, `Farmers`, `Knights`, `Pikemen`,
  `Berserkers`, `Beggars`, stable keepers). Villagers, ninjas and warriors are counted from the active units in the
  world.
- Names for nearby things come from the object's type (shops, hermits, mounts, town center level), otherwise from
  the building's prefab name. If you spot an odd one, the BepInEx log (debug level) has a
  `Near: <type> '<object>' -> '<label>'` line to improve it from.
