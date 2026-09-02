<div align="center">

# CoopKeep

**Bannerlord Coop server manager**

[![Version](https://img.shields.io/badge/version-v0.1.0-C9A227)](../../releases)
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

CoopKeep takes all of that over — and **never touches the mod's files.**

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
> Keep it wherever you like; to uninstall, delete the exe.

**Your friends do not need CoopKeep.** They just subscribe to the same mod and join
from the game.

---

## How to use

### 1 · Pick or create a server

Choose a server in the left panel. If you have none, type a name in the box below
and press **New server**.

> You never have to enter the game — the server builds the world from scratch.
> Takes about a minute.

### 2 · Configure (optional)

Set a password, port, autosave interval and Steam visibility in **Settings**.
Changes are made while the server is stopped, and **whatever is on screen is what
the server starts with.**

### 3 · Start

Press **Start** and wait. The status goes `Booting engine` → `Loading world` →
**`Online`**. About a minute in total.

### 4 · How your friends join

They open the game → **Coop** menu → **Steam Lobbies** tab.

> ⚠️ **The most common mistake:** the name they search for is **your Steam name**,
> not the server name. CoopKeep shows it in the "Send to your friends" box, with a
> **Copy** button.

For a direct IP join, forward **UDP 4200** on your router. Not needed for Steam joins.

---

## Features

### Server
- Start, stop safely (the world is saved), live status and phase indicator
- CPU and memory usage
- **Automatic restart on crash** — exponential backoff with crash-loop protection

### Players
- Live list, search, **kick**
- Broadcast messages to everyone
- **Admin actions:** give full health or set gold for the selected player.
  Every action is written to a persistent audit log, visible in the UI.

### Worlds and backups
- Create, rename, duplicate, delete, open folder
- **Automatic backup after every save**, transactional restore
- Pruning by both count *and* age

### For streamers
- **Eye button:** masks your IP, port and Steam name, and blurs the console.
  On by default.

### Installation checks
- Steam, game and mod are found automatically; version compatibility is verified
- **Module check:** warns when your game launcher has modules enabled that the
  server does not expect — the most likely cause of failed connections

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
| **Mod DLLs fail to load** | Windows may have blocked the downloaded DLLs. In the mod folder, run:<br>`Get-ChildItem "<mod folder>" -Recurse \| Unblock-File` |

---

## Known limitations

These are not CoopKeep gaps — they are **limits of the mod itself**, verified by research:

- **No player cap.** Bannerlord Coop does not enforce a slot limit, and connections
  cannot be refused from the outside.
- **No ban, only kick.** The Steam identity is not written to the live protocol, so a
  persistent ban could only ever be name-based.
- **No kill action.** The server refuses vanilla cheat commands
  (`Cheat mode is disabled!`) and zeroing health has unverified consequences.
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
| stdin commands | `status` · `players` · `save` · `say` · `kick` · `stop` |
| stdout `@DS@` events | Status phase, live player list (JSON) |
| File system | `server-config.json`, `Game Saves\`, backups |

The configuration file is edited **preserving its comments, key order and any keys we
don't recognise**, so your settings survive mod updates.

---

## Development

```bash
dotnet test                                    # 178 tests
dotnet run --project src/CoopKeep.Cli -- doctor
powershell -File publish.ps1                   # produces the single-file exe
```

| Project | Responsibility |
|---|---|
| `src/CoopKeep.Core` | Process supervision, protocol, backups, installation discovery — **UI-independent** |
| `src/CoopKeep.App` | Avalonia UI (English/Turkish, dark theme) |
| `src/CoopKeep.Cli` | Command line tool |
| `tests/CoopKeep.Core.Tests` | 178 tests |

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
