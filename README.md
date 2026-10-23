# DuckBots

**English** · [Русский](README.ru.md)

Computer-controlled ducks for [Duck Game](https://store.steampowered.com/app/312530/Duck_Game/). Fill an empty lobby with bots and play a real match, offline or online with friends who don't have the mod.

![version](https://img.shields.io/badge/version-0.3.1%20beta-orange)
[![license](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
[![Steam Workshop](https://img.shields.io/badge/Steam%20Workshop-subscribe-1b2838?logo=steam&logoColor=white)](https://steamcommunity.com/sharedfiles/filedetails/?id=3823678387)
[![Duck Game](https://img.shields.io/badge/Duck%20Game-mod-f2c53d)](https://store.steampowered.com/app/312530/Duck_Game/)
[![issues](https://img.shields.io/github/issues/dhsuioa/DuckBots)](https://github.com/dhsuioa/DuckBots/issues)

![DuckBots: two ducks facing off in front of a burning city](assets/preview.png)

## What the bots can do

- **Find weapons.** A bot goes for the nearest weapon it can actually reach, measured along the path rather than in a straight line. Bots also open item boxes and presents.
- **Get around the map.** They walk, jump, drop through one-way platforms, ride springs and teleporters and go through doors. Glass in the way they break with a slide or a shot. Spikes, saws, laid mines and bottomless pits they avoid.
- **Fight.** A bot looks for a firing position at the enemy's height and turns to face the enemy. When an enemy gets too close, it backs off while still facing them. It shoots upward while flapping and through glass, lobs grenade-launcher shots at enemies above or below, burns them with the flamethrower up close, uses melee weapons up close and doesn't fire rockets point-blank.
- **Dodge.** When someone aims at a bot, it slides under the bullet. Knocked down, it gets straight back up.
- **Use the net gun.** A bot catches a duck in the net, carries it to a ledge and throws it into the abyss.
- **Play online.** When you host, bots join as your extra local players and everyone sees them as normal ducks. Your friends don't need the mod.

![A Duck Game lobby round with eight ducks: the player and seven bots](assets/screenshot.png)

## How it works

DuckBots doesn't move ducks around and doesn't change the game's rules. Each bot is a virtual controller: the mod decides which buttons to press, and Duck Game handles physics, weapons and deaths exactly as it does for a human player.

1. **Map.** At the start of a round the mod reads the level into a grid of walkable cells: floors, platforms, walls, springs, doors, teleporters and hazards.
2. **Jumps.** Every possible jump is simulated frame by frame with the duck's own physics, which was checked against about 12,000 frames recorded in real matches. Each jump is stored as a sequence of button presses.
3. **Route.** The bot searches a path over this map to a weapon, a firing position or the enemy.
4. **Brain.** Every frame the bot decides what to do: run from a grenade, fight, pick up a weapon or chase. Then it presses the buttons.

## Install

- **From Steam Workshop:** subscribe on the [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3823678387) and restart the game.
- **Manually:** download `DuckBots-<version>.zip` from [Releases](https://github.com/dhsuioa/DuckBots/releases), unzip it into `%APPDATA%\DuckGame\Mods\` so that you get `Mods\DuckBots\DuckBots.dll`, and restart the game.

## How to play

**Offline.** In the lobby, open the pause menu and choose **BOTS**. Each of the seven rows is one bot: switch it from `OFF` to a level. Bots take free rooms in order starting from the second one; the first room is yours. Each room belongs to its own controller, so if a friend presses START in a room where a bot sits, the bot moves to the next free room and your friend joins with a second press of START. Your choice is remembered between launches.

<img src="assets/bots-menu.png" width="480" alt="The lobby pause menu with the BOTS item at the bottom">


**Hosting online.** Open the lobby pause menu and choose **INVITE FRIENDS**. In the network lobby the pause menu has **BOTS** next to EDIT SLOTS, so you can add or remove bots on the fly. During a match the host also gets **SKIP**, to end a round where bots got stuck.

**Joining a friend.** Sit in your lobby with bots and accept your friend's invite, or join them from the Steam friends list. Your bots come along as your local players. The host needs enough open slots for you and your bots.

**Strangers' lobbies.** When you join through quick match or the server browser, your bots stay home: the mod never brings them to people who didn't invite you.

The levels in the menu (`DUCKLING`, `AVERAGE`, `GOOD`, `BEAST`) don't change anything yet. Every bot plays at full strength, and real difficulty levels come in a later version.

### Online without the mod on the other side

Duck Game only lets players into a lobby if their enabled mods match, and it checks a fingerprint of the mod list. DuckBots adds no new items and no network messages, so for everyone else it is the same game, and the mod leaves itself out of that fingerprint. Your friends see the bots as `YourName(2)`, `YourName(3)` and so on. Let them know these are bots.

### Console commands

Open the console with `~`:

- `nextround` ends the current round, for when bots get stuck.
- `botdebug` shows each bot's current mode above it and turns on the detailed log.
- `botlog` records every frame of every duck into the detailed log.

## Known issues

- **PLAY ONLINE is greyed out ("FULL GAME")** with 3 or more bots in the lobby, because the game counts bots as players. Use **INVITE FRIENDS** from the pause menu instead.
- **No free slots online.** A network lobby has a limited number of slots, and bots take them. The host can open more with **EDIT SLOTS**.
- **Guests can't change their bots** after joining: you keep the ones you came with.
- **Bots sometimes still get stuck.** Use **SKIP** online or `nextround` offline.
- **Spikes.** On some maps bots still jump onto spikes along their route.
- **Some weapons bots can't use yet:**
  - the Mind Control Ray, the RC car and the Magnet Gun, so bots leave them on the floor;
  - charge weapons such as the Death Ray and the Coil Gun;
  - the grenade cannon's arc.
- **No equipment.** Bots don't wear armor or jetpacks.

## Roadmap

The order may change based on what players report. There are no dates: each change is tested in real matches before the next one.

**Done in 0.3**

- [x] A knocked-down bot gets back up right away instead of lying there for seconds.
- [x] Close fights: bots back away while facing the enemy instead of spinning on the spot.
- [x] The grenade launcher fires in an aimed arc, so bots hit enemies above and below them.
- [x] The flamethrower and the flare gun work: bots keep the flame going up close and fire flares from a distance where they hit.
- [x] Glass: bots break it with a slide on their way and shoot through it at enemies behind it.

**Done in 0.2**

- [x] Bots no longer get stuck bouncing on springs under a ceiling: they steer in flight.
- [x] An item box counts as much as a weapon lying on the floor, so bots no longer run past a nearby box to a far-away gun.
- [x] Each weapon's own range: snipers shoot from far away, short-range guns wait until they are close.
- [x] Bots don't pick up weapons they can't use yet (Mind Control Ray, RC car, Magnet Gun).

**Next up**

- [ ] Bots stop jumping onto spikes.
- [ ] The grenade cannon, and bullet drop for weapons affected by gravity.
- [ ] The Mind Control Ray: grab an enemy and walk them off a ledge.
- [ ] Purple boxes: store a copy of a good weapon and take fresh copies in later rounds.

**Later**

- [ ] Real difficulty levels from `DUCKLING` to `BEAST`: reaction time, accuracy, weapon choice, sense of danger.
- [ ] Putting out fire with an extinguisher or water.
- [ ] Equipment: jetpacks, helmets and armor.
- [ ] Ropes and vines.
- [ ] Charge weapons (Death Ray, Coil Gun) and smarter reloading of the musket and its relatives.
- [ ] Online polish: PLAY ONLINE with 3+ bots and an in-game hint when the lobby is full.
- [ ] Better map reading: doors that block line of sight, small ledges, pipes, keys and locked doors.

## Reporting bugs

The mod keeps a short log of what the bots did and why at `%APPDATA%\DuckGame\DuckBots-data\botlog.txt`. It is rewritten when the game starts; the previous run is kept as `botlog.prev.txt`. Please attach it to a [GitHub issue](https://github.com/dhsuioa/DuckBots/issues) together with the map name and what you saw. The log is in Russian, and that's fine.

## Building from source

You need:

- Duck Game from Steam;
- `csc.exe` from .NET Framework 4.x (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319`). The code is C# 5, so no string interpolation and no `?.`;
- `0Harmony.dll` from [Lib.Harmony](https://github.com/pardeike/Harmony) 2.2.2, the `net472` build, in the mod folder;
- `mod.conf` in the mod folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Mod>
  <MajorSupportedRevision>1</MajorSupportedRevision>
  <NoCompilation>true</NoCompilation>
</Mod>
```

The paths to the game and the mod folder are set at the top of `build.cmd`. It compiles all `src/**/*.cs` into `DuckBots.dll` in the mod folder. The game loads the DLL only at startup, so restart it completely after a build. Don't put the `.cs` files into the mod folder: the game would try to compile them itself and fail to load Harmony.

On startup the mod runs a self-test of the bot brain without the game, and the first line of `botlog.txt` says whether it passed. To get the detailed log and the mode labels while developing, create an empty `debug.txt` in `%APPDATA%\DuckGame\DuckBots-data\`.

### Layout of `src/`

- `DuckBotsMod.cs` is the entry point and holds the Harmony patches.
- `Brain/` holds the decisions. `Nav/` holds the walkability map, the jump simulation and the path search. Neither knows any game types, so the self-test runs them outside the game.
- `Game/` holds everything that touches the game: bot input, the lobby, the network, what a bot sees (`Look`) and routing.
- `Journal/` writes the logs.
- `Tests/` holds the self-test, one file per topic.

## Contributing

Pull requests are welcome. A few practical notes:

- **One behaviour change per pull request.** Bot behaviour can only be checked by playing. When several changes go into one match, it's impossible to tell which one made the bots better or worse.
- **Start from the log.** Find the cause in `botlog.txt` before changing code, or in `botlog-detail.txt` with `debug.txt` on. Where the logic is pure, add a self-test case in `Tests/`.
- **Language.** Most existing comments, logs and commit messages are in Russian, but English is just as welcome: write your code comments, commits and pull requests in English or Russian, whichever you prefer.

## License

Copyright (C) 2026 dhsuioa

DuckBots is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE).

**Additional permission under GNU GPL version 3 section 7.** If you modify this program, or any covered work, by linking or combining it with Duck Game (or a modified version of it), containing parts covered by the terms of the Duck Game license, or with Harmony (Lib.Harmony), the licensors of this program grant you additional permission to convey the resulting work.

DuckBots uses [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike, MIT License. DuckBots is a fan-made mod and is not affiliated with the developers or publishers of Duck Game.
