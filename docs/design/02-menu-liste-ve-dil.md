# 02 — Menü, sunucu listesi ve dil

## Ana menü

- **Seçenekler:** "Co-op Sunucusu Aç" ve "Co-op'a Katıl", oyunun kendi ana menüsüne eklenir (`InitialStateOption`).
- **Pencereler:** oyunun kendi pencereleri kullanılır (`TextInquiry`, `MultiSelectionInquiry`). Ayrı bir arayüz çizilmediği için oyunun görünümüyle uyumlu ve bakımı kolay.
- **Sunucu açma:** kayıt seç → sunucu adı → şifre → kayıt yüklenir. Sunucu, kampanya haritaya gelince açılır; o ana kadar bekleyen bir istek olarak durur.
- **Oturum açıkken:** aynı düğmeler durum penceresini açar ("Sunucuyu kapat" / "Sunucudan ayrıl"). Önceden yalnızca "zaten açık" yazıyordu ve oyuncu ekran açılmıyor sandı.

## Sunucu listesi

Oyuncular sunucuyu Steam adıyla değil, sunucu sahibinin verdiği adla arar.

- **Sorgu:** bağlantı açılmadan tek bir UDP paketi gider (`CPKQ`), sunucu tek paketle yanıt verir (`CPKI`). Yanıtta ad, oyuncu sayısı, şifre durumu, oyun ve mod sürümü, dünya adı ve rastgele bir sunucu kimliği var.
- **Kaynaklar:**
  - aynı ağdaki sunucular, 7400–7404 portlarına yayın ile;
  - daha önce katılınan adresler, doğrudan sorgu ile.
- **Tek satır:** aynı sunucu IPv4 ve IPv6 adreslerinden iki kez yanıt verebiliyor (oyunda görüldü). Liste sunucu kimliğine göre tek satıra indirir ve IPv4'ü tercih eder. Yerel bağlantı (`fe80::`) adresleri başka bilgisayardan çalışmaz.
- **Arama:** oyunun penceresindeki arama kutusu kullanılır. `ServerBrowser.Search` aynı aramayı koddan da yapar; büyük/küçük harf ve Türkçe İ/ı duyarsız.
- **Uyumsuz sürüm:** bu sunucular listede soluk görünür, nedeni ipucunda yazar.

### Sonraki adım: internet listesi

- Herkese açık internet sunucularını listelemek için küçük bir ana sunucu (master server) gerekecek. Sunucular kendini oraya kaydeder, liste oradan çekilir.
- Bu aynı zamanda port açamayanlar için NAT delme aracısı olabilir.
- Barındırmanın bir maliyeti olacak, bu yüzden karar sunucu sahibine (sana) ait.

## Dil

- **Yöntem:** oyunun kendi yerelleştirmesi kullanılır (`{=ck_kimlik}English`). Mod, oyunun dilinde konuşur; ayrı bir dil ayarı yok.
- **Ağ katmanı (`CoopKeep.Net`) dilden bağımsızdır:** yalnızca neden kodları ve değerler taşır. Oyuncuya gösterilen metin modda üretilir (`RejectText`).
- **Denetim:** `mod/tools/check-strings.py`, koddaki her metnin her dil dosyasında olduğunu ve yer tutucularının (`{NAME}`) aynı olduğunu denetler.
- **Görülen tuzak:** dil dosyasına XML açıklaması yazılınca oyun dosyanın tamamını yok sayıyor. Dosya "açıldı" diye günlüğe yazılıyor ama metinler gelmiyor. Betik bunu da yakalar.

## Test için profil

`/coopkeep-profile <ad>` ayrı bir kimlik ve ayar klasörü kullanır. Aynı bilgisayarda iki oyun açıp biri sunucu biri oyuncu olarak denendi: iki kopya birlikte çalışıyor, her biri yaklaşık 2,7 GB bellek kullanıyor.
