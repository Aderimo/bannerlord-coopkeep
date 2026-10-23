# 03 — Kampanya senkronu ve kaynak koruması

## Kim neyi işletir

- **Barındıranın oyunu dünyayı işletir.** Kampanya, zaman, yapay zekâ, savaşlar orada çalışır.
- **Katılan oyuncu kopyayla oynar.** Barındıranın dünyasını indirir, kendi oyununda yükler; o kopyada zaman durdurulur ve kilitlenir.
  - Partilerin yeri sunucudan gelir, saniyede 10 kez ve oyuncunun görüş alanı için (45 harita birimi). Yumuşak geçişle uygulanır; çok uzaktaysa doğrudan yerine konur.
  - Oyuncunun tıkladığı hedef **niyet** olarak sunucuya gider: nokta, yerleşim, parti, dur, yerleşimden çık. Partiyi sunucu yürütür.
  - Kopyada yerel karşılaşma (savaş, yerleşim menüsü) açılırsa hemen kapatılır. Savaşlar sunucuda otomatik çözülür, sonuç oyuncuya bildirilir.
  - Bağlantı koparsa kopya dünyada devam edilmez: uyarı çıkar, oyuncu ana menüye döner.

## Oyuncu karakteri

- **Mesaj sırası:**
  1. Oyuncu bağlanır.
  2. Barındıran haritadayken `CharacterSetup` gider: karakterin var mı, seçilebilecek kültürler.
  3. Oyuncu ad, kültür, cinsiyet ve klan adı seçer (`CharacterRequest`; klan adı için hazır bir öneri gelir).
  4. Barındıran kahramanı ve partiyi kurar, ardından `YouAre` ve dünyayı gönderir.
- **Kahraman:** seçilen kültürün lord şablonundan üretilir ve **kendi klanının lideri** olur; kendi partisiyle barındıranın yanında başlar (10 temel asker, 1000 altın, 10 tahıl).
- **Kendi klanı:** savaş, barış ve evlilik ittifakı oyunda klanlar arasında yapıldığı için her oyuncunun klanı ayrı (ayrıntı: [04](04-durum-ticaret-diplomasi.md)). Önceki sürümde barındıranın klanına katılmış karakterler girişte kendi klanlarına taşınır.
- **Eşlemenin kaydı:** oyuncu kimliği ile kahraman arasındaki eşleme kampanya davranışının kayıt verisinde durur (`coopkeep_players`). Oyuncu döndüğünde kendi kahramanını bulur.
- **Partinin yapay zekâsı:** kendi karar vermez (`SetDoNotMakeNewDecisions`); yalnızca oyuncunun emirlerini uygular.

## Kaynak koruması

**İstek:** biri kalede, savaşta ya da menüde beklerken (ya da oyunun başından kalkmışken) diğeri haritada gezerse, bekleyenin parası maaşa, yemeği tüketime gitmesin, askerleri firar etmesin.

**Kural:** korunan oyuncunun parası, yemeği ya da askerleri **zaman akarken** azalırsa hemen geri konur. Zaman dururken olan azalma (kendi alışverişi) kabul edilir, artışlar kalır.

**Reddedilen yaklaşım:** "haritadan çıkınca kaydet, eksileni geri koy" sınırsız para açığıydı. Şehirde harcanan para da geri gelirdi.

**Kim korunur:**
- haritada olmayan bağlı oyuncular (yerleşimde, savaşta, menüde),
- **bağlı olmayan** oyuncular (sunucu kapalıyken bile),
- barındıranın kendisi haritadan çıkınca.

**Savaş istisnası:** savaştaki partinin asker kayıpları gerçektir, korunmaz. Para ve yemek korunur.

**Uygulama:** her karede karşılaştırma yapılır. Oyunun iç hesaplarına (Harmony yaması) dokunulmadığı için başka modlarla çakışma riski düşük.

## Zaman kuralları

- **Kimse haritada değilse zaman durur.** Biri haritaya dönünce kaldığı hızla devam eder. Barındıranın kendi durdurması buna karışmaz.
- **Barındıran savaştayken kampanya zamanı herkes için durur.** Bu, oyuncunun kendi oyununda sunucu barındırmanın doğal sonucu; oyuncusuz sunucu (Faz 4) bunu kaldıracak.

## Bilinen sınırlar (sonraki adımlar)

- Oyuncu katıldıktan sonra barındıranda doğan partiler (yeni haydutlar, kervanlar) oyuncuda görünmez. Bunun için parti doğum ve ölüm senkronu gerekiyor.
- Yerleşimde ticaret ve asker toplama, ortak savaş (her oyuncunun kendi karakterini oynaması) henüz yok.
- Oyuncunun kopyasındaki tarih ilerlemez; doğru tarih F9 panelinde sunucudan gelir.
