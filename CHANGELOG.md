# Changelog · Değişiklik günlüğü

Versioning follows [Semantic Versioning](https://semver.org/).
The single source of the version number is `Directory.Build.props`.

*Sürümlendirme [Semantic Versioning](https://semver.org/lang/tr/) kuralına uyar.
Sürüm numarasının tek kaynağı `Directory.Build.props` dosyasıdır.*

---

## v1.0.2 — 2026-10-23

### Fixed · Düzeltilenler

- The same library can be on the Steam Workshop twice: the official Harmony, ButterLib,
  UIExtenderEx and Mod Configuration Menu v5 pages and a second "ModReady" set with the same
  module ids but other versions. Subscribed to both, the game can't tell which to load and your
  version won't match your friend's. CoopKeep now marks these mods, shows both versions with
  their pages (the official one is marked), and uses the copy your Steam account actually sees.
  *Aynı kütüphane Atölye'de iki sayfada olabiliyor (resmî sayfalar ve aynı kimlikli, sürümü
  farklı "ModReady" seti). İkisine de aboneysen oyun hangisini açacağını bilemez, arkadaşınla
  sürüm tutmaz. CoopKeep bunları işaretler, iki sürümü sayfalarıyla gösterir (resmî olan
  işaretli) ve Steam hesabının gerçekten gördüğü kopyayı kullanır.*
- A mod you download by hand (zip) is installed even if the Workshop has another version of it;
  only an identical copy is skipped. v1.0.1 skipped it, so "Refresh" never showed it.
  *Elle indirilen mod, Atölye'de başka sürümü olsa da kurulur; yalnızca birebir aynısı atlanır.*
- In a pack, a mod installed from a different Workshop page says so and offers the pack's page.
  *Pakette, başka bir Atölye sayfasından kurulu mod bunu söyler ve paketteki sayfayı önerir.*

## v1.0.1 — 2026-10-23

### Added · Eklenenler

- **Themes:** Settings → Appearance has four backgrounds (Classic, Night, Graphite, Light) and
  nine accent colours (gold, blue, purple, red, green, orange, yellow, pink, teal). The change
  applies at once and is remembered. Every combination is checked for readable text.
  *Temalar: Ayarlar → Görünüm'de dört zemin (Klasik, Gece, Antrasit, Açık) ve dokuz vurgu rengi.
  Hemen uygulanır ve hatırlanır; her birleşimde yazıların okunurluğu denetlenir.*
- **Match system** theme: Light when Windows is light, Night when it is dark, and it follows
  Windows when you switch.
  *"Sistemle aynı" teması: Windows açıksa Açık, koyuysa Gece; Windows değişince o da değişir.*
- **Advanced view** (Settings → Appearance): admin tools, map, help wiki and console show in the
  side menu only when it is on. If you used to keep Tools open, it starts on.
  *Gelişmiş görünüm: yönetici araçları, harita, yardım wiki ve konsol yalnızca açıkken yan
  menüde görünür. Araçlar grubunu açık tutuyorsan açık başlar.*

### Changed · Değişenler

- The Encyclopedia is now called **Help wiki**.
  *Ansiklopedi'nin adı artık **Yardım wiki**.*

- Less text: the Mods page explanations moved behind small "i" icons, mod rows show one quiet
  line (role · source · version) instead of badges, the guide is in short bullet points,
  notifications are one sentence, and Home no longer repeats the firewall warning.
  *Daha az yazı: Modlar sayfasındaki açıklamalar "i" simgelerine taşındı, mod satırlarında
  rozetler yerine tek soluk satır var, rehber kısa maddeler hâlinde, bildirimler tek cümle,
  ana sayfa güvenlik duvarı uyarısını iki kez göstermiyor.*

### Fixed · Düzeltilenler

- A mod a pack needs but that wasn't on the PC was **silently skipped**: the game opened without
  it (e.g. Better Time without Mod Configuration Menu v5), showed "could not be loaded due to a
  dependency conflict" and could freeze. Now such mods are listed in the pack with **Subscribe**,
  the game won't open without them, and sending a pack adds the mods it needs.
  *Paketteki bir modun ihtiyaç duyduğu ama bilgisayarda olmayan mod sessizce atlanıyordu; oyun
  onsuz açılıp hata veriyordu. Artık pakette "Abone ol" ile görünür, oyun onsuz açılmaz, paket
  gönderilirken gereken modlar da eklenir.*
- A pack (or zip) could install a folder posing as **Coop**, CoopKeep or one of the game's modules
  into the game's Modules folder, where the game prefers it over the real one. Such folders, and
  copies of mods already installed from the Workshop, are no longer installed; packs that bring mod
  files show a "only from people you trust" note.
  *Paketle sahte Coop ya da Atölye'deki modun önüne geçen kopya kurulamaz; güven uyarısı.*
- Worlds page: rules and world settings sit under a collapsed header instead of showing 20+
  settings at once.
  *Dünyalar sayfası: kurallar kapalı bir başlığın altında.*
- Streamer mode also hides the friend's address you type to join.
  *Yayın modu arkadaşın adresini de gizler.*
- **Update now** works even if the exe was saved as "CoopKeep (1).exe" or the folder has a % sign.
  *Güncelleme exe'nin adı farklı olsa da çalışır.*
- Importing a pack no longer leaves a temporary folder behind.
  *Paket içe aktarınca geçici klasör kalmaz.*
- Start and play: if the server can't start after the game opened, you're told so clearly.
  *Başlat ve oyna: sunucu başlamazsa açıkça söylenir.*

## v1.0.0 — 2026-10-23

First public release: a launcher and server manager for the **Bannerlord Coop** mod on the
Steam Workshop. Built from a survey of the problems Coop players actually run into (Coop's
server notes and README, its GitHub issues, and our own testers).

*İlk herkese açık sürüm: Steam Atölyesi'ndeki **Bannerlord Coop** modu için başlatıcı ve
sunucu yöneticisi. Coop oyuncularının gerçekten yaşadığı sorunların taramasından yola
çıkıldı (Coop'un sunucu notları ve README'si, GitHub hata bildirimleri, kendi testçilerimiz).*

### Added · Eklenenler

- **Setup check** on Home: game version, Coop and its server package, DLCs Coop rejects
  (War Sails, …), files Windows blocked, firewall — each with a one-click fix.
  *Ana sayfada kurulum denetimi; her eksikte tek tıkla düzeltme.*
- **Pre-start check:** a missing save (Coop would silently start a new world), missing
  world mods, Steam not running (Steam search won't find you), broken Coop environment
  variables, Documents on OneDrive.
  *Başlatmadan önce denetim: kayıt yok, eksik mod, Steam kapalı, ortam değişkenleri, OneDrive.*
- **Exit codes explained** (2 load failure, 3 crash, 4 Coop module changed, 6 license) with
  what to do next, in English and Turkish.
  *Çıkış kodları iki dilde, ne yapılacağıyla açıklanır.*
- **Play:** opens the game with Coop and the world's mods and copies the server address;
  **Start and play** does both in one click. **Join a friend:** opens the game, copies the
  address, or searches Steam.
  *Oyuna gir, başlat ve oyuna gir, arkadaşına katıl.*
- **Modded Coop (experimental):** per-world mod list; mods are installed into the Coop
  server's module folder (Coop's own files untouched) and the server starts with them.
  **World mod pack** for friends; applying a pack keeps the game's modules and Coop on.
  *Modlu Coop (deneysel): dünyaya özel modlar, sunucuya kurulum, dünya mod paketi.*
- **Mod manager:** enable/disable, dependency-aware sorting, install from zip, remove to a
  backup folder; find mods on the Steam Workshop and Nexus Mods; mod packs.
  *Mod yöneticisi; Atölye ve Nexus'ta mod bulma; mod paketleri.*
- **How to use** page: every step from download to playing with friends, and the most
  common problems.
  *Nasıl kullanılır sayfası.*

- **Epic Games** installs are found too (players reported Epic joins work); Coop installed
  by hand in the game's Modules folder is recognised. Mod packs can include Workshop mods for
  friends on Epic.
  *Epic kurulumları da bulunur; elle kurulan Coop tanınır; paketler Epic'teki arkadaşlar için
  Atölye modlarını içerebilir.*
- **Mods page for Coop:** Coop pinned on top (turn it on in one click), groups (your mods /
  the game's modules / not working with Coop), filters, a Coop role per mod (UI/library, data,
  code), game-version warnings, Workshop descriptions, drag-and-drop ordering, and an apply bar
  that says what will change.
  *Coop'a göre Modlar sayfası: sabit Coop, gruplar, süzgeçler, roller, sürüm uyarısı, açıklamalar,
  sürükle-bırak, ne değişeceğini söyleyen uygula bandı.*

### Changed · Değişenler

- Home is one clear flow: **Start server** → **Play**; the invite lists the world's mods.
  *Ana sayfa tek akış: Sunucuyu başlat → Oyuna gir.*
- The new world wizard asks about mods ("No mods (recommended)" / "Modded world") and still
  continues single-player campaigns.
  *Sihirbaz modları da sorar; tek oyunculu kampanyayı sürdürme duruyor.*
- The optional server plugin stays off until you turn it on (Settings → Coop mod).
  *Sunucu eklentisi isteğe bağlı, varsayılan kapalı.*
- Lighter while hosting: resource readings no longer scan every process on the PC every two
  seconds, and the server's item list is only fetched when nobody is playing.
  *Sunucu açıkken daha hafif: kaynak ölçümü her 2 sn'de bütün süreçleri taramıyor; eşya
  listesi kimse oynamıyorken alınır.*
- An unexpected error no longer closes CoopKeep (and leaves the server orphaned); it is written
  to `crash.log` and included in the support report.
  *Beklenmeyen hata programı kapatmaz; crash.log'a yazılır, hata raporuna girer.*
- Closing CoopKeep gives the server up to two minutes to save and stop.
  *Kapanışta sunucuya kaydedip durması için 2 dakika tanınır.*
- Mods page explains its two switches: the toggle is **your game**, "Add to server" is the
  **selected world's server** (column headers and a short guide).
  *Modlar sayfası iki ayarı anlatıyor: aç/kapat senin oyunun, "Sunucuya ekle" dünyanın sunucusu.*
- Mods a mod needs (ButterLib, Harmony, MCM…) come along automatically: when you add it to a
  server, when you turn it on in your game, and when "Play" opens the game for your own server.
  A mod another server mod needs can't be removed by mistake.
  *Gereken modlar kendiliğinden gelir: sunucuya eklerken, oyununda açarken ve "Oyuna gir"de.*
- Workshop mods downloaded by **another Steam account** on the same PC are detected: the game
  can't see them, so CoopKeep marks them and offers "Subscribe on Steam". Packs, the server and
  "Play" no longer treat them as ready.
  *Başka Steam hesabının indirdiği modlar tespit edilir; "Steam'de abone ol" düğmesi çıkar.*
- Packs: applying a pack also turns on the mods it needs and asks which **server worlds** to
  install it to; the last applied pack is marked; a "Where do mods go?" guide with folder buttons.
  *Paket uygulanınca gereken modlar da açılır, hangi dünyalara kurulacağı sorulur.*
- The mod list refreshes when you come back to CoopKeep (after subscribing or installing by hand).
  *CoopKeep'e dönünce mod listesi tazelenir.*
- Home: the "mods differ" notice is a yellow note with a one-click **Match my game** button.
  *Ana sayfadaki mod farkı uyarısı bilgi notu oldu; "Oyunumu eşitle" düğmesi.*
- Sidebar: advanced pages (Cheats, Map, Encyclopedia, Console) are grouped under **Tools**.
  Contact card (gitgit.me/aderimo) in Settings and How to use.
  *Yan menü sade: ileri sayfalar "Araçlar" altında. İletişim kartı eklendi.*
- **Invite file**: one zip with the world's mods, the mods they need and how to join (Steam name,
  address, optional password, Coop version). Imported in CoopKeep it shows **Apply pack and join**.
  *Tek dosyalık davet: modlar + adres; arkadaş "Paketi uygula ve katıl" ile tek tıkla girer.*
- **Open port automatically** (UPnP) on Home; the internet address goes into the invite.
  *Modem destekliyorsa port tek tıkla açılır.*
- Coop update notice on Home: friends must update too, versions must match.
  *Coop güncellenince ana sayfada uyarı.*
- Apply fixes the load order by itself when a mod loads before a mod it needs.
  *Uygula, gereken modlardan önce yüklenen modların sırasını kendisi düzeltir.*
- Less memory: map layers only hold their own settlements and the map page is released when you
  leave it (peak ~440 → ~275 MB in a measured run).
  *Daha az bellek: harita katmanları ayrıldı, harita sayfası çıkınca bırakılıyor.*
- Before starting, missing world mods come with a **Get missing mods** button: Steam subscribe
  pages open for known Workshop mods, unknown ones are searched in Find mods. Messages show mod
  names and say when a mod is only "not subscribed". Installing a pack to a world warns about
  mods that aren't on this PC.
  *Başlatmadan önce eksik modlar için "Eksikleri indir" düğmesi; paket dünyaya kurulurken eksik uyarısı.*
- **Start and play opens the game first**; the server gets ready in the background and you are
  told (address on the clipboard) when it is serving. Play no longer fails if the game is open.
  *"Başlat ve oyna" önce oyunu açar, sunucu arkada hazırlanır.*
- "Cheats" is now **Admin tools**; pages that need the optional server add-on say so, with a
  button to turn it on (the add-on is marked experimental).
  *"Hileler" artık "Yönetici araçları"; eklenti gereken yerlerde açıklama kartı.*
- Notification history: a bell in the sidebar keeps the last 30 notifications.
  *Bildirim geçmişi: yan menüde zil.*
- Notices show mod names instead of ids.
  *Uyarılarda mod kimliği yerine adı.*
- Home shows the journey **Setup → World → Server → Friends** with the current step highlighted.
  *Ana sayfada yolculuk: Kurulum → Dünya → Sunucu → Arkadaşlar.*
- Mods page **Compact** view (one line per mod).
  *Modlar sayfasında kompakt görünüm.*
- Empty pages guide you: no players → invite buttons; no backups → how backups work.
  *Boş sayfalar yönlendiriyor.*
- **System tray** icon: server status and player count on hover; show, stop server or quit.
  *Sistem tepsisi simgesi: durum, oyuncu sayısı, göster/durdur/çık.*
- **Player joined alert** while CoopKeep is in the background: a short sound and a flashing taskbar
  icon (Settings → Automation).
  *Oyuncu katılınca ses ve görev çubuğu uyarısı.*
- **Update now**: downloads the new CoopKeep.exe from the GitHub release and restarts on it
  (stop the server first).
  *Program içinden güncelleme.*
- Mods page **Refresh** button. When Workshop folders were deleted by hand, Steam still thinks
  they are installed and won't download them again: CoopKeep detects this and offers
  **Verify in Steam** (verifying game files downloads them).
  *Yenile düğmesi; elle silinen Atölye modları için "Steam'de doğrula".*
- Packs: **Play with this pack** opens the game straight away with the pack's mods (plus the mods
  they need, in order) and Coop; no ticking in the launcher.
  *Paketler: "Oyunu bu paketle aç".*
- **Load order fix:** libraries like Harmony, ButterLib, UIExtenderEx and MCM now load **before**
  the game's own modules, as they ask in their SubModule.xml. Before, the game showed "ButterLib is
  loaded after Native, terminate the game?". Applies when CoopKeep opens the game and when it
  writes the launcher list; the Mods page also flags "must load before" problems.
  *Yükleme sırası düzeltildi: kütüphaneler oyunun modüllerinden önce yüklenir.*

---

## v0.5.0 — 2026-10-17

Fourth round, after a live test with a friend. The new server commands were tried on a
copy of the Coop v0.1.6 server first (`Fixtures/server-tools-v2.txt`).

*Dördüncü tur: arkadaşla yapılan canlı testten sonra. Yeni sunucu komutları önce Coop
v0.1.6 sunucusunun bir kopyasında denendi.*

### Added · Eklenenler

- **Player profile** window, opened from Players, Cheats and the Map: level, gold,
  health, age, influence, renown, the skills screen, battle and civilian equipment with
  real item images, inventory and army with troop portraits; quick actions.
  *Oyuncu profili: beceriler, kuşanım, envanter, ordu ve hızlı eylemler.*
- **Skills screen like the game's:** 6 attributes, 18 skills with their icons, focus
  points, level and total XP. Click a skill to change its level, focus or XP; click an
  attribute to add points; unspent points can be given too. Values are read back from the
  server after every change.
  *Oyundaki gibi beceri ekranı; her değişiklikten sonra sunucudan yeniden okunur.*
- **Encyclopedia tab:** kingdoms, clans, lords, settlements, troops (upgrade trees) and
  items with the game's own texts — in Turkish from the game's own translation — plus
  guides computed from game data: strongest troops per tier, richest towns, strongest
  clans, best castle locations, best weapons/armor/horses. Live owner, prosperity and
  lord location are added while the server runs.
  *Ansiklopedi sekmesi ve verilerden hesaplanan rehberler.*
- **Map:** teleport from the map (pick a player → Teleport → click or search a
  settlement → confirm); all lords on the map with an info card; level-of-detail labels.
  *Haritadan ışınlama, bütün lordlar, yakınlığa göre ayrıntı.*
- Player search on Players and Cheats.
  *Oyuncular ve Hileler'de oyuncu arama.*
- Server plugin v2: `hero_stats`, `add_skill`, `add_skill_xp`, `add_focus`,
  `add_attribute`, `add_unspent`, `hero_equipment`, `roster`, `parties`, `owners`,
  `settlement_stats`, `item_stats`.

### Changed · Değişenler

- **New worlds are created instantly** from the game's `default_new_game.sav` template;
  the server starts once (before: one full boot to create, another to start).
  *Yeni dünya anında kurulur; sunucu bir kez açılır.*
- Nothing heavy runs the moment the server goes live: the item list and prices are cached
  for a day, characters are refreshed when their page opens (details only for online
  players), the map reads all parties and owners with one command each.
  *Yayına geçince sunucuya yük binmez.*
- Map: zooming out keeps the map centered; markers keep their size on screen; village
  and castle names only when zoomed in; kingdom names on top in a readable frame; lords
  inside a settlement are not drawn (as in the game); movement is smooth (2 s polls with a
  linear 2 s glide).
  *Harita: ortalı uzaklaştırma, sabit boylu işaretler, akıcı hareket, karmaşa giderildi.*
- Cheats: the info panel and actions appear only after a player is picked; the Teleport
  tab moved to the Map.
  *Hileler: panel yalnızca oyuncu seçilince; ışınlama haritaya taşındı.*
- Players: the separate "block list" is gone — players banned by name show up in Bans
  with a "by name" tag.
  *Engel listesi kalktı; ad ile banlananlar Banlılar'da.*
- Thin, centered number fields; Backups scope buttons no longer look highlighted;
  Mods list is paged with an info window (Steam Workshop description and cover).
  *İnce sayı alanları, Yedekler düğmeleri, sayfalı mod listesi ve mod bilgi penceresi.*
- Plugin data lines are no longer printed to the console.

### Fixed · Düzeltilenler

- **"Take troops" list was empty for players:** Coop's `character_ids` only accepts the
  game id (`lord_1_1`), not the registry id saved for players (`Hero_…`). The plugin now
  accepts both; the fallback strips the prefix.
  *"Asker al" listesi boştu: kimlik biçimi düzeltildi.*
- **Attribute, focus and skill XP did not work:** replaced with plugin commands that are
  verified by reading the values back. Skill XP follows the game's learning rate, so the
  screen also offers direct level changes.
  *Özellik, odak ve yetenek XP'si çalışmıyordu.*
- Clicking a town's banner on the map now always opens it, whatever layers are on.
  *Haritaya basılı sancaklar her zaman tıklanabilir.*

---

## v0.4.0 — 2026-10-16

Third round of user feedback: a separate Cheats tab, real game visuals, a real map and
Steam bans. Every new server command was tried on a Coop v0.1.6 server first
(`Fixtures/troops-clans.txt`, `Fixtures/server-tools.txt`).

*Üçüncü geri bildirim turu: ayrı Hileler sekmesi, oyunun gerçek görselleri, gerçek harita
ve Steam banları. Her yeni sunucu komutu önce Coop v0.1.6 sunucusunda denendi.*

### Added · Eklenenler

- **Cheats tab** (moved out of Players): pick one, several or all players; Gold, Health,
  Army, Clan, Character, Teleport, Items. Every "give" has a "take" where the server
  supports it; the last actions can be undone.
  *Hileler sekmesi: ver/al/ayarla, çoklu hedef, geri alma.*
- **Troops by type and count** with the troops' real portraits, culture/class filters and
  a basket; take troops from a player's army; heal wounded (`set_troop_state`).
  *Asker türü ve sayısı seçilerek ver/al, gerçek asker portreleri.*
- **Items screen:** all 1,285 game items (weapons, armor, civilian/battle clothes,
  horses, food, materials) grouped, searchable, with the cheapest and most expensive town
  price; give or take the items themselves, or their value as gold.
  *Eşya ekranı: gruplar, arama, kasaba fiyatları, eşya ver/al.*
- **CoopKeep server add-on** (opt-in, Settings): Coop has no item command, so CoopKeep can
  add a small sub-module to your own Coop install for `coopkeep.give_item/take_item`, the
  item list and town prices. Turning it off restores the original files.
  *İsteğe bağlı sunucu eklentisi: eşya verme, tam eşya listesi, fiyatlar.*
- **Real map:** the in-game Calradia map with the game's town banners, castles, villages,
  kingdom names, rulers and searchable lords ("Who's where?"), player trails, starred
  players drawn larger with a yellow ring, alerts for battles, sieges and captured
  settlements, ownership history. Zoom with the wheel, drag to pan.
  *Gerçek harita: katmanlar, öncelikli oyuncular, el değiştirme bildirimleri.*
- **Viewer mode:** players who don't host can watch the live map from their own CoopKeep
  (read-only, share code).
  *İzleyici modu: arkadaşlar haritayı kendi CoopKeep'lerinden izler.*
- **Steam bans** through Coop's own `steam-bans.json` (no restart needed), unban, undo,
  last seen and play time per player.
  *Steam hesabıyla ban, ban kaldırma, geri alma, son görülme.*
- **All mods tab:** 529 Bannerlord mods from the community list, searchable even when not
  installed, with ✓/✗ report counts; reports are tallied automatically by a GitHub Action.
  *Tüm modlar sekmesi ve otomatik sayılan bildirimler.*
- **Image pack** as a separate download (`tools/build-image-pack.ps1`): troop and lord
  portraits, settlement pictures and the map from community wikis; items, troops and
  banners rendered by the game itself (`CoopKeep.ImageExporter`). No game images are
  stored in this repository.
  *Ayrı indirilen görsel paketi; repoda oyun görseli yok.*

### Known limitations · Bilinen sınırlar

- Coop can't lower renown, attribute or focus points; there is no god mode.
  *Ün, özellik ve odak puanı azaltılamıyor; tanrı modu yok.*
- A player's own custom banner can't be read from the server; their kingdom's banner is shown.
  *Oyuncunun kendi sancağı okunamıyor; krallığının sancağı gösterilir.*

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
