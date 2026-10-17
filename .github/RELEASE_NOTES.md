**[English](#english)** · **[Türkçe](#türkçe)**

Manage your Bannerlord Coop server from a single app — create the world, start the
server, manage players, repair what cheaters broke and watch Calradia live.

*Bannerlord Coop sunucunu tek uygulamadan yönet: dünyayı kur, sunucuyu başlat, oyuncuları
yönet, hilecilerin bozduğunu düzelt, Calradia'yı canlı izle.*

> ⬇ **Download `CoopKeep.exe` from the Assets list at the bottom of this page.**
> The *Source code* archives contain the code, not the program.
>
> ⬇ ***`CoopKeep.exe`'yi bu sayfanın en altındaki Assets listesinden indir.***
> *Source code arşivleri kod içerir, program değil.*

---

## English

### Install
1. **Mount & Blade II: Bannerlord** v1.4.8 on Steam
2. Subscribe to [**Bannerlord Coop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698) on the Steam Workshop
3. Download **`CoopKeep.exe`** below and double-click it — no installer, no .NET needed

Your friends do not need CoopKeep; they subscribe to the same mod and join from the game.

### What's new since v0.1.0

**Added**
- **New interface** in the style of Bannerlord, with a page per task, English and Turkish
- **New world wizard** like starting a campaign in the game, per-world settings, and
  importing a single-player save. New worlds are created **instantly**
- **Cheats tab** to repair what a cheater broke — gold, health, clan influence and renown,
  age, **troops by type and count** with real portraits, **every item in the game** with
  town prices, and **skills, focus and attribute points** on a skills screen like the
  game's. Recent actions can be undone; everything goes to an audit log
- **Player profile:** skills, equipment with real item images, inventory and army
- **Steam bans** through the Coop server's own ban list, no restart needed
- **Live map** of Calradia with players, **all lords**, kingdoms, capture alerts,
  **teleport from the map**, and a **viewer mode** so friends can watch your map
- **Encyclopedia** with the game's own texts (in Turkish from the game's translation) and
  guides: strongest troops, richest towns, strongest clans, best castle locations, best
  weapons, armor and horses
- **Mods page:** status of every launcher module, one-click "Set up for Coop", a list of all
  known mods with community results, and a mod info window
- Automatic backups after every save, restart with a 60-second warning, crash restart,
  bug report zip, server priority and memory warning
- Optional **CoopKeep server plugin** (off by default) for items, skills, the profile and
  the fast map

**Changed**
- The server starts **once** when you create a world (it used to boot twice)
- Nothing heavy is sent to the server the moment it goes live
- The map keeps markers readable at every zoom level and moves players smoothly

**Removed**
- The separate *block list* — players banned by name now appear in the Bans list
- The *Teleport* tab in Cheats — teleporting is done from the map
- The old admin panel on the Players page — moved into the Cheats tab

**Fixed**
- Giving gold did nothing (Coop removed the old command)
- Admin actions were refused with more than one player
- "Take troops" was empty for players; skills, focus and attribute points did not work

Full list: [CHANGELOG.md](https://github.com/Aderimo/bannerlord-coopkeep/blob/main/CHANGELOG.md)

### Known limitations
- Renown cannot be taken away; there is no god mode
- Skill XP follows the game's learning rate — use the level buttons for a visible change
- Steam installations only; one server at a time

---

## Türkçe

### Kurulum
1. Steam'de **Mount & Blade II: Bannerlord** v1.4.8
2. Steam Atölyesi'nde [**Bannerlord Coop**](https://steamcommunity.com/sharedfiles/filedetails/?id=3770450698)'a abone ol
3. Aşağıdan **`CoopKeep.exe`**'yi indirip çift tıkla. Kurulum ya da .NET gerekmez.

Arkadaşlarının CoopKeep'e ihtiyacı yok; aynı moda abone olup oyundan katılırlar.

### v0.1.0'dan bu yana yenilikler

**Eklenenler**
- Bannerlord tarzında, her iş için ayrı sayfası olan **yeni arayüz**; Türkçe ve İngilizce
- Oyunda kampanya açar gibi **yeni dünya sihirbazı**, her dünyaya özel ayarlar, tek
  oyunculu save içe aktarma. Yeni dünya **anında** kurulur
- Hilecinin bozduğunu düzeltmek için **Hileler sekmesi**:
  - altın, can, klan nüfuzu ve ünü, yaş
  - gerçek portreleriyle **tür ve sayı seçerek asker**
  - kasaba fiyatlarıyla **oyundaki bütün eşyalar**
  - oyundaki gibi beceri ekranında **beceri, odak ve özellik puanları**
  - son işlemler geri alınabilir; her şey denetim kaydına yazılır
- **Oyuncu profili:** beceriler, gerçek eşya görselleriyle kuşanım, envanter ve ordu
- Coop sunucusunun kendi ban listesiyle **Steam banı**; yeniden başlatmak gerekmez
- **Canlı Calradia haritası:**
  - oyuncular, **bütün lordlar**, krallıklar ve el değiştirme uyarıları
  - **haritadan ışınlama**
  - arkadaşların haritanı izleyebilsin diye **izleyici modu**
- **Ansiklopedi:**
  - metinler oyunun kendi ansiklopedisinden, oyunun Türkçe çevirisiyle
  - rehberler: en güçlü askerler, en zengin kasabalar, en güçlü klanlar, en iyi kale
    konumları, en iyi silah, zırh ve atlar
- **Modlar sayfası:** her modun durumu, tek tıkla "Coop için düzenle", topluluk
  sonuçlarıyla bilinen bütün modlar ve mod bilgi penceresi
- Her kayıttan sonra otomatik yedek, 60 saniye uyarılı yeniden başlatma, çökünce yeniden
  başlatma, hata raporu zip'i, sunucu önceliği ve bellek uyarısı
- Eşyalar, beceriler, profil ve hızlı harita için isteğe bağlı **CoopKeep sunucu
  eklentisi** (varsayılan olarak kapalı)

**Değişenler**
- Dünya kurarken sunucu artık **bir kez** açılıyor; önceden iki kez açılıyordu
- Sunucu yayına geçer geçmez ona ağır iş gönderilmiyor
- Harita her yakınlıkta okunaklı kalıyor; oyuncular akıcı hareket ediyor

**Kaldırılanlar**
- Ayrı *engel listesi*: ad ile banlananlar artık Banlılar listesinde görünüyor
- Hileler'deki *Işınla* sekmesi: ışınlama haritadan yapılıyor
- Oyuncular sayfasındaki eski yönetici paneli: Hileler sekmesine taşındı

**Düzeltilenler**
- Altın verme bir şey yapmıyordu (Coop eski komutu kaldırmıştı)
- Birden fazla oyuncu varken yönetici işlemleri reddediliyordu
- Oyuncularda "Asker al" listesi boştu; beceri, odak ve özellik puanları çalışmıyordu

Tüm liste: [CHANGELOG.md](https://github.com/Aderimo/bannerlord-coopkeep/blob/main/CHANGELOG.md)

### Bilinen sınırlar
- Ün azaltılamıyor; tanrı modu yok
- Yetenek XP'si oyunun öğrenme hızıyla işler; görünür değişiklik için seviye düğmelerini kullan
- Yalnızca Steam kurulumları; aynı anda tek sunucu

---

CoopKeep is an independent tool, not affiliated with the Bannerlord Coop team or TaleWorlds.
*CoopKeep bağımsız bir araçtır; Bannerlord Coop ekibi ya da TaleWorlds ile bağı yoktur.*
