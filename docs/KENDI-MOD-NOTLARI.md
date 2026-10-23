# Kendi modumuz (CoopKeep modu): ikinci güncellemeye notlar

Kendi modumuz ve ona göre yazılmış program `kendi-mod` dalında ve `kendi-mod-v0.2` etiketinde
saklı (commit 2037905, 2026-10-22). İlk yayın Steam'deki Coop modu için yapılan başlatıcı
(`coop-1.0` dalı). İkinci güncellemede program kendi modumuza göre ayarlanır; aşağıdakiler
o zaman ele alınır.

## O tarihte çalışanlar (oyunda doğrulandı)
- Penceresiz sunucu (Steam'deki ücretsiz Dedicated Server aracı), ~10 sn açılış, ~600 MB.
- Katılma, dünya aktarımı, zamanın akması, partinin yürümesi.
- Oyunun kendi kasaba menüsü; çıkışın sunucuya işlenmesi.
- Aynı ağda sunucu adıyla arama.

## Yazıldı, oyunda denenmedi
- Kendi savaşını oynamak (iki partinin açık arazi savaşı).
- Yapay zekânın oyuncu parasıyla alışveriş yapmasının ve askerleri yükseltmesinin engellenmesi.
- Kasabada asker toplama ve ticaretin sunucuya işlenmesi.
- Oyunu açmadan dünya oluşturma (penceresiz).
- Resmî Dedicated Server aracıyla açılış (testler eski bir sunucu paketinin motoruyla yapıldı).

## Eksikler

### Kritik
1. Esir düşme, ölüm, esaretten kurtulma, fidye, varisin geçmesi yok. Sunucuda esir düşen oyuncunun
   durumu oyuncuya gitmiyor; iki dünya ayrılıyor.
2. Birlikte savaş yok: iki oyuncu aynı savaşa girerse savaşı sunucu otomatik çözüyor; oyuncuya karşı
   oyuncu da öyle.
3. Kendi savaşın sınırlı: kuşatma, köy yağması, ordular, çok partili savaşlar otomatik çözülüyor.

### Önemli
4. Savaş sonrası ganimet/esir ekranı yalnızca gösteriyor; asıl dağıtım sunucuda (farklı olabilir).
5. Saldırıya uğrayınca "kaçmaya çalış" sunucuda yine otomatik savaşa dönüyor.
6. Ekrandaki fiyat ile sunucu fiyatı farkı; oyuncu ticareti pazar fiyatını etkilemiyor.
7. `coopkeep.unstuck` kurtarma komutu yok.
8. Deniz hareketi / War Sails desteklenmiyor.

### Penceresiz sunucu
9. Başka modlu dünyalar penceresiz açılmıyor (oyun penceresine düşüyor).
10. İnternetten adla arama yok (yalnızca aynı ağ); port açamayanlar için NAT çözümü yok.
11. Motorda oyunun sürüm dosyası yok; kayıtlara sürüm elle yazılıyor (kırılgan).
12. Gece yeniden başlatma/yedek, uzaktan yönetim yok.

### Program (kendi mod dalı)
13. CPU/bellek kartı penceresiz sunucuda büyük olasılıkla boş.
14. Eski Coop artıkları (ServerTools, CLI, eski sunucu yöneticisi) o dalda hâlâ duruyor.
15. Ana sayfa "yaklaşık bir dakikada açılır" diyor (penceresiz ~10 sn).
16. Mod hata tespiti ("şu mod yüzünden"), paketi dünyaya atama, akıcılık ölçümleri yok.

Ayrıntılı tasarım: `docs/design/05-penceresiz-sunucu.md`, eski yol haritası: `docs/YOL-HARITASI.md`.
