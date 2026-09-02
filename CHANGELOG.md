# Changelog · Değişiklik günlüğü

Versioning follows [Semantic Versioning](https://semver.org/).
The single source of the version number is `Directory.Build.props`.

*Sürümlendirme [Semantic Versioning](https://semver.org/lang/tr/) kuralına uyar.
Sürüm numarasının tek kaynağı `Directory.Build.props` dosyasıdır.*

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
