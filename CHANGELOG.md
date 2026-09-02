# Değişiklik günlüğü / Changelog

Sürümlendirme [Semantic Versioning](https://semver.org/lang/tr/) kuralına uyar.
Sürüm numarasının tek kaynağı `Directory.Build.props` dosyasıdır.

---

## v0.1.0 — 2026-09-02

İlk sürüm. Bannerlord Coop dedicated server'ını tamamen dışarıdan yöneten
masaüstü uygulaması.

*First release. A desktop app that manages the Bannerlord Coop dedicated server
entirely from the outside.*

### Eklenenler / Added

- **Oyuna girmeden sunucu dünyası yaratma.** Modun kurulum talimatındaki
  "önce tek oyunculu bir save yarat" adımı gereksizleşiyor.
- Sunucu başlatma, güvenli durdurma (dünya kaydedilerek), canlı durum ve faz
  göstergesi.
- Canlı oyuncu listesi, oyuncu arama, `kick`.
- Oyunculara duyuru gönderme.
- Şifre, port, otomatik kayıt aralığı ve Steam görünürlüğü ayarları.
- **Otomatik yedekleme** (her kayıttan sonra), işlemsel geri yükleme, sayı ve
  yaş bazlı temizleme.
- **Çökme kurtarma**: üstel bekleme ve çökme döngüsü koruması.
- **Yönetici işlemleri**: seçili oyuncuya tam can verme ve altın ayarlama;
  her işlem kalıcı bir denetim kaydına yazılıyor.
- **Yayın modu**: IP, port ve Steam adını maskeler, konsolu bulanıklaştırır.
- **Bağlantı bilgisi kutusu**: Steam adı, yerel IP ve port, tek tıkla kopyalanır.
- **Modül uyumluluk denetimi**: oyun başlatıcısında fazladan seçili modülleri
  uyarır (bağlantı reddedilmelerinin en olası sebebi).
- İşlemci ve bellek kullanımı göstergesi.
- Sunucu dünyalarını yeniden adlandırma, kopyalama, silme, klasörünü açma.
- Türkçe ve İngilizce arayüz, koyu tema, ilk açılış rehberi.
- Komut satırı aracı (`CoopKeep.Cli`): `doctor`, `saves`, `new`, `use`, `run`.

### Bilinen sınırlar / Known limitations

Bunlar eksiklik değil, modun kendi sınırları — araştırmayla doğrulandı:

- **Oyuncu limiti yok.** Bannerlord Coop bir slot sınırı uygulamıyor ve
  dışarıdan bağlantı reddedilemiyor.
- **Ban yalnızca isim bazlı olabilir.** Steam kimliği canlı protokole
  yazılmıyor; bu sürümde ban henüz yok, yalnızca `kick` var.
- **Öldürme işlemi yok.** Sunucu vanilla hile komutlarını reddediyor
  (`Cheat mode is disabled!`) ve canı sıfırlamanın sonucu doğrulanamadı.
- **"Sadece arkadaşlar" görünürlüğü** dedicated server'da doğrulanamadı;
  yalnızca "Steam'de görünür / görünmez" sunuluyor.
- **Bellek sınırı ayarlanamıyor.** Sunucunun böyle bir seçeneği yok; yalnızca
  kullanım gösteriliyor.
- Yönetici işlemleri, bir dünyada birden fazla kayıtlı oyuncu varsa hedefi
  güvenle çözemediği için uygulanmıyor (sebebi arayüzde belirtiliyor).
- Yalnızca Steam kurulumları tespit ediliyor; Epic/GOG desteği yok.
- Aynı anda tek sunucu yönetilebiliyor.
