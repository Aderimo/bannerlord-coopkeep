# Lisans ve izinler

## Diğer co-op modunun koduna bakma izni

Steam Atölyesi'ndeki diğer co-op modunun lisansı özeldir (açık kaynak değil). Değiştirilmiş kopyalarının dağıtılması yazılı onay gerektirir.

Modun geliştiricisi 2026-10 tarihinde şu izni verdi: kodlarına **bakabiliriz**, ama kodlarını kullanamayız ve birebir aynısını yapamayız.

**Önemli (2026-10-21):** Onların GitHub deposundaki lisans ("BannerlordCoop Source Available License") kodun rakip bir co-op, ağ ya da senkron modu yapmak, geliştirmek veya desteklemek için kullanılmasını **önceden alınmış yazılı izin olmadan** açıkça yasaklıyor. Bu yüzden yazılı izin gelene kadar koduna bakılmadı.

**Yazılı izin (2026-10-22):** Proje sahibi (aderimo), Discord'da modun sahibi olduğunu söyleyen "Levi" adlı kullanıcıdan şu yazılı izni aldı (22 Ekim 2026, 00:58):

> Coop modunun asıl sahibiyim coop modunu kullanmak için sana tüm izinleri yetkili izinleri dahil herşeyi veriyorum

Mesajın ekran görüntüsü: [`izin-2026-10-22-discord.png`](izin-2026-10-22-discord.png). Gönderenin gerçekten modun sahibi olduğunu proje sahibi doğruladı; biz bunu ayrıca doğrulayamıyoruz.

Daha önceki sözlü izinde sahibi şunu da istemişti: birebir aynısı yapılmasın, mod kendi komutları ve kendi geliştirmeleriyle bizim olsun; açık kaynak ve herkese ücretsiz olursa sorun yok. CoopKeep MIT lisanslı, açık kaynak ve ücretsiz.

## Uyduğumuz kurallar

1. Onların kaynağı yalnızca bir sorunu nasıl çözdüklerini anlamak için okunur.
2. Dosya, sınıf ya da fonksiyon kopyalanmaz. Satır satır yeniden yazılmaz, yapıları aynen taşınmaz.
3. Okuduktan sonra varılan tasarım kararı, kendi cümlelerimizle kendi tasarım notlarımıza yazılır. Kod bu nottan yazılır, onların kodundan değil.
4. Kamuya açık metinlerde (Atölye sayfası, README, uygulama) diğer ürün adı karşılaştırma için kullanılmaz.

## Kullandığımız açık kaynak kütüphaneler

| Kütüphane | Lisans | Nerede |
|---|---|---|
| LiteNetLib | MIT | Mod ağ katmanı (`mod/src/CoopKeep.Net`) |

Oyunun kendi kütüphaneleri (TaleWorlds) kurulu oyundan referans alınır ve modla birlikte dağıtılmaz.
