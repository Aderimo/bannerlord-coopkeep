# CoopKeep yol haritası

Tek liste: ne istiyoruz, ne bitti, ne kaldı. Bir iş bitince burada işaretlenir.

## İki dal
- **`coop-1.0` (bu dal):** CoopKeep, Steam Atölyesi'ndeki **Coop** modu için başlatıcı ve
  sunucu yöneticisi. İlk herkese açık sürüm budur (GitHub + YouTube videosu).
- **`kendi-mod` dalı / `kendi-mod-v0.2` etiketi:** kendi co-op modumuz ve ona göre yazılmış
  program. Dokunulmadan saklı; "ikinci güncelleme"de ele alınacak. Eksik listesi:
  [KENDI-MOD-NOTLARI.md](KENDI-MOD-NOTLARI.md).

## Değişmeyen kurallar
- **Coop'un koduna dokunulmaz.** Hataları tespit edilir; program insanları bunlardan korur,
  açıklar, kolaylaştırır. Coop'un kaynağı yalnızca okunur (`docs/legal/README.md`).
- Coop programda yalnızca bağımlılık olarak anılır; karşılaştırma yapılmaz. Kişisel e-posta
  hiçbir yerde kullanılmaz.
- Korsana özel özellik eklenmez.
- Her metin Türkçe ve İngilizce. Basit ama özellik silmeden.
- GitHub'a gönderme ve Atölye/video kullanıcı onayıyla, en sonda.

---

## ✅ Coop 1.0 (2026-10-22)
- Program yeniden Coop'un özel sunucusunu yönetiyor (v0.5'in bütün özellikleri: hileler,
  harita, ansiklopedi, profil, yedekler, sunucu eklentisi isteğe bağlı).
- **Kurulum denetimi** (oyun sürümü, Coop, sunucu paketi, DLC, engellenmiş dosyalar, güvenlik
  duvarı) ve **açılış öncesi uyarılar** (kayıt yok, eksik mod, Steam kapalı, ortam
  değişkenleri, OneDrive); her birinde tek düğmeli düzeltme.
- **Çıkış kodları** (2/3/4/6) iki dilde, ne yapılacağıyla.
- **Oyuna gir / Başlat ve oyuna gir / Arkadaşına katıl:** oyun Coop ve doğru modlarla açılır,
  adres panoya kopyalanır.
- **Modlu Coop (deneysel):** dünyaya özel modlar, sunucuya kurulum (Coop'a dokunmadan),
  dünya mod paketi, paket uygularken resmî modüller ve Coop açık kalır.
- **Mod yöneticisi** (Atölye + Nexus + zip, paketler) ve **Nasıl kullanılır** sayfası.
- README / README.tr / CHANGELOG 1.0.0.

## 📋 Kalanlar (1.0 yayınından önce)
1. **Kullanıcının oyun içi testi** (kontrol listesi aşağıda). Bulunan hatalar buraya.
2. README ekran görüntüleri: sunucu açıkken çekilmiş yeni görüntüler (Ana sayfa, Nasıl
   kullanılır, Dünyalar > Modlar, Modlar > Paketler). Şimdikiler v0.5'ten.
3. GitHub: `coop-1.0` → `main`, `v1.0.0` etiketi (Release otomatik derlenir). Kullanıcı onayıyla.
4. YouTube videosu (kullanıcı).

### Oyun içi test kontrol listesi
- [ ] İlk açılış: kurulum denetimi doğru mu (DLC açıkken "Kapat", güvenlik duvarı "İzin ekle").
- [ ] Yeni dünya (modsuz) → Sunucuyu başlat → sözleşme → Yayında → Oyuna gir → Coop menüsünde
      adresi yapıştırıp bağlan.
- [ ] Arkadaş Steam'de senin Steam adınla bulup bağlanıyor mu; IP:4200 ile bağlanıyor mu.
- [ ] Tek oyunculu kampanyayı içe aktarıp sunucuda açmak.
- [ ] Modlu dünya: bir mod seç → başlat (konsolda "sunucuya kurulan modlar") → dünya mod
      paketini oluştur → arkadaşta "Paketi uygula" → bağlanabiliyor mu.
- [ ] Kayıt adını bozup başlat: "kayıt yok" uyarısı çıkıyor mu.
- [ ] Steam kapalıyken başlat: uyarı çıkıyor mu.
- [ ] Hileler (sunucu eklentisi açıkken), harita, oyuncular, yedekten geri yükleme.

## Sonra bakılabilecekler
- Kendi modumuz (ikinci güncelleme): `kendi-mod` dalı ve notları.
- Uygulama içinden güncelleme, Discord bildirimleri.
