# 05 — Penceresiz sunucu

İstek (2026-10-22): dünya programda kurulur ve ayarlanır, "Başlat" deyince sunucu **oyun
penceresi açmadan** çalışır. Oyuncular oyunu normal açar, sunucuyu adıyla arar ya da IP ile girer.

## Motor

TaleWorlds'ün ücretsiz sunucu motoru (çok oyunculu özel sunucular için yaptığı, pencere ve çizim
olmayan .NET sürümü). Başlatma:

```
engine\dotnet\dotnet.exe TaleWorlds.Starter.DotNetCore.dll
    _MODULES_*Native*SandBoxCore*Sandbox*CoopKeep*_MODULES_
    /dedicatedcustomserver <port> <bölge> 0 no_watchdog
çalışma klasörü: engine\bin\Win64_Shipping_Server
ortam: BANNERLORD_USER_DIR=<veri klasörü>, DOTNET_ROOT=engine\dotnet
```

- Modüllerin DLL'leri `Modules\<id>\bin\Win64_Shipping_Server` altından yüklenir. Oyunun
  `SandBox.dll` ve `TaleWorlds.CampaignSystem.dll`'i .NET Standard 2.0: istemci derlemeleri
  bu motorda da yüklenir. Bizim mod da aynı (netstandard2.0).
- Kayıtlar `<veri klasörü>\Game Saves` altına gider. Bu klasör oyunun kendi `Game Saves`
  klasörüne bağlanır (dizin bağlantısı): program, oyun ve sunucu aynı dünyaları görür.
- 2026-10-22 denemesi: mod motorda yüklendi (`4 modules`), kayıt yüklenmeye başladı ve
  karakter verileri okunurken motor yönetilen bir hata bırakmadan kapandı (aşağıda 1. madde).

## Ekranlı oyunun kendiliğinden yaptığı, burada elle yapılması gerekenler

1. **Yüz üretici:** motor `FaceGen`'i başlatmıyor. Karakterlerin görünüm verisi okunurken
   yerel kod düşüyor. Yüklemeden önce yarış adları boşsa `FaceGen.CreateInstance()`.
2. **Oyun durumu yöneticisi:** `GameStateManager.Current` boş; modülün genel yöneticisi atanır.
3. **Paralel iş sürücüsü:** `TWParallel` varsayılan sürücüyle kurulur.
4. **Kayıt tanımları:** `SaveManager.InitializeGlobalDefinitionContext()`.
5. **Oyun sürümü:** parametre dosyası yok, `MBSaveLoad.CurrentVersion` boş (i-1.-1.-1). Boş
   sürümle yüklenen kayıt eski kayıt sanılır ve gereksiz dönüştürme kodu çalışır; kaydettiğimiz
   dünya da oyuncuda eski sanılır. Native modülünün sürümü yazılır.
6. **Hata ayıklama yöneticisi:** motorda bir "assert" süreci düşürebilir. Kendi
   `IDebugManager`'ımız uyarıları günlüğe yazar, süreci durdurmaz.
7. **Harita sahnesi:** kampanya yol bulma ve konum sorguları için harita sahnesi ister; ekranlı
   oyunun sahnesi çizim parçaları da kurar. Kendi `MapScene` türevimiz yalnızca haritanın yol
   ağını okur (`Main_map`), sınırları ve arazi boyutunu kurar; yükseklik, hava, kar ve atmosfer
   sorgularına sabit/boş yanıt verir. Kampanya yüklenirken `Campaign.MapSceneCreator` bizimkiyle
   değiştirilir.
8. **Yerleşim mesafe önbelleği:** `SandBox\ModuleData\DistanceCaches\settlements_distance_cache_Default.bin`
   okunur ve harita mesafe modeline kaydedilir; dosya yoksa hesaplanır.
9. **Görsel oluşturucular ve sancaklar:** savaş görseli oluşturucu boş bir türle değiştirilir;
   sancak yöneticisi ve renkleri yüklenir; hava durumu davranışının dinleyicileri kaldırılır.
10. **Yapay zekâ adımı:** harita ekranının her karede yaptığı `Campaign.LateAITick()` her karede
    çağrılır; `CampaignLateAITickTask` de buna bağlanır.
11. **Pencereler:** sunucuda bir soru penceresi açılırsa kimse tıklamaz; ilk seçenek seçilmiş
    sayılır. Oyunun kendi otomatik kaydı kapatılır (kaydı biz alırız).

## Dağıtım (kullanıcıda)

- Motor: kullanıcının Steam kütüphanesindeki ücretsiz **Mount & Blade II: Dedicated Server**
  aracı (Araçlar, uygulama 1863440). CoopKeep motoru kopyalamaz, dağıtmaz. Araç kurulu değilse
  program Steam'in kurma penceresini açar (Ayarlar > Kurulum ve ana sayfa). Başka bir klasördeyse
  elle seçilebilir.
- Program her açılışta motoru hazırlar (`HeadlessServer.Prepare`):
  - `SandBoxCore` ve `SandBox` modülleri oyunun kurulumundan alınır. `ModuleData` klasörleri ve
    harita sahnesi (`SandBox\SceneObj\Main_map`, ~2 GB) kopyalanmaz, dizin bağlantısıyla
    gösterilir; oyun güncellenince kendiliğinden güncel kalır.
  - SandBox'ın tanımından ekran alt modülleri (görünüm, arayüz) çıkarılır; yalnızca `SandBox.dll`
    sunucu klasörüne kopyalanır.
  - CoopKeep modu oyundaki (ya da Atölye'deki) kopyasından kopyalanır.
  - Sunucu verisi `Documents\...\CoopKeep\server-engine` altında; `Game Saves` oyunun kayıt
    klasörüne bağlıdır.
- Ek varlık paketi (`DsAssetPackages`) gerekmez. 2026-10-22'de paketsiz denendi: dünya 10 saniyede
  açıldı, oyuncu katıldı.
- Yalnızca oyunun kendi modülleri ve CoopKeep ile çalışır. Dünyada başka mod varsa program
  sunucuyu küçültülmüş bir oyun penceresinde açar ve nedenini konsola yazar.
- Dünya oluşturma da önce penceresiz denenir. Başarısız olursa program küçültülmüş oyun
  penceresiyle yeniden dener.

## Oyunda bulunan ve düzeltilen sorunlar (2026-10-22)

- Motordaki resmî modüller sürümü derleme numarasıyla bildiriyor (`v1.4.8.118999`); oyuncu
  "modların farklı" diye reddediliyordu. Resmî modüllerde derleme numarası karşılaştırılmıyor.
- Zaman akmıyordu. Oyun, zaman kilidi açıkken kip değişikliğini yok sayıyor; ayrıca
  "durdurulabilir" kipte sunucunun kendi (hep bekleyen) partisi zamanı durduruyor. Kilit açılıp
  kip değiştiriliyor; sunucu "durdurulamaz oynat" kullanıyor.
- Oyunun lordlar için yaptığı alışveriş (asker toplama, yiyecek, ganimet/esir satışı, günlük
  asker yükseltme) oyuncunun partisine de uygulanıyordu. `PlayerPartyShield` ve
  `CoopKeepTroopUpgradeModel` bunu engelliyor.
