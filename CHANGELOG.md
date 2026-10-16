# Changelog · Değişiklik günlüğü

Versioning follows [Semantic Versioning](https://semver.org/).
The single source of the version number is `Directory.Build.props`.

*Sürümlendirme [Semantic Versioning](https://semver.org/lang/tr/) kuralına uyar.
Sürüm numarasının tek kaynağı `Directory.Build.props` dosyasıdır.*

---

## v0.3.0 — 2026-10-16

Second round of user feedback. New server commands below were read from a running
Coop v0.1.6 server before being used.

*İkinci geri bildirim turu. Aşağıdaki yeni sunucu komutları kullanılmadan önce çalışan
bir Coop v0.1.6 sunucusundan okundu.*

### Added · Eklenenler

- **Admin tools** on the Players page, for one player or all players: full health,
  add/set gold, age, influence, renown, troops, troop XP, skill XP, attribute and
  focus points, with an optional note in the audit log (e.g. making up for a cheater).
  *Yönetici araçları: tek oyuncu ya da tüm oyuncular için can, altın, nüfuz, ün,
  asker, deneyim, puan; denetim kaydına not.*
- **Live map** of Calradia drawn from the game's own settlement data, with every
  player's party position, activity (travelling, in a town, in battle, besieging),
  morale and strength; click a player for details and quick actions.
  *Canlı harita: oyuncuların konumu, ne yaptığı ve bilgileri.*
- **Mods page:** search (matches move to the top), filter tabs, a community
  compatibility list (`compat/mods.json`) and "works / broken" reports that open a
  pre-filled GitHub issue. Mods that appeared in an error line in the last Coop session
  are flagged.
  *Modlar: arama, filtreler, topluluk listesi, GitHub üzerinden bildirim.*
- **Console help:** recognised lines are summarised with an explanation; search and a
  "problems only" filter.
  *Konsol yardımı: tanıdık satırların açıklaması, arama, yalnızca sorunlar.*
- **Backups:** all-worlds view; every backup shows its world, date, time, size and
  reason (autosave, manual, shutdown, before restore).
  *Yedekler: tüm dünyalar, dünya adı, tarih, saat, neden.*
- **Server priority** setting and a **memory warning** threshold.
  *Sunucu önceliği ve bellek uyarısı.*
- More world settings: wanderers, smithing stamina, voice chat distances. The wizard's
  server step now includes autosave interval and port.
  *Daha fazla dünya ayarı; sihirbazda otomatik kayıt ve port.*

### Changed · Değişenler

- **Cheat protection:** the player-cheats setting is now presented as "Cheat
  protection" and is on by default, so players can't cheat; the host uses the admin
  tools instead.
  *Hile koruması varsayılan olarak açık; hileler yalnızca yönetici araçlarında.*
- **Bannerlord-style design:** warm leather/wood palette, gold frames, serif headings,
  far less explanatory text (moved into tooltips).
  *Bannerlord temalı tasarım, çok daha az yazı.*
- Home page shows autosave interval, last save, last backup, connected players and a
  single **Save and back up** button.
  *Ana sayfa: otomatik kayıt, son kayıt/yedek, bağlı oyuncular, "Kaydet ve yedek al".*

### Known limitations · Bilinen sınırlar

- Coop's troop command adds a fixed group of recruits; the troop type and count can't
  be chosen. "God mode" has no server-side command.
  *Asker türü ve sayısı seçilemiyor; tanrı modunun sunucu komutu yok.*
- Memory can't be reserved for the server in advance; Windows allocates it on demand.
  *Sunucuya önceden bellek ayrılamıyor.*

---

## v0.2.0 — 2026-10-16

A redesign driven by user feedback: the app was hard to follow, worlds could not be
configured, and admin actions did not work. Every server-side behaviour below was
verified against a running Coop v0.1.6 server.

*Kullanıcı geri bildirimiyle yeniden tasarım: arayüz karmaşıktı, dünya ayarı
yapılamıyordu ve yönetici işlemleri çalışmıyordu. Aşağıdaki her sunucu davranışı
çalışan bir Coop v0.1.6 sunucusunda doğrulandı.*

### Added · Eklenenler

- **New world wizard**, like starting a campaign in the game: start type, name,
  difficulty presets (Very easy / Easy / Realistic) with every option adjustable,
  rules (battle death, aging, executions, battle size, bandits, wanderers, economy,
  fast forward, voice, cheats…), server settings and a summary.
  *Oyundaki gibi yeni dünya sihirbazı: zorluk, kurallar, sunucu ayarları ve özet.*
- **Per-world settings.** Each world keeps its own difficulty and rules and they can be
  changed later on the Worlds page. They are written to Coop's `mod-config.json`
  right before the server starts (the host applies them on every boot).
  *Her dünyanın kendi ayarları; sonradan değiştirilebilir.*
- **Import a single-player save.** Continue a sandbox campaign you started in the
  game. Saves made with third-party mods are detected from the save header and
  refused, because the server crashes while loading them.
  *Tek oyunculu save'i içe aktarma; modlu save'ler önceden tespit edilip engellenir.*
- **Cheats for players:** the `clientsCanUseCheats` world setting.
  *Oyuncu hileleri dünya ayarlarından açılır.*
- **Characters list** with name, gold, health and age read live from the server;
  actions: full health, add/set gold, set age. Results are read back from the server
  and only successful actions are written to the audit log.
  *Karakter listesi ve doğrulanan yönetici işlemleri.*
- **Block list:** blocked names are kicked automatically when they connect.
  *Engel listesi: engelli adlar bağlanınca otomatik atılır.*
- **Restart with warning:** players get a 60-second notice, the world is saved and
  the server comes back up (Coop's own `coop.server.shutdown`).
  *Oyuncuları uyararak yeniden başlatma.*
- **Mods page** with a status for every launcher module and a one-click
  "Set up for Coop" that turns problem mods off (with a backup and a restore button).
  *Modlar sayfası ve tek tıkla Coop profili.*
- **Server license (EULA) consent:** shown once before the first start; CoopKeep
  never accepts it on the user's behalf.
  *Sunucu EULA'sı ilk başlatmada gösterilir, kullanıcı onaylar.*
- **Bug report** zip (logs, settings, module list; password and Steam IDs masked),
  **update check** against GitHub releases (can be turned off), invite text with
  everything friends need.
  *Hata raporu paketi, güncelleme denetimi, davet metni.*

### Changed · Değişenler

- **New interface:** sidebar navigation with one page per task, a home page that
  always shows the single next step, animated page transitions, toasts, a startup
  progress indicator, a pulsing online indicator and a three-step welcome tour.
  *Yeni arayüz: yan menü, her sayfada tek iş, animasyonlar, bildirimler.*
- New app icon.
  *Yeni uygulama ikonu.*
- The module check now uses Coop's own rules (War Sails, Birth and Aging Options and
  Fast Mode are refused; other mods are not on the server).
  *Modül denetimi Coop'un kendi kurallarına göre.*

### Fixed · Düzeltilenler

- **Gold did nothing:** Coop removed `coop.debug.hero.SetGold`; CoopKeep now uses
  `coop.debug.hero.set_gold_state <hero_id>` and targets the character by id instead
  of the player's Steam name.
  *Altın işlemi çalışmıyordu: kaldırılan komut yerine kimlikle çalışan komut.*
- **Admin actions refused with more than one player:** actions now target registered
  characters, so they work for any number of players.
  *Birden fazla oyuncuda yönetici işlemleri yapılamıyordu.*
- "Full health" no longer lowers health above 100.
  *"Tam can" 100'ün üstündeki canı düşürmüyor.*
- Failed admin actions were logged as done.
  *Başarısız işlemler yapıldı diye kaydediliyordu.*

### Known limitations · Bilinen sınırlar

- The game's own console cheats stay disabled on the server (`Cheat mode is disabled!`)
  and no command enables them. Player cheat menus are available through the world
  setting above.
  *Oyunun konsol hileleri sunucuda kapalı ve açılamıyor.*
- Mods cannot be added to the server; clients must run the same official modules.
  *Sunucuya mod eklenemiyor.*

---

## v0.1.0 — 2026-09-02

First release. A desktop app that manages the Bannerlord Coop dedicated server
entirely from the outside.

*İlk sürüm. Bannerlord Coop dedicated server'ını tamamen dışarıdan yöneten
masaüstü uygulaması.*

### Added · Eklenenler

- **Create a server world without entering the game.** The mod's "create a
  singleplayer save first" step becomes unnecessary.
  *Oyuna girmeden sunucu dünyası yaratma.*
- Start the server, stop it safely (the world is saved), live status and phase
  indicator.
  *Sunucu başlatma, güvenli durdurma, canlı durum ve faz göstergesi.*
- Live player list, player search, `kick`.
  *Canlı oyuncu listesi, oyuncu arama, `kick`.*
- Broadcast messages to players.
  *Oyunculara duyuru gönderme.*
- Password, port, autosave interval and Steam visibility settings.
  *Şifre, port, otomatik kayıt aralığı ve Steam görünürlüğü ayarları.*
- **Automatic backups** after every save, transactional restore, pruning by both
  count and age.
  *Otomatik yedekleme, işlemsel geri yükleme, sayı ve yaş bazlı temizleme.*
- **Crash recovery** with exponential backoff and crash-loop protection.
  *Çökme kurtarma: üstel bekleme ve çökme döngüsü koruması.*
- **Admin actions:** full health and gold for the selected player; every action is
  written to a persistent audit log.
  *Yönetici işlemleri: tam can ve altın; her işlem kalıcı denetim kaydına yazılır.*
- **Streamer mode:** masks IP, port and Steam name, blurs the console.
  *Yayın modu: IP, port ve Steam adını maskeler, konsolu bulanıklaştırır.*
- **Connection info box:** Steam name, local IP and port, copied in one click.
  *Bağlantı bilgisi kutusu: tek tıkla kopyalanır.*
- **Module compatibility check:** warns about extra modules enabled in the game
  launcher — the most likely cause of refused connections.
  *Modül uyumluluk denetimi.*
- CPU and memory usage indicator.
  *İşlemci ve bellek kullanımı göstergesi.*
- Rename, duplicate, delete server worlds and open their folder.
  *Sunucu dünyalarını yeniden adlandırma, kopyalama, silme, klasörünü açma.*
- English and Turkish UI, dark theme, first-run guide.
  *Türkçe ve İngilizce arayüz, koyu tema, ilk açılış rehberi.*
- Command line tool (`CoopKeep.Cli`): `doctor`, `saves`, `new`, `use`, `run`.

### Known limitations · Bilinen sınırlar

These are limits of the mod itself, not gaps in CoopKeep — each was verified by
research against the running server.

*Bunlar eksiklik değil, modun kendi sınırları — çalışan sunucuya karşı doğrulandı.*

- **No player cap.** Bannerlord Coop does not enforce a slot limit and connections
  cannot be refused from the outside.
  *Oyuncu limiti yok.*
- **Ban can only be name-based.** The Steam identity is not written to the live
  protocol; this release has `kick` only.
  *Ban yalnızca isim bazlı olabilir; bu sürümde yalnızca `kick` var.*
- **No kill action.** The server refuses vanilla cheat commands
  (`Cheat mode is disabled!`) and zeroing health has unverified consequences.
  *Öldürme işlemi yok.*
- **"Friends only" visibility** could not be verified on the dedicated server; only
  "visible on Steam / hidden" is offered.
  *"Sadece arkadaşlar" görünürlüğü doğrulanamadı.*
- **Memory cannot be capped.** The server has no such setting; usage is only shown.
  *Bellek sınırı ayarlanamıyor.*
- Admin actions are not applied when a world has more than one registered player,
  because the target cannot be resolved reliably (the reason is shown in the UI).
  *Birden fazla kayıtlı oyuncuda yönetici işlemleri uygulanmaz.*
- Only Steam installations are detected; no Epic/GOG support.
  *Yalnızca Steam kurulumları tespit ediliyor.*
- One server at a time.
  *Aynı anda tek sunucu.*
