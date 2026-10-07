# Kingdom Menu (Kingdom Two Crowns, BepInEx 6 IL2CPP)

One menu, one key, four tabs:

| Tab | What it does |
|---|---|
| **SHOP** | Buy bows, hammers, scythes, pikes, shields and other stand items from anywhere in your kingdom. |
| **UPGRADES** | Spend coins on permanent upgrades for workers, army, your monarch and steed, and the kingdom (walls, bank, farms), saved per campaign. |
| **UNITS** | How many of each job you have, with their live speed, work, reload and damage numbers. |
| **STATUES** | The statues you've found: locked, asleep or active, what each blessing does, and the time statue's countdown. |

Also, outside the menu: **hold the drop key to keep dropping coins** (see [Hold to drop coins](#hold-to-drop-coins)),
and an **update popup** when newer versions of the mods are out (see [Updates](#updates)).

It replaces the separate **Remote Shop** (F6) and **Kingdom Upgrades** (F8) mods. Install only this one; see
[Upgrading from Remote Shop / Kingdom Upgrades](#upgrading-from-remote-shop--kingdom-upgrades).

**No spoilers:** nothing you haven't reached shows up. Units, upgrades and icons for knights, pikemen, berserkers and
ninjas stay hidden until your kingdom has one, or their stand has been placed. Wall icons show your own best wall.
Statues appear once you've been near them.

## What's in this folder

```
KingdomMenu/
  README.md          this file
  package/           ready to install: the same layout as the game folder
    BepInEx/plugins/KingdomMenu/KingdomMenu.dll   the mod
  src/               source code (C#)
```

**Requirement:** BepInEx 6.0.0-be.785 (IL2CPP) with the fix in the sibling folder `BepInEx-Fix/`. Without it,
BepInEx can't load any plugins on this game version.

## Install (this PC or another one)

1. Install BepInEx and apply `BepInEx-Fix/package/`. See `BepInEx-Fix/README.md`.
2. Copy everything inside `KingdomMenu/package/` into the game folder (`...\steamapps\common\Kingdom Two Crowns`).
3. Start the game **from Steam**. In a kingdom, press **F6**, or click a stick (**L3** or **R3**) on your controller.

To uninstall, delete `BepInEx/plugins/KingdomMenu`. Purchased upgrade levels stay in `kingdommenu.upgrades.json` in
the game's save folder (see [Upgrades](#upgrades)) until you delete that file too; the unmodded game ignores it.

## Layout and controls

The tabs run down the left side of the panel, and the page fills the rest. The header shows your coins (and gems,
if you have any), plus the page's buttons: PRICE on Shop and Upgrades, RESET on Upgrades. Upgrades has a second row
of tabs across the top of the page for its categories (Workers, Army, Monarch, Kingdom).

Pages show up to 6 rows (at least 4: in a short split-screen half the menu shows fewer rows rather than shrinking).
Longer ones scroll: moving past the last row scrolls down, the mouse wheel scrolls the panel under the cursor, and a
gold mark on the line between the tabs and the page shows where you are. Rows without buttons
(Units, Statues) can be stepped through too: press right from the tab, then up/down.

| | Controller | Keyboard | Mouse |
|---|---|---|---|
| Open / close | L3 or R3 (click a stick) | F6 (player 2: F9) | |
| Close | B, Start, or a stick click | Esc or Backspace | |
| Next / previous tab | RB / LB | E / Q, Tab / Shift+Tab, PgDn / PgUp | Click a tab |
| Move focus | Left stick or d-pad | WASD or arrow keys | Hover |
| Buy | **Hold** A on BUY or FILL | **Hold** Space/Enter, or **hold 1-9** for that row (Shift+1-9: FILL) | **Hold** BUY or FILL |
| Press PRICE, RESET or a tab | A | Space/Enter | Click |

Player 2's keyboard keys are listed under [Local co-op](#local-co-op).

- **Moving around:** focus moves to the nearest button in the direction you press. Up and down walk the tab column
  or the page's rows, left from a row goes back to the tabs, and right from the tabs goes into the page. Moving onto
  a tab opens it. One push is one step: holding a direction doesn't keep scrolling, so let the stick come back to the
  centre (or let go of the key) before the next step.
- **Hold to buy:** BUY and FILL fill from left to right while held, and the purchase happens when they're full.
  Letting go early cancels. Pricier things take longer (0.4 s to 1.6 s), and FILL a little longer still. After a
  purchase you have to let go before the next one starts.
- While the menu is open your monarch stands still and the menu has your controls. Esc or Start only close the menu;
  they don't also open the pause menu.
- The menu opens only while you're playing, never over the game's pause menu, dialogs or loading screens (it closes
  itself if one of those comes up).
- **PRICE: NORMAL / FREE** switches whether shop purchases and upgrades cost coins.

## Shop

- One row per kind of stand the game has actually built: stands are spawned as the town center is upgraded, and
  farmhouses carry their own scythe stand. Stands still under construction, or being removed, are hidden.
- Rows stay visible but greyed out while the stand is full, you don't have the crown, or you can't afford it.
- **BUY** buys one item. **FILL** keeps buying until every stand of that type is full or you run out of coins.
- The price is drawn as coins like the game's own cost indicators (one coin and "x12" above five). The pips show
  what's lying on the stands.
- Each purchase goes through the game's own payment code (`Payable.TransactionComplete`). The item appears on the
  stand exactly as if you had dropped coins there, and the stand's price increase still applies.

## Upgrades

Each level adds a fixed amount. The next level costs `base × 1.5^level` coins (rounded). The pips show the level,
and MAX means fully upgraded. **RESET** (press twice, it shows "SURE?") clears the kingdom-wide upgrades and **your
own** per-player upgrades (steed, coin magnet, quick hands, bigger purse) on the current save. **No coins are refunded.**

| Category | Upgrade | Per level | Max | Costs | What it changes in the game |
|---|---|---|---|---|---|
| Workers | Builder work speed | x1.15 | 5 | 4, 6, 9, 14, 20 | `Worker.workTime` ÷ multiplier (time per work tick) |
| Workers | Builder movement | x1.10 | 5 | 3, 5, 7, 10, 15 | `Worker.walkSpeed`, `runSpeed` |
| Workers | Villager movement | x1.10 | 5 | 3, 5, 7, 10, 15 | `Peasant.walkSpeed/runSpeed`, `Farmer._walkSpeed/_runSpeed/_fleeSpeed` |
| Workers | Faster recruits | x1.25 | 3 | 5, 8, 11 | Cottage refill (`CitizenHousePayable._cooldownOfSpawning`, 200 s) and vagrant camp spawns (`BeggarCamp.spawnInterval`, 120 s) ÷ multiplier |
| Army | Archer fire rate | x1.15 | 5 | 5, 8, 11, 17, 25 | `Archer.shootCooldownTime`, `shootCooldownWithKnightTime`, `playerShootCooldownTime` ÷ multiplier |
| Army | Archer damage | x1.25 | 4 | 6, 9, 14, 20 | Damage from archers, scaled per hit |
| Army | Archer range | x1.10 | 3 | 6, 9, 14 | `Archer.shootRange` (5.5) and `towerShootRange` (8.25) |
| Army | Soldier damage | x1.25 | 4 | 6, 9, 14, 20 | Damage from knights, pikemen, berserkers, ninjas, scaled per hit. Hidden until the kingdom has one of them (or its stand) |
| Army | Soldier movement | x1.10 | 5 | 3, 5, 7, 10, 15 | `Archer.walkSpeed/runSpeed`, `Knight._walkSpeed/_runSpeed/_retreatSpeed`, `Pikeman._runSpeed` |
| Monarch | Steed stamina (per player) | x1.20 | 5 | 4, 6, 9, 14, 20 | Stamina drain ÷ multiplier, recovery × multiplier, `reserveStamina` × multiplier |
| Monarch | Steed speed (per player) | x1.06 | 5 | 5, 8, 11, 17, 25 | `Steed.walkSpeed`, `runSpeed` |
| Monarch | Coin magnet (per player) | reach 1.5 / 2.5 / 3.5 / 4.5 | 4 | 4, 6, 9, 14 | Collects loose coins and gems within reach (see below) |
| Monarch | Quick hands (per player) | x1.25 | 3 | 3, 5, 7 | `Player.timeBetweenCoins` (0.3 s per coin while paying) ÷ multiplier |
| Monarch | Bigger purse (per player) | bag x1.25 each way | 3 | 6, 10, 15 | The purse's physics bag, so it holds about 25 / 39 / 56 / 77 coins before spilling (see below) |
| Kingdom | Wall toughness | x1.20 | 5 | 5, 8, 11, 17, 25 | Damage taken by walls ÷ multiplier, per hit |
| Kingdom | Wall repair | x1.25 | 4 | 4, 6, 9, 14 | `Wall.repairRate` (hit points restored per repair tick) |
| Kingdom | Bank interest | x1.25 | 4 | 10, 16, 26, 41 | `Banker.dailyInterest` (10 per 100) and `maxInterest` (8 a day, up to 16). Hidden until you have a banker |
| Kingdom | Harvest yield | +1 coin | 3 | 25, 40, 64 | `Farmland.coinYield` (6 coins per plot harvest, up to 9). Hidden until you have a farm |

**Balance.** The convenience upgrades are cheap. The two income upgrades are priced as investments so they can't
snowball: a Bank interest level earns at most about 2 more coins a day, so it pays for itself in 5 to 20 days; a
Harvest yield level adds 1 coin to every plot harvest, about +17% farm income. Military upgrades sit in between.
Everything together costs roughly 900 coins, a long campaign's worth.

**Coin magnet.** A monarch with the magnet collects coins and gems lying within reach (about 20 units fit across the
screen) through the game's own pickup, with its sound and purse, one coin at a time. It leaves alone coins that are
still flying or moving somewhere, coins a unit is walking to (a vagrant going for one), coins this monarch dropped
(thrown to vagrants, the banker or a stand, or spilled from a full purse), and coins the game says that monarch can't
take yet. It stops while the purse is nearly full and pauses for 20 seconds after a coin spills (see below), so it
never empties the ground into a full purse. Host or single-player only.

**The purse overflows.** The purse at the top of the screen is a small physics bag: each coin (and gem) is a little
body that drops in and piles up. It holds about 25 coins (a gem takes about two coins' room). Once the pile reaches the
rim, every coin added pushes one over the edge (`BagCurrency` hits the bag's "Ground" trigger, then
`CurrencyBag.CurrencyFell`, then `Player.CoinFellFromBag`). That coin leaves your purse and drops at your feet. Half of
spilled coins (`Player.overflowCoinsWaterProbability` = 0.5) fall into the water instead and are lost (gems never
are). A coin on the ground can't be picked up by anyone for 0.6 s; then picking it up with a full purse just spills
again. Spend coins or leave some with the banker before you collect more.

**Bigger purse.** Makes that bag bigger: its walls, rim, lid, drawing and the spill trigger all grow by 25% each way
per level, while the coins keep their size, so it holds about 39, 56 and then 77 coins. The bag's parts are scaled
about the bottom of its cavity (so the floor never moves under the coins) and shifted so the top of the drawing stays
where the game puts it; new coins drop in from the same height above the bigger rim (a Harmony postfix on
`CurrencyBag.SpawnCurrency`). The bag grows smoothly when bought. After a RESET, or on a save without the upgrade, it
only shrinks once the coins in it fit the smaller bag, so changing size never spills coins.

How it's applied:
- **Unit stats** are the units' own tuning fields. Every second the mod scans for units (new recruits, new steeds,
  rebuilt walls included) and sets each field to *its original value × multiplier*. The original is remembered per
  object, so effects never stack up, and a reset restores the originals. Outside a kingdom (title screen, loading),
  everything is put back.
- **Damage** is changed in a single Harmony prefix on `Damageable.ReceiveDamage`, which every hit in the game passes
  through. Arrows report their archer as the attacker, so the attacker decides which bonus applies; walls are found
  by a `Wall` component on the target. Hit points are small integers, so fractions are rounded randomly in
  proportion (1 × 1.5 is 1 or 2 with equal odds), which averages out to the exact bonus.

Levels are saved **per save**, keyed by the game's campaign slot (`"campaign0"`, …) or challenge island
(`"challenge3"`). Kingdom-wide upgrades are stored by id (`"archer_rate"`), per-player ones with the player
(`"steed_speed_p1"`). Upgrades only work while playing a kingdom, and only for the host or in single-player.

**Steam Cloud.** The levels (and which statues you've found) live in `kingdommenu.upgrades.json` next to the game's
own save file, in `%USERPROFILE%\AppData\LocalLow\noio\KingdomTwoCrowns\Release`. Steam Cloud syncs every file in
that folder for this game, so your upgrades follow your campaign to another PC, or to GameNative if it syncs your
cloud saves. The unmodded game never reads the file.
- Older versions kept them in `BepInEx/config/kingdomupgrades.levels.json`. On the first start the mod copies that
  file over and renames the old one to `kingdomupgrades.levels.json.moved`. If the save folder already has a
  levels file (synced from another device), that one is used and the old one is left alone.
- Without the levels, a save holding more coins than a normal purse would overflow when it loads: the Bigger purse
  is what holds them. With the mod and its levels, the purse reaches full size within the first few coins of
  loading (measured), so nothing spills.

## Units

One row per job you've unlocked (villagers, builders and archers always; farmers once a farm is built; soldiers once
you have one or their stand is placed), plus your steed:
- the count, at the right;
- stats read from a live unit of that job, so upgrades and statue blessings are already included: walk/run speed
  (the game's units per second), builders' time per work tick, archers' reload, soldiers' damage per hit and attack
  interval. Damage upgrades act per hit, so for archers and pikemen they're shown as a multiplier.

## Statues

One row per statue blessing (archer, worker, knight, farmer, pike) that you've unlocked before, or whose statue
you've been near on this campaign (within about half a screen, noticed even with the menu closed; remembered in the
levels file):
- **LOCKED:** not unlocked yet. If the statue is on this island, its unlock price is shown.
- **ASLEEP:** unlocked, but not active. If it's on this island, the coins to wake it are shown.
- **ACTIVE:** working, with the days left when the statue counts them.
- What it does: the farmer statue's extra farmland per farm is read from the game; the knight statue makes knights
  strike faster; the others say which units they bless.
- Each row uses the game's own map icon for that statue (the greyed one while it's locked).

The **time statue** appears once you've been near it, with the days left before time runs out (gold under 5, red
under 3).

## Hold to drop coins

In the game, pressing the drop key (S or the down arrow, player 2's J, or your controller's drop button) with nothing
to pay for in reach drops one coin, and holding it does nothing more. With this mod, holding it keeps dropping coins:
after half a second, one every 0.2 s until you let go. A quick press still drops exactly one.

- Each extra coin is dropped by the game's own code, as if you had pressed the key again (a Harmony postfix on
  `Player.UpdatePayState`), so it's tossed, sounds and can be picked up like any coin you drop.
- Paying is unchanged: holding the key next to a building, stand or anything else payable pays for it as usual, and a
  hold that started paying never turns into dropping.
- Walking up to something payable while holding pauses the drops; it never starts paying for it. They carry on once
  it's out of reach.
- Only coins repeat. When your purse runs out of coins it stops instead of dropping gems.
- Works for each player in co-op. Turn it off with `HoldToDropCoins = false`, or change the pace with
  `HoldToDropInterval`.

## Updates

When the game starts, the menu checks the latest release of [minwoong-seo/K2C-Mods](https://github.com/minwoong-seo/K2C-Mods)
in the background (the game never waits for it). If that release has newer versions of mods you have installed, a
popup lists them a few seconds into play, e.g. `Kingdom HUD  1.1.0 to 1.2.0`, with **UPDATE** and **NOT NOW**:
- **UPDATE** downloads the release's `KingdomTwoCrowns-Mods-Only-v<version>.zip`, checks it against the size and
  SHA-256 checksum GitHub lists for it, and replaces those mods' files. The new versions load the next time you
  start the game; the popup says so. If anything goes wrong, your mods are left as they were.
- **NOT NOW** closes it; it asks again on the next start.
- Player 1 answers it: left/right and Space/Enter (or the controller's stick and A, B for NOT NOW), or the mouse.
  Your monarch doesn't move while it's up.
- Only mods you have installed are updated; one you've removed stays removed. Only files in those mods' own folders
  under `BepInEx/plugins` are written.
- The release lists each mod's version in its `mods.json`; a mod is offered when that version is higher than the
  one loaded. Turn the check off with `CheckForUpdates = false`.

## Local co-op

Each player has their own menu, controls and purse.
- Each player clicks a stick on **their own** controller to open their own menu. On the keyboard each player uses
  the side of it the game gives them, so one player playing never moves the other's menu:
  - player 1 (the game's WASD/arrow keys): F6, WASD or arrows, Space/Enter, Esc, Q/E and the number keys;
  - player 2 (the game's H/J/K/U keys): F9 to open, **U/H/J/K** to move, **hold G** to buy, **Y/I** for tabs, ` (the
    key left of 1, player 2's pause key) or F9 to close. The game binds player 2's J to both down and submit; in the
    menu J only moves down.
- The mouse works on whichever menu is under the cursor.
- Only the player whose menu is open stops; the other keeps playing.
- Purchases come out of the buyer's own purse. Kingdom-wide upgrades are shared (either player can buy the next
  level). Steed, coin magnet, quick hands and bigger purse upgrades are per player; steed ones follow whatever steed that player
  rides.
- Shared screen: player 1's menu on the left half, player 2's on the right (smaller if needed so both fit). Split
  screen (Pause > Multiplayer > Play local coop > Enter splitscreen): each menu inside its owner's half, player 1 on
  top. A short half shows 4 rows and scrolls the rest instead of shrinking the menu. Side-by-side splits (which the
  game supports on some platforms) are handled too. If the menus show up on the wrong halves, set
  `Player1OnTop = false`.
- The title shows `KINGDOM  P1` / `KINGDOM  P2`.
- Buying works for the host or in local play; in online co-op the joining player can't buy.

## Settings

`BepInEx/config/kingdommenu.kingdomtwocrowns.cfg`, or press F1 for Configuration Manager:
- `ToggleKey` (F6) and `Player2ToggleKey` (F9)
- `OpenWithStickClick`: L3/R3 opens the menu (on by default). If your pad's stick buttons aren't found, the log says
  `No left stick button found on '<controller>'` and lists its buttons; Steam Input can bind any button to F6 meanwhile.
- `FreePurchases`: shop items and upgrades cost nothing.
- `UiScale`: bigger or smaller (it already scales in whole-pixel steps with the screen height).
- `Player1OnTop`: player 1 has the top half of a split screen (the left half when it's side by side).
- `HoldToDropCoins` (on) and `HoldToDropInterval` (0.2 s between coins): holding the drop key keeps dropping coins.
- `[Updates] CheckForUpdates` (on): check GitHub for newer versions when the game starts. `Repository`
  (`minwoong-seo/K2C-Mods`) is where it looks; `ApiUrl` is only for testing against a mirror.

## Upgrading from Remote Shop / Kingdom Upgrades

1. Delete `BepInEx/plugins/RemoteShop` and `BepInEx/plugins/KingdomUpgrades` (otherwise their menus load too and the
   upgrades are applied twice).
2. Install this mod. Your upgrade levels carry over: `kingdomupgrades.levels.json` is moved into the game's save
   folder on the first start, where Steam Cloud syncs it (see [Upgrades](#upgrades)).
3. `remoteshop.kingdomtwocrowns.cfg` and `kingdomupgrades.kingdomtwocrowns.cfg` in `BepInEx/config` are no longer
   read and can be deleted. `FreePurchases` and `FreeUpgrades` are now the one `FreePurchases` setting.

Fixed along the way: the old menus read the game's **Pay** action (the "down" that drops coins) as their confirm
button, so pressing down in a menu also held the BUY button under the focus. The menu now uses the game's real
Submit action, and LB/RB use its menu tab actions.

## Look

Everything is the game's own art, loaded at runtime (nothing is copied into the mod): the pixel fonts `Kingdom` and
`KingdomMenu`, the panel `menu_panel_background`, the wood buttons and row highlight, the spinning coin, each stand's
real item sprite, live unit, steed, wall and statue sprites. It's built with uGUI and scales in whole-pixel steps,
so the pixel art stays crisp. Effects are written as multipliers (x1.15) because in the game's pixel font `%`, `|`
and `>` draw controller-button icons. Clicks on the panel don't pass through to the game's menus underneath.

To check the layout without loading a save, create an empty file `BepInEx/config/kingdommenu.preview` and restart.
The Shop, Upgrades and Statues tabs then show sample rows outside a kingdom, and nothing can be bought. Delete the
file afterwards.

## Build

You need the .NET SDK (6 or newer) and a game folder where BepInEx has already generated `BepInEx/interop`.

```
cd src
dotnet build -c Release
```

From outside the game folder, use `dotnet build -c Release -p:GameDir="D:\path\to\Kingdom Two Crowns"`. The DLL is
copied to `package/` and into the game's `BepInEx/plugins/KingdomMenu/`. That second copy is skipped while the game
is running, because the game locks the file. `src/bin` and `src/obj` are build leftovers and safe to delete.

## Testing tip: start the game from Steam

If you start `KingdomTwoCrowns.exe` directly while Steam is closed, the game starts Steam itself, and that Steam
inherits the mod loader's "already set up" flag. Every game it launches afterwards then runs **without** BepInEx (no
HUD, F6 does nothing) until Steam is restarted. Start Steam first, or launch the game from Steam.

## If the menu stops working after a BepInEx update or reinstall

Reinstalling or updating BepInEx overwrites the fix, and no plugins load until it's applied again. Look for
`Failed to generate Il2Cpp interop assemblies` in `BepInEx/LogOutput.log`. If it's there, copy
`BepInEx-Fix/package/` into the game folder again. See `BepInEx-Fix/README.md`.
