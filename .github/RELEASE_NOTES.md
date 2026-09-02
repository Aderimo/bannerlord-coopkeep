Manage your Bannerlord Coop server from a single app. No command line, no editing
config files by hand, no need to create a save in-game first.

*Bannerlord Coop sunucusunu tek bir uygulamadan yönetmeni sağlar. Komut satırı yok,
elle dosya düzenleme yok, oyuna girip save yaratma yok.*

## Install · Kurulum

1. **Mount & Blade II: Bannerlord** (v1.4.8) on Steam
2. Subscribe to [**Bannerlord Coop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698) on the Steam Workshop
3. Download **`CoopKeep.exe`** below and double-click it

**No installer, no .NET required.** Nothing is copied into the game folder.
Your friends do **not** need CoopKeep — they just subscribe to the same mod and
join from the game.

> ⚠️ Do not download the *Source code* archives — they contain the code, not the
> program. You want **`CoopKeep.exe`**.
>
> *Kaynak kodu arşivlerini indirme — onlar kod içerir, program değil.
> İndirmen gereken dosya **`CoopKeep.exe`**.*

## Highlights · Öne çıkanlar

- **Create a server world without entering the game** — the mod's "make a
  singleplayer save first" step becomes unnecessary
- Start / stop safely, live status, CPU and memory usage
- Live player list, search, **kick**, broadcast messages
- **Automatic backup after every save**, transactional restore
- **Automatic restart on crash** with backoff and crash-loop protection
- **Admin actions:** give health and gold, written to a persistent audit log
- **Streamer mode:** masks IP, port and Steam name, blurs the console
- **Module check** — catches the most common cause of failed connections
- English and Turkish, dark theme

## The most common mistake

Your friends must search the Steam server list for **your Steam name**, not the
server name. CoopKeep shows it in the "Send to your friends" box with a copy button.

*Arkadaşların sunucu listesinde **senin Steam adını** aratmalı, sunucu adını değil.*

## Known limitations · Bilinen sınırlar

These are limits of the mod itself, not gaps in CoopKeep:

- No player cap (the mod does not enforce slot limits)
- No ban, only kick (the Steam identity is not in the live protocol)
- No kill action (the server refuses vanilla cheat commands)
- Memory cannot be capped (the server has no such setting)
- Steam installations only; one server at a time

Details: [CHANGELOG.md](../blob/main/CHANGELOG.md)

## Note

Admin actions (health/gold) have not yet been tested with multiple real players.
Feedback welcome.

---

CoopKeep is an independent tool. It is not affiliated with the Bannerlord Coop team
or TaleWorlds, and contains none of their code.
