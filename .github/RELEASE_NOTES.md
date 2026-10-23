**[English](#english)** · **[Türkçe](#türkçe)**

A launcher and server manager for the **Bannerlord Coop** mod: set up, host, invite your
friends and play — modded too — from a single app.

*Bannerlord Coop modu için başlatıcı ve sunucu yöneticisi: kur, sunucu aç, arkadaşlarını çağır
ve oyna — modlu da — tek uygulamadan.*

> ⬇ **Download `CoopKeep.exe` from the Assets list at the bottom of this page.**
> The *Source code* archives contain the code, not the program.
>
> ⬇ ***`CoopKeep.exe`'yi bu sayfanın en altındaki Assets listesinden indir.***
> *Source code arşivleri kod içerir, program değil.*

---

## English

### Install
1. **Mount & Blade II: Bannerlord** v1.4.8 (Steam or Epic)
2. Subscribe to [**Bannerlord Coop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698) on the Steam Workshop
3. Download **`CoopKeep.exe`** below and double-click it — no installer, no .NET needed
4. Windows may say *"Windows protected your PC"* the first time (the exe is not signed):
   click **More info → Run anyway**

Your friends do not need CoopKeep; they subscribe to the same mods and join from the game.
With CoopKeep they can join in one click from an invite file.

### What's new since v0.5.0

**Guided from setup to playing**
- **Setup check** on Home (game version, Coop and its server package, DLCs Coop rejects,
  blocked files, firewall) — each problem with a one-click fix
- **Journey bar** Setup → World → Server → Friends, and a **How to use** page
- **Pre-start check:** missing save, missing world mods (with **Get missing mods**), Steam not
  running, broken Coop environment variables, Documents on OneDrive
- **Exit codes explained** (load failure, crash, Coop module changed, license) with what to do

**Hosting and joining**
- **Start and play** opens the game first while the server gets ready, then tells you and copies
  the address
- **Invite file:** one zip with the world's mods and how to join; your friend clicks
  **Apply pack and join**
- **Open port automatically** (UPnP) when your router supports it
- Coop update notice, so everyone stays on the same Coop version

**Playing Coop with mods**
- Per-world mod list: the server starts with the world's mods (Coop's own files are not touched)
- Mods page shows **your game** vs. **the server**, with an "Add to server" button per mod
- Mods a mod needs (Harmony, ButterLib, MCM…) come along automatically
- **Correct load order:** libraries load before the game's modules, as they ask; no more
  "ButterLib is loaded after Native" warning
- **Mod packs:** create, import, apply to your game and to server worlds, or
  **Play with this pack** in one click
- Detects Workshop mods downloaded by **another Steam account** (the game can't see them) and
  mods Steam thinks are installed but were deleted — with the fix for each
- Find mods on the Steam Workshop and Nexus Mods; compact view, refresh, filters

**Comfort**
- System tray icon, player-joined alert, notification history, **Update now** inside the app
- "Cheats" is now **Admin tools**; the optional server add-on is marked experimental
- Less memory, lighter while hosting, and unexpected errors no longer close the app

Full list: [CHANGELOG.md](https://github.com/Aderimo/bannerlord-coopkeep/blob/main/CHANGELOG.md)

### Known limitations
- Coop itself warns that it may be unstable with extra mods. Everyone must use exactly the same
  mods; mods that change the campaign (e.g. Diplomacy) can cause desyncs — add them one by one
- Automatic port opening needs UPnP on your router; joining through Steam needs no port
- One server at a time

---

## Türkçe

### Kurulum
1. **Mount & Blade II: Bannerlord** v1.4.8 (Steam ya da Epic)
2. Steam Atölyesi'nde [**Bannerlord Coop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698)'a abone ol
3. Aşağıdan **`CoopKeep.exe`**'yi indirip çift tıkla. Kurulum ya da .NET gerekmez.
4. İlk açılışta Windows *"Windows kişisel bilgisayarınızı korudu"* diyebilir (exe imzasız):
   **Ek bilgi → Yine de çalıştır**

Arkadaşlarının CoopKeep'e ihtiyacı yok; aynı modlara abone olup oyundan katılırlar.
CoopKeep'leri varsa davet dosyasıyla tek tıkla katılabilirler.

### v0.5.0'dan bu yana yenilikler

**Kurulumdan oyuna yol gösterme**
- Ana sayfada **kurulum denetimi** (oyun sürümü, Coop ve sunucu paketi, Coop'un reddettiği
  DLC'ler, engellenmiş dosyalar, güvenlik duvarı); her sorunda tek tıkla düzeltme
- **Yolculuk çizgisi:** Kurulum → Dünya → Sunucu → Arkadaşlar; ayrıca **Nasıl kullanılır** sayfası
- **Başlatmadan önce denetim:** kayıt yok, eksik dünya modları (**Eksikleri indir** düğmesiyle),
  Steam kapalı, hatalı Coop ortam değişkenleri, OneDrive'daki Belgeler
- **Çıkış kodları açıklanır** (yükleme hatası, çökme, Coop modülü değişmiş, lisans) ve ne
  yapılacağı söylenir

**Sunucu açma ve katılma**
- **Başlat ve oyna:** önce oyun açılır, sunucu arkada hazırlanır; hazır olunca haber verir ve
  adresi panoya kopyalar
- **Davet dosyası:** dünyanın modları ve katılma bilgileri tek zip'te; arkadaşın
  **Paketi uygula ve katıl**'a basar
- Modem destekliyorsa **portu otomatik aç** (UPnP)
- Coop güncellenince uyarı: herkes aynı Coop sürümünde kalır

**Coop'u modlu oynamak**
- Dünyaya özel mod listesi: sunucu dünyanın modlarıyla açılır (Coop'un kendi dosyalarına
  dokunulmaz)
- Modlar sayfası **senin oyununu** ve **sunucuyu** ayrı gösterir; her modda "Sunucuya ekle"
- Bir modun ihtiyaç duyduğu modlar (Harmony, ButterLib, MCM…) kendiliğinden gelir
- **Doğru yükleme sırası:** kütüphaneler istedikleri gibi oyunun modüllerinden önce yüklenir;
  "ButterLib Native'den sonra yüklendi" uyarısı bitti
- **Mod paketleri:** oluştur, içe aktar, oyununa ve sunucu dünyalarına uygula ya da tek tıkla
  **Oyunu bu paketle aç**
- **Başka bir Steam hesabının** indirdiği modları (oyun görmez) ve Steam'in kurulu sanıp
  aslında silinmiş modları tespit eder; her biri için çözümü söyler
- Steam Atölyesi ve Nexus Mods'ta mod bulma; kompakt görünüm, yenile, süzgeçler

**Rahatlık**
- Sistem tepsisi simgesi, oyuncu katılınca uyarı, bildirim geçmişi, program içinden **Şimdi güncelle**
- "Hileler" artık **Yönetici araçları**; isteğe bağlı sunucu eklentisi deneysel olarak işaretli
- Daha az bellek, sunucu açıkken daha hafif; beklenmeyen hata artık programı kapatmıyor

Tüm liste: [CHANGELOG.md](https://github.com/Aderimo/bannerlord-coopkeep/blob/main/CHANGELOG.md)

### Bilinen sınırlar
- Coop'un kendisi ek modlarla kararsız olabileceğini söylüyor. Herkes birebir aynı modları
  kullanmalı; kampanyayı değiştiren modlar (ör. Diplomacy) senkron kopmasına yol açabilir —
  modları birer birer ekle
- Otomatik port açma modemde UPnP ister; Steam ile katılmada port gerekmez
- Aynı anda tek sunucu

---

CoopKeep is an independent tool, not affiliated with the Bannerlord Coop team or TaleWorlds.
*CoopKeep bağımsız bir araçtır; Bannerlord Coop ekibi ya da TaleWorlds ile bağı yoktur.*

Made by **[aderimo](https://gitgit.me/aderimo)** · *Geliştiren* **[aderimo](https://gitgit.me/aderimo)**
