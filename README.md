# DuckBots

Computer-controlled ducks for [Duck Game](https://store.steampowered.com/app/312530/Duck_Game/). Fill an empty lobby with bots and play a real match: they grab weapons, open item boxes, navigate the map with jumps, springs and teleporters, dodge bullets by sliding and fight back. Online, your friends don't need the mod.

**Status: 0.1 beta.** Bots play full matches, offline and online, but they still have rough edges. See [Known issues](#known-issues).

## Features

- **Up to 7 bots** in a local match, added from the lobby pause menu.
- **They know the map.** Every round the mod builds a walkability map of the level and simulates jumps frame by frame with the duck's own physics. Bots also use springs, teleporters, doors and one-way platforms, and they avoid spikes, saws, mines and bottomless pits.
- **They fight.** Bots pick the closest usable weapon by path length, open item boxes and presents, and find a firing position at the enemy's height. They shoot up while flapping, swing melee weapons up close and keep away from their own rockets. They slide under incoming bullets. With a net gun they catch a duck, carry it to a ledge and throw it into the abyss.
- **Online with friends who don't have the mod.** When you host, bots join as your extra local players, like friends on extra controllers, and everyone sees them as normal ducks. Accept a friend's invite and your bots come with you.

## Install

**Steam Workshop:** subscribe on the Workshop page *(link coming soon)* and restart the game.

**Manually:** put `mod.conf`, `DuckBots.dll` and `0Harmony.dll` into `%APPDATA%\DuckGame\Mods\DuckBots\` and restart the game.

## How to play

- **Offline.** In the lobby open the pause menu and choose **BOTS**. Each of the seven rows is one bot: switch it from `OFF` to a level. Your choice is remembered between launches. Bots take the free rooms starting from the last one, and they never take the first room: that one is yours.
- **Hosting online.** Open the lobby pause menu and choose **INVITE FRIENDS**. In the network lobby the pause menu has **BOTS** next to EDIT SLOTS, so you can add or remove bots on the fly. During a match the host also gets **SKIP**, to end a round where bots got stuck.
- **Joining a friend.** Sit in your lobby with bots and accept your friend's invite, or join them from the Steam friends list. Your bots come along as your local players. The host needs enough open slots for you and your bots.
- **Strangers' lobbies.** When you join through quick match or the server browser, your bots stay home: the mod never brings them to people who didn't invite you.

The difficulty levels in the menu (`DUCKLING`, `AVERAGE`, `GOOD`, `BEAST`) don't change anything yet. Every bot plays at full strength; real difficulty levels come in a later version.

### How online works without the mod on the other side

Duck Game only lets players into a lobby if their enabled mods match, and it checks this with a mod fingerprint. DuckBots adds no new items and no network messages, so a game with DuckBots is the same game for everyone else. The mod leaves itself out of that fingerprint. Bots are ordinary local players driven by the mod, the same way as a second controller. If you'd rather your friends knew there are bots in the match, tell them: they see the bots as `YourName(2)`, `YourName(3)` and so on.

### Console commands

Open the console with `~`:

- `nextround` ends the current round, for when bots get stuck.
- `botdebug` shows each bot's current mode above it and writes the detailed log.
- `botlog` records every frame of every duck into the detailed log.

## Known issues

- **PLAY ONLINE is greyed out ("FULL GAME")** with 3 or more bots in the lobby: the game counts bots as players. Use **INVITE FRIENDS** from the pause menu instead.
- **No free slots online.** A network lobby has a limited number of slots, and bots take them. The host can open more with **EDIT SLOTS**.
- **Guests can't change their bots** after joining: you keep the ones you came with.
- **Bots sometimes get stuck.** For example, a bot can bounce forever on a spring under a ceiling. Use **SKIP** online or `nextround` offline.
- **Weapons bots don't use well yet:** the Mind Control Ray, RC car and Magnet Gun; charge weapons like the Death Ray and Coil Gun; grenade launcher arcs. Bots also use one fire range for every gun and don't wear equipment.

## Reporting bugs

The mod keeps a short log of what the bots did and why at `%APPDATA%\DuckGame\DuckBots-data\botlog.txt`. It is overwritten when the game starts; the previous run is kept as `botlog.prev.txt`. Please attach it to a [GitHub issue](../../issues) together with the map name and what you saw. The log is in Russian, that's fine.

## Building from source

You need:

- Duck Game from Steam.
- `csc.exe` from .NET Framework 4.x (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319`). The code is C# 5: no string interpolation, no `?.`.
- `0Harmony.dll` from [Lib.Harmony](https://github.com/pardeike/Harmony) 2.2.2, `net472` build, in the mod folder.
- `mod.conf` in the mod folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Mod>
  <MajorSupportedRevision>1</MajorSupportedRevision>
  <NoCompilation>true</NoCompilation>
</Mod>
```

The paths to the game and the mod folder are set at the top of `build.cmd`. It compiles all `src/**/*.cs` into `DuckBots.dll` in the mod folder. The game loads the DLL only at startup, so restart it completely after a build. Don't put the `.cs` files into the mod folder: the game would try to compile them itself and fail to load Harmony.

On startup the mod runs a self-test of the bot brain without the game. The first line of `botlog.txt` says whether it passed.

### Layout of `src/`

- `DuckBotsMod.cs` is the entry point and holds the Harmony patches.
- `Brain/` is the decision making, and `Nav/` is the walkability map, jump simulation and path search. Neither knows any game types, so the self-test runs them outside the game.
- `Game/` holds everything that touches the game: bot input, the lobby, the network, the eyes of the bot (`Look`) and routing.
- `Journal/` writes the logs.
- `Tests/` holds the self-test, one file per topic.

## Contributing

Pull requests are welcome. Some practical notes:

- **One behaviour change per pull request.** Bot behaviour can only be checked by playing, and when several changes go into one match it's impossible to tell which one made bots better or worse.
- **Start from the log.** Find the cause in `botlog.txt` (with `debug.txt` in `DuckBots-data`, also in `botlog-detail.txt`) before changing code, and add a self-test case in `Tests/` where the logic is pure.
- **Language.** Code comments, logs and commit messages are in Russian. Write your pull request in English or Russian, whichever you like.

## License

Copyright (C) 2026 dhsuioa

DuckBots is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE).

**Additional permission under GNU GPL version 3 section 7.** If you modify this program, or any covered work, by linking or combining it with Duck Game (or a modified version of it), containing parts covered by the terms of the Duck Game license, or with Harmony (Lib.Harmony), the licensors of this program grant you additional permission to convey the resulting work.

DuckBots uses [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike, MIT License. DuckBots is a fan-made mod and is not affiliated with the developers or publishers of Duck Game.
