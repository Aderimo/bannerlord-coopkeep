**[English](#english)** · **[Türkçe](#türkçe)**

A fix for libraries that are on the Steam Workshop twice.

*Steam Atölyesi'nde iki kez bulunan kütüphaneler için bir düzeltme.*

> ⬇ **Download `CoopKeep.exe` from the Assets list at the bottom of this page.**
> Already on v1.0.0 or v1.0.1? Use **Settings → Update now** inside CoopKeep.
>
> ⬇ ***`CoopKeep.exe`'yi bu sayfanın en altındaki Assets listesinden indir.***
> *v1.0.0 ya da v1.0.1 kullanıyorsan CoopKeep içinde **Ayarlar → Şimdi güncelle**.*

---

## English

### Fixed
- **Harmony, ButterLib, UIExtenderEx and Mod Configuration Menu v5 are on the Workshop twice:**
  the official pages and a second set with the same mod ids but other versions. Subscribed to
  both, the game can't tell which to load, so your version may not match your friend's and Coop
  refuses the connection.
  - The Mods page now marks these mods and shows both versions with their pages; the official
    one is marked. Unsubscribe from one.
  - CoopKeep uses the copy your Steam account actually sees.
- A mod you download by hand (zip) installs again even if the Workshop has another version of it.
  In v1.0.1 it was skipped, so **Refresh** never showed it.
- In a pack, a mod installed from a different Workshop page says so and offers the pack's page.

**If a friend can't join with a modded world:** everyone keeps the same set of library pages
(the official ones are recommended), unsubscribes from the other, and the host sends the pack again.

Full list: [CHANGELOG.md](https://github.com/Aderimo/bannerlord-coopkeep/blob/main/CHANGELOG.md)

---

## Türkçe

### Düzeltilenler
- **Harmony, ButterLib, UIExtenderEx ve Mod Configuration Menu v5 Atölye'de iki kez var:**
  resmî sayfalar ve aynı mod kimlikli ama sürümü farklı ikinci bir set. İkisine de aboneysen oyun
  hangisini açacağını bilemiyor; sürümün arkadaşınınkiyle tutmayabiliyor ve Coop bağlantıyı
  reddediyor.
  - Modlar sayfası bu modları işaretliyor, iki sürümü sayfalarıyla gösteriyor; resmî olan
    işaretli. Birinin aboneliğinden çık.
  - CoopKeep, Steam hesabının gerçekten gördüğü kopyayı kullanıyor.
- Elle (zip) indirdiğin mod, Atölye'de başka sürümü olsa da yeniden kuruluyor. v1.0.1 bunu
  atlıyordu, bu yüzden **Yenile** onu hiç göstermiyordu.
- Pakette, başka bir Atölye sayfasından kurulu mod bunu söylüyor ve paketteki sayfayı öneriyor.

**Arkadaşın modlu dünyaya katılamıyorsa:** herkes aynı kütüphane sayfalarını tutsun (resmî
olanlar önerilir), diğerlerinin aboneliğinden çıksın ve sunucuyu açan paketi yeniden göndersin.

Tüm liste: [CHANGELOG.md](https://github.com/Aderimo/bannerlord-coopkeep/blob/main/CHANGELOG.md)

---

CoopKeep is an independent tool, not affiliated with the Bannerlord Coop team or TaleWorlds.
*CoopKeep bağımsız bir araçtır; Bannerlord Coop ekibi ya da TaleWorlds ile bağı yoktur.*

Made by **[aderimo](https://gitgit.me/aderimo)** · *Geliştiren* **[aderimo](https://gitgit.me/aderimo)**
