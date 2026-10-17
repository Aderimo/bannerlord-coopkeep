Manage your Bannerlord Coop server from a single app. No command line, no editing
config files by hand, no need to create a save in-game first.

*Bannerlord Coop sunucusunu tek bir uygulamadan yönetmeni sağlar. Komut satırı yok,
elle dosya düzenleme yok, oyuna girip save yaratma yok.*

## Install · Kurulum

1. **Mount & Blade II: Bannerlord** (v1.4.8) on Steam
2. Subscribe to [**Bannerlord Coop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698) on the Steam Workshop
3. Download **`CoopKeep.exe`** below and double-click it

**No installer, no .NET required.** Your friends do **not** need CoopKeep — they just
subscribe to the same mod and join from the game.

> ⚠️ Do not download the *Source code* archives — they contain the code, not the
> program. You want **`CoopKeep.exe`**.
>
> *Kaynak kodu arşivlerini indirme — onlar kod içerir, program değil.
> İndirmen gereken dosya **`CoopKeep.exe`**.*

## What's new in this version · Bu sürümde yeni

- **Player profile** (from Players, Cheats and the Map): skills screen like the game's,
  equipment with real item images, inventory and army.
  *Oyuncu profili: oyundaki gibi beceri ekranı, kuşanım, envanter, ordu.*
- **Skills, focus and attribute points now work** and are read back from the server
  after every change. *Beceri, odak ve özellik puanları artık çalışıyor.*
- **Encyclopedia tab** with the game's own texts (in Turkish from the game's own
  translation) and guides: strongest troops, richest towns, best castle locations,
  best weapons and armor. *Ansiklopedi sekmesi ve rehberler.*
- **Map:** teleport from the map, all lords with info cards, smooth movement,
  centered zoom, no more label clutter. *Haritadan ışınlama, bütün lordlar, akıcı
  hareket, karmaşa giderildi.*
- **Instant world creation** — the server boots once instead of twice.
  *Yeni dünya anında kurulur.*
- "Take troops" works for players; player search; bans in one list; paged mods list
  with an info window.
  *"Asker al" düzeldi, oyuncu arama, tek ban listesi, sayfalı mod listesi.*

Full list: [CHANGELOG.md](../blob/main/CHANGELOG.md)

## Highlights · Öne çıkanlar

- **Create a server world without entering the game**
- Start / stop safely, live status, automatic backup after every save, automatic
  restart on crash
- Players: kick, **Steam bans** (no restart needed), search, profiles
- **Cheats tab** to repair what a cheater broke: gold, health, troops by type, items,
  clan influence and renown, skills — with undo and an audit log
- **Live map** of Calradia with players, lords, owners and capture alerts; friends can
  watch it from their own CoopKeep (viewer mode)
- Streamer mode, module check, English and Turkish

## Server plugin · Sunucu eklentisi

Items, skills, the profile and the fast map use a small **CoopKeep server plugin**. It
is **off until you turn it on** (Settings or the Items screen). When on, CoopKeep adds
it as a sub-module to the Coop mod's own folder (a backup of the original file is
kept) and removes it when you turn it off. Only the server console can use its
commands; players cannot.

*Eşyalar, beceriler, profil ve hızlı harita için küçük bir CoopKeep sunucu eklentisi
kullanılır. Sen açmadıkça kapalıdır; kapatınca kaldırılır.*

## The most common mistake

Your friends must search the Steam server list for **your Steam name**, not the
server name. CoopKeep shows it in the "Send to your friends" box with a copy button.

*Arkadaşların sunucu listesinde **senin Steam adını** aratmalı, sunucu adını değil.*

## Known limitations · Bilinen sınırlar

- Renown cannot be taken away and there is no god mode (the server does not support it)
- Skill XP follows the game's learning rate; use the level buttons for a visible change
- Steam installations only; one server at a time

---

CoopKeep is an independent tool. It is not affiliated with the Bannerlord Coop team
or TaleWorlds, and contains none of their code. Game texts and images are read from
your own installation or downloaded from community wikis to your computer.
