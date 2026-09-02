<div align="center">

# CoopKeep

**Mount & Blade II: Bannerlord — Coop sunucu yöneticisi**
*Bannerlord Coop server manager*

[![Sürüm](https://img.shields.io/badge/s%C3%BCr%C3%BCm-v0.1.0-C9A227)](../../releases)
[![Lisans](https://img.shields.io/badge/lisans-MIT-4ADE80)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-6B7280)](#kurulum)

Kendi bilgisayarında Bannerlord Coop sunucusu açmayı tek tıklık hâle getirir.
Komut satırı yok, elle dosya düzenleme yok, oyuna girip save yaratma yok.

</div>

---

## Neden

Bannerlord Coop modu co-op kampanyayı çalışır hâle getiriyor, ama işletme tarafı
tamamen elle: DLL'leri unblock etmek, önce oyunda tek oyunculu bir save yaratmak,
`server-config.json`'ı Notepad'le düzenlemek, `.exe`'yi çift tıklayıp konsol
penceresini açık tutmak, save bozulursa dosyaları elle kopyalamak.

CoopKeep bu yükü tamamen üstlenir ve **mod dosyalarına hiç dokunmaz.**

---

## Kurulum

### Gereksinimler

| | |
|---|---|
| Oyun | Mount & Blade II: Bannerlord **v1.4.8** (Steam) |
| Mod | [Bannerlord Coop](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698) — Steam Atölyesi'nden abone ol |
| İşletim sistemi | Windows 10/11 |
| .NET | **Gerekmiyor** — exe kendi kendine yeter |

### Adımlar

1. Steam'den **Bannerlord**'u kur (zaten varsa geç)
2. Steam Atölyesi'nden **Bannerlord Coop** moduna abone ol
3. [Releases](../../releases) sayfasından **`CoopKeep.exe`** dosyasını indir
4. Çift tıkla. Bu kadar.

> **Kurulum gerektirmez.** Oyun klasörüne hiçbir şey kopyalanmaz, hiçbir dosya
> değiştirilmez. İstediğin klasörde durabilir; silmek istersen exe'yi silmen yeterli.

**Arkadaşlarının CoopKeep'e ihtiyacı yok.** Onlar sadece aynı Coop moduna abone
olup oyundan bağlanır.

---

## Nasıl kullanılır

### 1 · Sunucu seç veya yarat

Sol paneldeki listeden bir sunucu seç. Hiç yoksa alttaki kutuya bir ad yazıp
**Yeni sunucu**'ya bas.

> Oyuna girmene gerek yok — dünyayı sunucu sıfırdan kurar. Yaklaşık bir dakika sürer.

### 2 · Ayarları yap (isteğe bağlı)

**Ayarlar** bölümünden şifre, port, otomatik kayıt aralığı ve Steam görünürlüğünü
belirle. Sunucu duruyorken değiştirilir; **Başlat'a bastığında ekranda ne yazıyorsa
sunucu onunla açılır.**

### 3 · Başlat

**Başlat**'a bas ve bekle. Durum sırayla `Motor açılıyor` → `Dünya yükleniyor` →
**`Yayında`** olur. Toplam yaklaşık bir dakika.

### 4 · Arkadaşların nasıl girer

Oyunu açarlar → **Coop** menüsü → **Steam Lobbies** sekmesi.

> ⚠️ **En sık yapılan hata:** Aradıkları isim **senin Steam adın**, sunucu adı değil.
> CoopKeep bunu "Arkadaşlarına gönder" kutusunda gösterir ve **Kopyala** ile
> panoya alabilirsin.

Doğrudan IP ile bağlanacaklarsa: router'ından **UDP 4200** portunu yönlendirmen
gerekir. Steam üzerinden gelenler için gerekmez.

---

## Neler yapabilir

### Sunucu
- Başlat, güvenle durdur (dünya kaydedilerek), canlı durum ve faz göstergesi
- İşlemci ve bellek kullanımı
- **Çökerse otomatik yeniden başlatma** — üstel bekleme ve çökme döngüsü koruması

### Oyuncular
- Canlı liste, arama, **oyuncu atma** (`kick`)
- Tüm oyunculara duyuru gönderme
- **Yönetici işlemleri:** seçili oyuncuya tam can verme ve altın ayarlama.
  Her işlem kalıcı bir denetim kaydına yazılır ve arayüzde görünür.

### Dünyalar ve yedekler
- Yeni dünya yaratma, yeniden adlandırma, kopyalama, silme, klasörünü açma
- **Her kayıttan sonra otomatik yedek**, işlemsel geri yükleme
- Sayı *ve* yaş bazlı otomatik temizleme

### Yayın yapanlar için
- **Göz düğmesi:** IP, port ve Steam adını maskeler, konsolu bulanıklaştırır.
  Varsayılan olarak açıktır.

### Kurulum denetimi
- Steam, oyun ve mod otomatik bulunur; sürüm uyumu denetlenir
- **Modül uyumu denetimi:** oyun başlatıcında sunucunun beklemediği modüller
  seçiliyse uyarır — bağlantı reddedilmelerinin en olası sebebi budur

---

## Komut satırı

Arayüz istemeyenler için `CoopKeep.Cli`:

```bash
coopkeep doctor          # kurulumu bul ve uyumluluğu denetle
coopkeep saves           # sunucuları listele (oyuncularıyla)
coopkeep new Calradia    # yeni dünya yarat (oyuna girmeden)
coopkeep use Calradia    # aktif sunucuyu değiştir
coopkeep run             # başlat ve konsolu sür
```

---

## Sorun giderme

| Sorun | Sebep ve çözüm |
|---|---|
| **Arkadaşım sunucuyu bulamıyor** | Sunucu listesinde **senin Steam adını** aratıyor mu? Sunucu adını değil. |
| **Bağlanamıyor** | CoopKeep'teki **Modül uyumu** bölümüne bak. Oyun başlatıcında fazladan mod seçiliyse kapat. |
| **Başlat çalışmıyor** | Zaten çalışan bir Coop sunucusu olabilir — CoopKeep uyarır. Görev Yöneticisi'nden kapat. |
| **Şifre işe yaramıyor** | Şifreyi yazdıktan sonra **Başlat**'a bas; sunucu ayarları yalnızca açılışta okur. |
| **Mod DLL'leri yüklenmiyor** | Windows indirilen DLL'leri bloke etmiş olabilir. Mod klasöründe PowerShell ile:<br>`Get-ChildItem "<mod klasörü>" -Recurse \| Unblock-File` |

---

## Bilinen sınırlar

Bunlar CoopKeep'in eksiği değil, **modun kendi sınırları** — araştırmayla doğrulandı:

- **Oyuncu limiti yok.** Bannerlord Coop bir slot sınırı uygulamıyor; dışarıdan
  bağlantı reddedilemiyor.
- **Ban yok, yalnızca kick var.** Steam kimliği canlı protokole yazılmıyor, bu
  yüzden kalıcı ban ancak isim bazlı olabilirdi.
- **Öldürme işlemi yok.** Sunucu vanilla hile komutlarını reddediyor
  (`Cheat mode is disabled!`) ve canı sıfırlamanın sonucu doğrulanamadı.
- **"Sadece arkadaşlar" görünürlüğü** dedicated server'da doğrulanamadı.
- **Bellek sınırı ayarlanamıyor** — sunucunun böyle bir seçeneği yok.
- Yalnızca **Steam** kurulumları tespit ediliyor (Epic/GOG desteği yok).
- Aynı anda **tek sunucu** yönetilebiliyor.

Tam liste: [CHANGELOG.md](CHANGELOG.md)

---

## Nasıl çalışır

CoopKeep, Bannerlord Coop modunun dosyalarına **dokunamaz** — dedicated server dört
Coop assembly'sini SHA-256 ile doğruluyor ve değiştirilmiş bir modülde exit code 4
ile açılmayı reddediyor. Bu yüzden entegrasyon yüzeyi kod değil:

| Kanal | Ne için |
|---|---|
| Süreç yaşam döngüsü | Başlatma, durdurma, çökme tespiti (çıkış kodları 0/2/3/4) |
| stdin komutları | `status` · `players` · `save` · `say` · `kick` · `stop` |
| stdout `@DS@` olayları | Durum fazı, canlı oyuncu listesi (JSON) |
| Dosya sistemi | `server-config.json`, `Game Saves\`, yedekler |

Yapılandırma dosyası **yorumları, sırası ve tanımadığımız anahtarları korunarak**
düzenlenir; mod güncellendiğinde ayarların kaybolmaz.

---

## Geliştirme

```bash
dotnet test                                    # 178 test
dotnet run --project src/CoopKeep.Cli -- doctor
powershell -File publish.ps1                   # tek dosyalık exe üretir
```

| Proje | Sorumluluk |
|---|---|
| `src/CoopKeep.Core` | Süreç yönetimi, protokol, yedekleme, kurulum tespiti — **UI'dan bağımsız** |
| `src/CoopKeep.App` | Avalonia arayüzü (Türkçe/İngilizce, koyu tema) |
| `src/CoopKeep.Cli` | Komut satırı aracı |
| `tests/CoopKeep.Core.Tests` | 178 test |

Sürüm numarasının tek kaynağı `Directory.Build.props`.

---

## Lisans

[MIT](LICENSE) — özgürce kullan, değiştir, dağıt.

CoopKeep **bağımsız bir araçtır**; Bannerlord Coop ekibiyle veya TaleWorlds ile
bağlantısı yoktur ve onların kodunu içermez.

*CoopKeep is an independent tool. It is not affiliated with the Bannerlord Coop
team or TaleWorlds, and contains none of their code.*

<div align="center">

Geliştiren · **[aderimo](https://gitgit.me/aderimo)**

</div>
