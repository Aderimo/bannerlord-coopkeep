# CoopKeep

**Mount & Blade II: Bannerlord — Coop sunucu yöneticisi**
*Bannerlord Coop server manager*

Kendi bilgisayarında Bannerlord Coop sunucusu açmayı tek tıklık hâle getirir.
Komut satırı, elle dosya düzenleme veya oyuna girip save yaratma yok.

*Turns hosting a Bannerlord Coop server on your own PC into a single click — no
command line, no hand-editing config files, no need to create a save in-game first.*

---

## Ne yapar / What it does

- **Oyuna hiç girmeden yeni sunucu dünyası yaratır.** Modun kurulum talimatındaki
  "önce tek oyunculu bir save yarat" adımı tamamen gereksizleşir.
- Sunucuyu başlatır, güvenle durdurur (dünya kaydedilerek), durumunu canlı gösterir.
- Bağlı oyuncuları listeler, arama yapar, oyuncu atar (`kick`).
- Oyunculara duyuru gönderir.
- Şifre, port, otomatik kayıt aralığı ve Steam görünürlüğünü arayüzden ayarlar.
- **Her kayıttan sonra otomatik yedek alır**, geri yükler; eski yedekleri sayı ve
  yaşa göre temizler.
- Sunucu çökerse üstel beklemeyle yeniden başlatır; döngüye girerse durur.
- **Yayın modu:** IP, port ve Steam adını maskeler, konsolu bulanıklaştırır.
- Türkçe ve İngilizce, koyu tema.

## Kurulum / Install

1. Steam'den **Mount & Blade II: Bannerlord**
2. Steam Atölyesi'nden **Bannerlord Coop** moduna abone ol
3. [Releases](../../releases) sayfasından `CoopKeep.exe` indir, çift tıkla

Kurulum gerektirmez, oyun klasörüne hiçbir şey kopyalamaz. Tek dosya, kendi kendine
yeten bir uygulama — **.NET kurulu olması gerekmez.**

*No installer, nothing copied into the game folder. Single self-contained
executable — .NET is not required.*

Sunucuya bağlanacak arkadaşlarının CoopKeep'e ihtiyacı yok; onlar aynı Coop moduna
abone olup oyundan bağlanır.

## Kullanım / Usage

1. Soldaki listeden bir sunucu seç, ya da bir ad yazıp **Yeni sunucu**'ya bas
2. **Başlat** — dünya yüklemesi yaklaşık bir dakika sürer
3. Durum **Yayında** olunca arkadaşların oyundan bağlanabilir

> **Önemli:** Arkadaşların Steam sunucu tarayıcısında **senin Steam adını** arar,
> sunucu adını değil. CoopKeep bu adı sunucu panelinde gösterir.

## Nasıl çalışır / How it works

CoopKeep, Bannerlord Coop modunun dosyalarına **dokunmaz**. Dedicated server dört
Coop assembly'sini SHA-256 ile doğruluyor ve değiştirilmiş bir modülde açılmayı
reddediyor — bu yüzden CoopKeep tamamen dışarıdan çalışan bir süreç yöneticisidir:

- `BannerlordCoopServer.exe` süreç yaşam döngüsü
- stdin üzerinden komutlar (`status`, `players`, `save`, `say`, `kick`, `stop`)
- stdout'taki `@DS@` JSON olay akışı (durum fazı, oyuncu listesi)
- `server-config.json` ve `Game Saves\` dosya yönetimi

Yapılandırma dosyası **yorumları, sırası ve tanımadığımız anahtarları korunarak**
düzenlenir; mod güncellendiğinde ayarların kaybolmaz.

## Geliştirme / Development

```
dotnet test              # 138 test
dotnet run --project src/CoopKeep.Cli -- doctor
powershell -File publish.ps1
```

- `src/CoopKeep.Core` — süreç yönetimi, protokol, yedekleme, kurulum tespiti (UI'dan bağımsız)
- `src/CoopKeep.App` — Avalonia arayüzü
- `src/CoopKeep.Cli` — komut satırı aracı
- `tests/CoopKeep.Core.Tests`

## Lisans / License

MIT — bkz. [LICENSE](LICENSE).

CoopKeep bağımsız bir araçtır; Bannerlord Coop ekibiyle ya da TaleWorlds ile
bağlantısı yoktur ve onların kodunu içermez.

*CoopKeep is an independent tool. It is not affiliated with the Bannerlord Coop
team or TaleWorlds, and contains none of their code.*

Geliştiren: [aderimo](https://gitgit.me/aderimo)
