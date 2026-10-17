<div align="center">

# CoopKeep

**Bannerlord Coop server manager**

[![Version](https://img.shields.io/badge/version-v0.5.0-C9A227)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-4ADE80)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-6B7280)](#install)

Hosting a Mount & Blade II: Bannerlord co-op server on your own PC, in one click.
No command line, no editing config files by hand, no creating a save in-game first.

**English** · [Türkçe](README.tr.md)

</div>

---

## Why

The Bannerlord Coop mod makes co-op campaigns work, but running one is all manual:
unblock DLLs, create a singleplayer save first, edit `server-config.json` in Notepad,
double-click an `.exe` and keep a console window open, copy files by hand when a save
breaks.

CoopKeep takes all of that over. It does not touch the mod's files — unless you turn on
the optional server plugin (see below).

---

## Install

### Requirements

| | |
|---|---|
| Game | Mount & Blade II: Bannerlord **v1.4.8** (Steam) |
| Mod | [Bannerlord Coop](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698) — subscribe on the Steam Workshop |
| OS | Windows 10/11 |
| .NET | **Not required** — the exe is self-contained |

### Steps

1. Install **Bannerlord** from Steam
2. Subscribe to **Bannerlord Coop** on the Steam Workshop
3. Download **`CoopKeep.exe`** from [Releases](../../releases)
4. Double-click it. That's it.

> **No installer.** Nothing is copied into the game folder and no game file is modified.
> Keep it wherever you like; to uninstall, delete the exe. (The optional server plugin is
> the only exception; it is removed again when you turn it off.)

**Your friends do not need CoopKeep.** They just subscribe to the same mod and join
from the game.

---

## How to use

### 1 · Create a world

On first launch the home page offers **Create a world**. The wizard works like starting
a new campaign in the game:

1. **Start** — a fresh sandbox, or continue a campaign you started in single player
2. **Name**
3. **Difficulty** — Very easy / Easy / Realistic, or fine-tune each option
   (damage, combat AI, recruitment, map speed, persuasion…)
4. **Rules** — death in battle, aging, executions, battle size, bandits, wanderers,
   economy, fast forward, voice chat, **cheats for players**
5. **Server** — password and Steam visibility, then a summary

> You never have to enter the game — the server builds the world from scratch.
> Each world keeps its own settings; change them any time on the **Worlds** page.

### 2 · Start

Press **Start server**. The first time, the Coop server's license agreement is shown;
the server will not host until you accept it. The home page shows the progress
(`Booting engine` → `Loading world` → **`Online`**). About a minute.

### 3 · Invite your friends

**Copy invite** puts everything they need in one message. They open the game →
**Coop** → **Steam Lobbies** and search for **your Steam name** — not the server name.

For a direct IP join, forward **UDP 4200** on your router. Not needed for Steam joins.

---

## Features

### Worlds
- **New world wizard** with difficulty presets and every gameplay option of the Coop mod
- **Per-world settings**, applied automatically each time that world starts
- **Import a single-player save**; saves made with third-party mods are detected and
  refused (the server crashes loading them)
- Rename, duplicate, delete, open folder

### Server
- Start, stop safely (the world is saved), live status, CPU and memory
- **Restart with warning:** players get a 60-second notice, the world is saved
- **Automatic restart on crash** — exponential backoff with crash-loop protection

### Players
- Live list, search, kick, broadcast
- **Steam bans** through the Coop server's own ban list — no restart needed. Players
  whose Steam ID is not known yet are banned by name
- **Player profile:** level, gold, health, age, influence, renown, a skills screen like
  the game's, battle and civilian equipment with real item images, inventory and army

### Cheats
For repairing what a cheater broke. Pick one, several or all players:
- Gold, health, clan influence and renown, age
- **Troops by type and count** with real portraits; take troops from an army; heal wounded
- **Items:** every item in the game, grouped and searchable, with town prices
- **Skills, focus and attribute points** on the skills screen
- Undo for recent actions; every action is verified and written to an audit log

### Map
- The real Calradia map with towns, castles, villages, kingdoms and capture alerts
- Players and **all lords** move live; click for an info card
- **Teleport from the map:** pick a player, then click or search a settlement
- **Viewer mode:** friends can watch your map from their own CoopKeep

### Encyclopedia
- Kingdoms, clans, lords, settlements, troops (upgrade trees) and items with the game's
  own texts — in Turkish from the game's own translation
- **Guides** computed from game data: strongest troops, richest towns, strongest clans,
  best castle locations, best weapons, armor and horses

### Mods
- Status of every module in your launcher (compatible / not on server / refused by Coop)
- **All mods list** with community results; click a mod for its description
- **Set up for Coop:** turn off problem mods in one click, restore them later

### Backups
- **Automatic backup after every save**, transactional restore, pruning by count *and* age

### Other
- **Streamer mode** masks your IP, port and Steam name and blurs the console
- **Bug report** zip with logs and settings (password and Steam IDs masked)
- Update check (can be turned off), English and Turkish

---
## Command line

For people who prefer a terminal, `CoopKeep.Cli`:

```bash
coopkeep doctor          # locate the installation and check compatibility
coopkeep saves           # list servers with their players
coopkeep new Calradia    # create a new world (without entering the game)
coopkeep use Calradia    # switch the active server
coopkeep run             # start and drive the console
```

---

## Troubleshooting

| Problem | Cause and fix |
|---|---|
| **My friend can't find the server** | Are they searching for **your Steam name**? Not the server name. |
| **They can't connect** | Check the **Module check** panel. Disable extra mods in the game launcher. |
| **Start does nothing** | A Coop server may already be running — CoopKeep warns about this. Close it from Task Manager. |
| **The password doesn't work** | Press **Start** after typing it; the server only reads settings at boot. |
| **The server closes right away (code 6)** | The Coop server's license agreement was not accepted. Start again from CoopKeep and accept it. |
| **Importing a save is refused** | The save was made with mods the server can't load. Start a new sandbox without mods in the game, save, and import that. |
| **Mod DLLs fail to load** | Windows may have blocked the downloaded DLLs. In the mod folder, run:<br>`Get-ChildItem "<mod folder>" -Recurse \| Unblock-File` |

---

## Known limitations

These are not CoopKeep gaps — they are **limits of the mod itself**, verified by research:

- **No player cap.** Bannerlord Coop does not enforce a slot limit, and connections
  cannot be refused from the outside.
- **The game's own console cheats stay off** on the server (`Cheat mode is disabled!`).
  CoopKeep's Cheats tab works through Coop's own commands and the optional
  **CoopKeep server plugin** (items, skills, profile, fast map). The plugin is off until
  you turn it on; it is then added as a sub-module to the Coop mod's folder (the original
  file is backed up) and removed when you turn it off. Only the server console can use it.
- **Renown cannot be taken away** and there is no god mode.
- **No mods on the server.** It only loads the official modules and Coop; clients must
  turn other mods off (the Mods page helps).
- **"Friends only" visibility** could not be verified on the dedicated server.
- **Memory cannot be capped** — the server has no such setting.
- Only **Steam** installations are detected (no Epic/GOG support).
- **One server at a time.**

Full list: [CHANGELOG.md](CHANGELOG.md)

---

## How it works

CoopKeep **cannot** modify the Coop mod: the dedicated server verifies four Coop
assemblies with SHA-256 and refuses to boot with exit code 4 if the module was
changed. So the integration surface is not code:

| Channel | Used for |
|---|---|
| Process lifecycle | Start, stop, crash detection (exit codes 0/2/3/4) |
| stdin commands | `status` · `players` · `save` · `say` · `kick` · `stop`, plus an allow-list of Coop console commands (`coop.debug.hero.*`, `coop.server.shutdown`) |
| stdout `@DS@` events | Status phase, live player list (JSON) |
| File system | `server-config.json`, `Game Saves\`, backups, and Coop's gameplay settings in `CoopData\mod-config.json` |

The configuration file is edited **preserving its comments, key order and any keys we
don't recognise**, so your settings survive mod updates.

---

## Development

```bash
dotnet test                                    # 229 tests
dotnet run --project src/CoopKeep.Cli -- doctor
powershell -File publish.ps1                   # produces the single-file exe
```

| Project | Responsibility |
|---|---|
| `src/CoopKeep.Core` | Process supervision, protocol, backups, installation discovery — **UI-independent** |
| `src/CoopKeep.App` | Avalonia UI (English/Turkish, dark theme) |
| `src/CoopKeep.Cli` | Command line tool |
| `tests/CoopKeep.Core.Tests` | 229 tests |

The single source of the version number is `Directory.Build.props`.

Contributions welcome. Comments, commit messages and tests in this repository are
written in Turkish; English is fine for issues and pull requests.

---

## License

[MIT](LICENSE) — use, modify and redistribute freely.

CoopKeep is an **independent tool**. It is not affiliated with the Bannerlord Coop
team or TaleWorlds, and contains none of their code.

<div align="center">

Made by · **[aderimo](https://gitgit.me/aderimo)**

</div>
