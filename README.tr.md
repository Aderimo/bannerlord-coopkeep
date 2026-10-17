<div align="center">

# CoopKeep

**Bannerlord Coop sunucu yöneticisi**

[![Sürüm](https://img.shields.io/badge/s%C3%BCr%C3%BCm-v0.5.0-C9A227)](../../releases)
[![Lisans](https://img.shields.io/badge/lisans-MIT-4ADE80)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-6B7280)](#kurulum)

Kendi bilgisayarında Bannerlord Coop sunucusu açmayı tek tıklık hâle getirir.
Komut satırı yok, elle dosya düzenleme yok, oyuna girip save yaratma yok.

[English](README.md) · **Türkçe**

</div>

---

## Neden

Bannerlord Coop modu co-op kampanyayı çalışır hâle getiriyor, ama işletme tarafı
tamamen elle: DLL'leri unblock etmek, önce oyunda tek oyunculu bir save yaratmak,
`server-config.json`'ı Notepad'le düzenlemek, `.exe`'yi çift tıklayıp konsol
penceresini açık tutmak, save bozulursa dosyaları elle kopyalamak.

CoopKeep bu yükü tamamen üstlenir. Mod dosyalarına dokunmaz; yalnızca isteğe bağlı
sunucu eklentisini açarsan (aşağıda) dokunur.

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

1. Steam'den **Bannerlord**'u kur
2. Steam Atölyesi'nden **Bannerlord Coop** moduna abone ol
3. [Releases](../../releases) sayfasından **`CoopKeep.exe`** indir
4. Çift tıkla. Bu kadar.

> **Kurulum gerektirmez.** Oyun klasörüne hiçbir şey kopyalanmaz, hiçbir dosya
> değiştirilmez. İstediğin klasörde durabilir; silmek istersen exe'yi silmen yeterli.
> (Tek istisna isteğe bağlı sunucu eklentisi; kapatınca geri kaldırılır.)

**Arkadaşlarının CoopKeep'e ihtiyacı yok.** Onlar sadece aynı Coop moduna abone
olup oyundan bağlanır.

---

## Nasıl kullanılır

### 1 · Dünyanı kur

İlk açılışta ana sayfa **Dünya oluştur** der. Sihirbaz, oyunda yeni kampanya açmak gibi
ilerler:

1. **Başlangıç** — sıfırdan yeni bir sandbox ya da oyunda başlattığın bir kampanya
2. **Ad**
3. **Zorluk** — Çok kolay / Kolay / Gerçekçi ya da her ayarı tek tek seç
   (hasar, savaş yapay zekâsı, asker toplama, harita hızı, ikna…)
4. **Kurallar** — savaşta ölüm, yaşlanma, infaz, savaş boyutu, haydutlar, gezginler,
   ekonomi, hızlı ileri sarma, sesli sohbet, **oyuncu hileleri**
5. **Sunucu** — şifre ve Steam görünürlüğü, ardından özet

> Oyuna girmene gerek yok — dünyayı sunucu sıfırdan kurar. Her dünyanın ayarları
> kendine aittir; **Dünyalar** sayfasından istediğin zaman değiştirebilirsin.

### 2 · Başlat

**Sunucuyu başlat**'a bas. İlk seferde Coop sunucusunun lisans sözleşmesi gösterilir;
onaylamadan sunucu açılmaz. Ana sayfa ilerlemeyi gösterir (`Motor açılıyor` →
`Dünya yükleniyor` → **`Yayında`**). Yaklaşık bir dakika.

### 3 · Arkadaşlarını çağır

**Daveti kopyala**, arkadaşlarının ihtiyaç duyduğu her şeyi tek mesajda verir. Oyunu
açıp **Coop** → **Steam Lobileri**'ne girerler ve **senin Steam adını** ararlar —
sunucu adını değil.

Doğrudan IP ile bağlanacaklarsa router'ından **UDP 4200** portunu yönlendirmen gerekir.
Steam üzerinden gelenler için gerekmez.

---

## Neler yapabilir

### Dünyalar
- Zorluk ön ayarları ve Coop modunun tüm oyun ayarlarıyla **yeni dünya sihirbazı**
- **Dünyaya özel ayarlar**; o dünya her başladığında otomatik uygulanır
- **Tek oyunculu save'i içe aktarma**; üçüncü parti modlarla yapılmış save'ler tespit
  edilip engellenir (sunucu onları yüklerken çöküyor)
- Yeniden adlandırma, kopyalama, silme, klasörünü açma

### Sunucu
- Başlat, güvenle durdur (dünya kaydedilerek), canlı durum, işlemci ve bellek
- **Uyararak yeniden başlat:** oyunculara 60 saniye önceden duyuru, dünya kaydedilir
- **Çökerse otomatik yeniden başlatma** — üstel bekleme ve çökme döngüsü koruması

### Oyuncular
- Canlı liste, arama, atma, duyuru
- Coop sunucusunun kendi ban listesiyle **Steam banı**; yeniden başlatmak gerekmez.
  Steam kimliği henüz bilinmeyen oyuncu adıyla banlanır
- **Oyuncu profili:** seviye, altın, can, yaş, nüfuz, ün, oyundaki gibi beceri ekranı,
  savaş ve şehir kuşanımı (gerçek eşya görselleriyle), envanter ve ordu

### Hileler
Hile yapan birinin verdiği zararı telafi etmek için. Bir, birkaç ya da bütün oyuncular:
- Altın, can, klan nüfuzu ve ünü, yaş
- Gerçek portreleriyle **tür ve sayı seçerek asker** ver; ordudan asker al; yaralıları iyileştir
- **Eşyalar:** oyundaki bütün eşyalar, gruplu ve aranabilir, kasaba fiyatlarıyla
- Beceri ekranında **beceri, odak ve özellik puanları**
- Son işlemler geri alınabilir; her işlem doğrulanır ve denetim kaydına yazılır

### Harita
- Kasabalar, kaleler, köyler, krallıklar ve el değiştirme uyarılarıyla gerçek Calradia haritası
- Oyuncular ve **bütün lordlar** canlı hareket eder; tıklayınca bilgi kartı açılır
- **Haritadan ışınlama:** oyuncuyu seç, sonra bir yerleşime tıkla ya da ara
- **İzleyici modu:** arkadaşların haritanı kendi CoopKeep'lerinden izleyebilir

### Ansiklopedi
- Krallıklar, klanlar, lordlar, yerleşimler, askerler (terfi ağaçları) ve eşyalar;
  metinler oyunun kendi ansiklopedisinden, oyunun kendi Türkçe çevirisiyle
- Oyun verilerinden hesaplanan **rehberler:** en güçlü askerler, en zengin kasabalar,
  en güçlü klanlar, en iyi kale konumları, en iyi silah, zırh ve atlar

### Modlar
- Oyun başlatıcındaki her modun durumu (uyumlu / sunucuda yok / Coop reddediyor)
- Topluluk sonuçlarıyla **bütün modlar listesi**; moda tıklayınca açıklaması açılır
- **Coop için düzenle:** sorunlu modları tek tıkla kapat, sonra geri aç

### Yedekler
- **Her kayıttan sonra otomatik yedek**, işlemsel geri yükleme, sayı *ve* yaş bazlı temizleme

### Diğer
- **Yayın modu:** IP, port ve Steam adını maskeler, konsolu bulanıklaştırır
- **Hata raporu:** loglar ve ayarlar tek zip'te (şifre ve Steam kimlikleri gizlenir)
- Güncelleme denetimi (kapatılabilir), Türkçe ve İngilizce

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
| **Bağlanamıyor** | CoopKeep'teki **Modlar** sayfasına bak. Oyun başlatıcında fazladan mod seçiliyse kapat. |
| **Başlat çalışmıyor** | Zaten çalışan bir Coop sunucusu olabilir — CoopKeep uyarır. Görev Yöneticisi'nden kapat. |
| **Şifre işe yaramıyor** | Şifreyi yazdıktan sonra **Başlat**'a bas; sunucu ayarları yalnızca açılışta okur. |
| **Sunucu hemen kapanıyor (kod 6)** | Coop sunucusunun lisans sözleşmesi onaylanmamış. CoopKeep'ten tekrar başlat ve onayla. |
| **Save içe aktarılmıyor** | Save, sunucunun yükleyemediği modlarla yapılmış. Oyunda modsuz yeni bir sandbox başlat, kaydet ve onu içe aktar. |
| **Mod DLL'leri yüklenmiyor** | Windows indirilen DLL'leri bloke etmiş olabilir. Mod klasöründe:<br>`Get-ChildItem "<mod klasörü>" -Recurse \| Unblock-File` |

---

## Bilinen sınırlar

Bunlar CoopKeep'in eksiği değil, **modun kendi sınırları** — araştırmayla doğrulandı:

- **Oyuncu limiti yok.** Bannerlord Coop bir slot sınırı uygulamıyor; dışarıdan
  bağlantı reddedilemiyor.
- **Oyunun kendi konsol hileleri sunucuda kapalı** (`Cheat mode is disabled!`).
  CoopKeep'in Hileler sekmesi Coop'un kendi komutlarıyla ve isteğe bağlı **CoopKeep
  sunucu eklentisiyle** (eşyalar, beceriler, profil, hızlı harita) çalışır. Eklenti sen
  açmadıkça kapalıdır; açınca Coop modunun klasörüne alt modül olarak eklenir (asıl
  dosyanın yedeği alınır), kapatınca kaldırılır. Yalnızca sunucu konsolu kullanabilir.
- **Ün azaltılamıyor**, tanrı modu yok.
- **Sunucuya mod eklenemiyor.** Yalnızca resmî modülleri ve Coop'u yüklüyor;
  oyuncuların diğer modları kapatması gerekir (Modlar sayfası yardım eder).
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
| stdin komutları | `status` · `players` · `save` · `say` · `kick` · `stop` ve izin listesindeki Coop konsol komutları (`coop.debug.hero.*`, `coop.server.shutdown`) |
| stdout `@DS@` olayları | Durum fazı, canlı oyuncu listesi (JSON) |
| Dosya sistemi | `server-config.json`, `Game Saves\`, yedekler ve Coop'un oyun ayarları `CoopData\mod-config.json` |

Yapılandırma dosyası **yorumları, sırası ve tanımadığımız anahtarları korunarak**
düzenlenir; mod güncellendiğinde ayarların kaybolmaz.

---

## Geliştirme

```bash
dotnet test                                    # 229 test
dotnet run --project src/CoopKeep.Cli -- doctor
powershell -File publish.ps1                   # tek dosyalık exe üretir
```

| Proje | Sorumluluk |
|---|---|
| `src/CoopKeep.Core` | Süreç yönetimi, protokol, yedekleme, kurulum tespiti — **UI'dan bağımsız** |
| `src/CoopKeep.App` | Avalonia arayüzü (Türkçe/İngilizce, koyu tema) |
| `src/CoopKeep.Cli` | Komut satırı aracı |
| `tests/CoopKeep.Core.Tests` | 229 test |

Sürüm numarasının tek kaynağı `Directory.Build.props`.

Katkılar memnuniyetle karşılanır. Bu depodaki yorumlar, commit mesajları ve testler
Türkçe yazılıyor.

---

## Lisans

[MIT](LICENSE) — özgürce kullan, değiştir, dağıt.

CoopKeep **bağımsız bir araçtır**; Bannerlord Coop ekibiyle veya TaleWorlds ile
bağlantısı yoktur ve onların kodunu içermez.

<div align="center">

Geliştiren · **[aderimo](https://gitgit.me/aderimo)**

</div>
