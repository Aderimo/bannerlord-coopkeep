using System;
using System.Collections.Generic;

namespace CoopKeep.App.Localization;

/// <summary>
/// "Sunucuya katıl" sayfasının metinleri ve ana akışın CoopKeep moduna göre yeniden
/// yazılmış metinleri. Bu sözlükler önce aranır; aynı anahtar diğer dosyalardakini ezer.
/// </summary>
public sealed partial class Loc
{
    private static readonly Dictionary<string, string> TurkishCoop = new(StringComparer.Ordinal)
    {

        ["coop.firewall.added"] = "Güvenlik duvarı izni eklendi.",
        ["coop.firewall.failed"] = "İzin eklenemedi (onay verilmedi ya da yetki yok).",




        ["coop.gameRunning"] = "Oyun zaten açık. Önce kapat.",

        ["coop.how.text"] =
            "• Sunucu, onu açan bilgisayarda ayrı bir oyun penceresinde çalışır ve dünyayı işletir; katılanlar dünyayı indirir ve kendi karakterlerini oluşturur.\n" +
            "• Program kurmak istemeyen, oyunun ana menüsündeki \"Co-op Sunucusu Aç\" ile kendi oyununda da sunucu açabilir.\n" +
            "• Zaman herkes için aynı akar. Haritada olmayanın (yerleşimde, savaşta, menüde ya da bağlı değil) parası, yemeği ve askerleri zamanla azalmaz.\n" +
            "• Kimse haritada değilse zaman durur.\n" +
            "• Her oyuncu kendi klanının lideridir; oyuncular birbirine savaş açabilir, barış ve ticaret yapabilir, hediye gönderebilir, evlenebilir.\n" +
            "• Oyunda F7: yerleşim (ticaret, gönüllü), F8: diplomasi, F9: oyuncular ve durum, F10: sohbet.\n" +
            "• Bütün oyuncularda oyun sürümü, CoopKeep sürümü ve modlar aynı olmalı. Uymazsa oyun ne yapılacağını yazar; burada \"Modları düzelt\" ile tek tıkla ayarlanır.",


        // Ana akış artık CoopKeep modunu kullanıyor: eski Coop metinlerinin yerine.
        ["invite.text.head"] = "Bannerlord co-op sunucuma gel!",
        ["settings.reportHint"] = "Günlükler, ayarlar ve mod listesi tek bir zip'e toplanıp masaüstüne kaydedilir. Şifren gizlenir.",
        ["bans.byNameTip"] = "Oyuncu kimliği bilinmiyordu; bu adla bağlanan kişi otomatik atılır.",
        // ---- dünya sihirbazı ve başlatma (aşama 1) ----
        ["wiz.s0"] = "Ad",
        ["wiz.s1"] = "Zorluk",
        ["wiz.s2"] = "Kurallar",
        ["wiz.s3"] = "Modlar",
        ["wiz.s4"] = "Sunucu",
        ["wiz.how"] = "Nasıl başlasın?",
        ["sum.gold"] = "başlangıç {0} altın",
        ["sum.protectOn"] = "kaynak koruması açık",
        ["sum.protectOff"] = "kaynak koruması kapalı",
        ["set.startingGold"] = "Başlangıç altını",
        ["set.startingGold.desc"] = "Katılan oyuncunun yeni karakteri bu kadar altınla başlar.",
        ["set.startingTroops"] = "Başlangıç askeri",
        ["set.startingTroops.desc"] = "Katılan oyuncunun yeni partisi bu kadar askerle başlar.",
        ["set.protectResources"] = "Kaynak koruması",
        ["set.protectResources.desc"] = "Haritada olmayan oyuncunun (yerleşimde, savaşta, menüde ya da çevrimdışı) altını, yemeği ve askerleri zaman geçtikçe azalmaz. Kendi harcadığı yine düşer.",
        ["set.pauseWhenNobodyOnMap"] = "Kimse haritada değilse zaman dursun",
        ["set.pauseWhenNobodyOnMap.desc"] = "Sunucu boşken ya da herkes menüdeyken dünya beklemede kalır; 7/24 açık sunucuda kimse bir şey kaçırmaz.",
        // ---- mod yöneticisi ----
        ["mm.sub"] = "Modlarını aç/kapat, sırala, yenilerini bul ve paket olarak paylaş.",
        ["mm.tab.installed"] = "Yüklü modlar",
        ["mm.tab.browse"] = "Mod bul",
        ["mm.tab.packs"] = "Paketler",
        ["mm.openFolder"] = "Mod klasörü",
        ["mm.installZip"] = "Zip'ten kur",
        ["mm.autoSort"] = "Sırayı düzelt",
        ["mm.apply"] = "Uygula",
        ["mm.applied"] = "Mod listesi oyunun başlatıcısına yazıldı.",
        ["mm.sorted"] = "Modlar bağımlılıklarına göre sıralandı.",
        ["mm.sortConflict"] = "Bazı modlar birbirinden önce yüklenmek istiyor; sıralarını elle kontrol et.",
        ["mm.problem.missing"] = "Gerekli modlar kapalı ya da yüklü değil: {0}",
        ["mm.problem.order"] = "Şunlardan sonra yüklenmeli: {0} (\"Sırayı düzelt\")",
        ["mm.src.workshop"] = "Steam Atölyesi",
        ["mm.src.local"] = "Elle kurulmuş",
        ["mm.up"] = "Yukarı taşı",
        ["mm.down"] = "Aşağı taşı",
        ["mm.info"] = "Bilgi",
        ["mm.remove"] = "Kaldır",
        ["mm.removeConfirm"] = " kaldırılsın mı? Klasörü yedek klasörüne taşınır, istersen geri getirebilirsin.",
        ["mm.removed"] = "{0} kaldırıldı.",
        ["mm.pickZip"] = "Mod arşivi (zip)",
        ["mm.installedFromZip"] = "Kuruldu ve açıldı: {0}. Oyuna yazmak için \"Uygula\".",
        ["mm.installFailed"] = "Kurulamadı: {0}",
        ["mm.browseSearch"] = "Mod adı yaz (boş bırakırsan popülerler)",
        ["mm.searchBtn"] = "Ara",
        ["mm.browseHint"] = "Atölye: abone olunca Steam indirir. Nexus: indirdiğin zip'i \"Zip'ten kur\" ile kur.",
        ["mm.searching"] = "Aranıyor…",
        ["mm.noResults"] = "Sonuç yok.",
        ["mm.searchFailed"] = "Arama yapılamadı: {0}",
        ["mm.meta.workshop"] = "{0} abone · güncelleme {1}",
        ["mm.meta.nexus"] = "{0} · {1} indirme · {2} beğeni",
        ["mm.page"] = "Sayfası",
        ["mm.get"] = "Edin",
        ["mm.waiting"] = "İndiriliyor…",
        ["mm.installed"] = "Kurulu",
        ["mm.subscribe"] = "Abone ol",
        ["mm.subscribeHint"] = "Steam'de modun sayfası açıldı: \"Abone ol\"a bas. İnince CoopKeep listeye ekler.",
        ["mm.workshopReady"] = "{0} indi ve açıldı. Oyuna yazmak için Yüklü modlar'da \"Uygula\".",
        ["mm.nexusManual"] = "Nexus'ta dosyalar sayfası açıldı. İndirdiğin zip'i \"Zip'ten kur\" ile kur.",
        ["mm.nexusNotPremium"] = "Doğrudan indirme Nexus Premium istiyor; dosyalar sayfası açıldı.",
        ["mm.nexusKey"] = "Nexus API anahtarı",
        ["mm.nexusKeyHint"] = "İsteğe bağlı. nexusmods.com > Hesap > API anahtarı. Premium üyelikte modlar programdan doğrudan iner. Anahtar yalnızca bu bilgisayarda saklanır.",
        ["mm.nexusCheck"] = "Denetle",
        ["mm.nexusKeyInvalid"] = "Anahtar geçersiz.",
        ["mm.nexusKeyPremium"] = "Anahtar geçerli, Premium: modlar doğrudan iner.",
        ["mm.nexusKeyFree"] = "Anahtar geçerli ama Premium değil: modlar için sayfa açılır.",
        ["mm.pack.new"] = "Yeni paket",
        ["mm.pack.newHint"] = "Açık modların, şu anki sırayla.",
        ["mm.pack.name"] = "Paket adı",
        ["mm.pack.create"] = "Açık modlardan oluştur",
        ["mm.pack.import"] = "Paketi içe aktar",
        ["mm.pack.list"] = "Paketlerin",
        ["mm.pack.none"] = "Henüz paket yok.",
        ["mm.pack.status"] = "{0} / {1} mod hazır",
        ["mm.pack.apply"] = "Paketi uygula",
        ["mm.pack.applyTip"] = "Paketin modlarını açar, sırasını ayarlar ve pakette gelen modları kurar. Diğer modlar kapanır.",
        ["mm.pack.subscribeAll"] = "Eksik Atölye modlarına abone ol",
        ["mm.pack.export"] = "Arkadaşa gönder (zip)",
        ["mm.pack.exportTitle"] = "Mod paketini kaydet",
        ["mm.pack.importTitle"] = "Mod paketi (zip ya da .ckpack)",
        ["mm.pack.defaultName"] = "Paket {0:dd.MM HH:mm}",
        ["mm.pack.created"] = "\"{0}\" oluşturuldu ({1} mod).",
        ["mm.pack.exported"] = "{0} kaydedildi. İçinde NASIL KURULUR.txt var.",
        ["mm.pack.imported"] = "\"{0}\" içe aktarıldı. \"Paketi uygula\" ile kur.",
        ["mm.pack.appliedMissing"] = "\"{0}\" uygulandı; {1} mod eksik. Listeden abone ol ya da indir.",
        ["mm.state.Ready"] = "hazır",
        ["mm.state.WrongVersion"] = "sürüm farklı",
        ["mm.state.MissingWorkshop"] = "eksik: Atölye'den abone ol",
        ["mm.state.MissingBundled"] = "eksik: paketten kurulacak",
        ["mm.state.MissingManual"] = "eksik: elle indir",
        ["settings.install.app"] = "CoopKeep programı",
    };

    private static readonly Dictionary<string, string> EnglishCoop = new(StringComparer.Ordinal)
    {

        ["coop.firewall.added"] = "Firewall rule added.",
        ["coop.firewall.failed"] = "The rule could not be added (not approved or no permission).",




        ["coop.gameRunning"] = "The game is already running. Close it first.",

        ["coop.how.text"] =
            "• The server runs in a separate game window on the computer that hosts it and runs the world; players who join download the world and create their own characters.\n" +
            "• If you don't want the app, you can also host inside your own game with \"Host Co-op\" on the game's main menu.\n" +
            "• Time runs the same for everyone. Players who are not on the map (in a settlement, battle, menu or offline) don't lose gold, food or troops over time.\n" +
            "• If nobody is on the map, time stops.\n" +
            "• Every player leads their own clan; players can declare war, make peace, trade, send gifts and marry.\n" +
            "• In game, F7: settlement (trade, volunteers), F8: diplomacy, F9: players and status, F10: chat.\n" +
            "• Everyone needs the same game version, CoopKeep version and mods. If they differ, the game says what to change; \"Fix mods\" here sets it up in one click.",


        // The main flow now uses the CoopKeep mod: these replace the old Coop texts.
        ["invite.text.head"] = "Join my Bannerlord co-op server!",
        ["settings.reportHint"] = "Logs, settings and your mod list are packed into a single zip on your desktop. Your password is hidden.",
        ["bans.byNameTip"] = "The player ID was unknown; anyone joining with this name is kicked automatically.",
        // ---- world wizard and start (stage 1) ----
        ["wiz.s0"] = "Name",
        ["wiz.s1"] = "Difficulty",
        ["wiz.s2"] = "Rules",
        ["wiz.s3"] = "Mods",
        ["wiz.s4"] = "Server",
        ["wiz.how"] = "How should it start?",
        ["sum.gold"] = "{0} starting gold",
        ["sum.protectOn"] = "resource protection on",
        ["sum.protectOff"] = "resource protection off",
        ["set.startingGold"] = "Starting gold",
        ["set.startingGold.desc"] = "A joining player's new character starts with this much gold.",
        ["set.startingTroops"] = "Starting troops",
        ["set.startingTroops.desc"] = "A joining player's new party starts with this many troops.",
        ["set.protectResources"] = "Resource protection",
        ["set.protectResources.desc"] = "Players who are not on the map (in a settlement, battle, menu or offline) don't lose gold, food or troops as time passes. What they spend themselves still counts.",
        ["set.pauseWhenNobodyOnMap"] = "Stop time when nobody is on the map",
        ["set.pauseWhenNobodyOnMap.desc"] = "The world waits while the server is empty or everyone is in a menu, so nobody misses anything on a server that runs all day.",
        // ---- mod manager ----
        ["mm.sub"] = "Turn mods on/off, order them, find new ones and share them as packs.",
        ["mm.tab.installed"] = "Installed",
        ["mm.tab.browse"] = "Find mods",
        ["mm.tab.packs"] = "Packs",
        ["mm.openFolder"] = "Mods folder",
        ["mm.installZip"] = "Install from zip",
        ["mm.autoSort"] = "Fix order",
        ["mm.apply"] = "Apply",
        ["mm.applied"] = "The mod list was written to the game launcher.",
        ["mm.sorted"] = "Mods are sorted by their dependencies.",
        ["mm.sortConflict"] = "Some mods want to load before each other; check their order by hand.",
        ["mm.problem.missing"] = "Needed mods are off or not installed: {0}",
        ["mm.problem.order"] = "Must load after: {0} (\"Fix order\")",
        ["mm.src.workshop"] = "Steam Workshop",
        ["mm.src.local"] = "Installed by hand",
        ["mm.up"] = "Move up",
        ["mm.down"] = "Move down",
        ["mm.info"] = "Info",
        ["mm.remove"] = "Remove",
        ["mm.removeConfirm"] = " will be removed. Its folder moves to a backup folder, so you can bring it back.",
        ["mm.removed"] = "{0} removed.",
        ["mm.pickZip"] = "Mod archive (zip)",
        ["mm.installedFromZip"] = "Installed and turned on: {0}. Press \"Apply\" to write it to the game.",
        ["mm.installFailed"] = "Couldn't install: {0}",
        ["mm.browseSearch"] = "Type a mod name (empty shows popular ones)",
        ["mm.searchBtn"] = "Search",
        ["mm.browseHint"] = "Workshop: subscribe and Steam downloads it. Nexus: install the zip you downloaded with \"Install from zip\".",
        ["mm.searching"] = "Searching…",
        ["mm.noResults"] = "No results.",
        ["mm.searchFailed"] = "The search failed: {0}",
        ["mm.meta.workshop"] = "{0} subscribers · updated {1}",
        ["mm.meta.nexus"] = "{0} · {1} downloads · {2} endorsements",
        ["mm.page"] = "Page",
        ["mm.get"] = "Get",
        ["mm.waiting"] = "Downloading…",
        ["mm.installed"] = "Installed",
        ["mm.subscribe"] = "Subscribe",
        ["mm.subscribeHint"] = "The mod's page opened in Steam: press \"Subscribe\". CoopKeep adds it when it arrives.",
        ["mm.workshopReady"] = "{0} arrived and is on. Press \"Apply\" in Installed to write it to the game.",
        ["mm.nexusManual"] = "The files page opened on Nexus. Install the zip you download with \"Install from zip\".",
        ["mm.nexusNotPremium"] = "Direct downloads need Nexus Premium; the files page opened.",
        ["mm.nexusKey"] = "Nexus API key",
        ["mm.nexusKeyHint"] = "Optional. nexusmods.com > Account > API key. With Premium, mods download straight from the app. The key is stored only on this computer.",
        ["mm.nexusCheck"] = "Check",
        ["mm.nexusKeyInvalid"] = "The key is not valid.",
        ["mm.nexusKeyPremium"] = "The key works and is Premium: mods download directly.",
        ["mm.nexusKeyFree"] = "The key works but isn't Premium: mod pages will open.",
        ["mm.pack.new"] = "New pack",
        ["mm.pack.newHint"] = "Your mods that are on, in the current order.",
        ["mm.pack.name"] = "Pack name",
        ["mm.pack.create"] = "Create from current mods",
        ["mm.pack.import"] = "Import a pack",
        ["mm.pack.list"] = "Your packs",
        ["mm.pack.none"] = "No packs yet.",
        ["mm.pack.status"] = "{0} / {1} mods ready",
        ["mm.pack.apply"] = "Apply pack",
        ["mm.pack.applyTip"] = "Turns on the pack's mods, sets their order and installs the mods that come with the pack. Other mods turn off.",
        ["mm.pack.subscribeAll"] = "Subscribe to missing Workshop mods",
        ["mm.pack.export"] = "Send to a friend (zip)",
        ["mm.pack.exportTitle"] = "Save the mod pack",
        ["mm.pack.importTitle"] = "Mod pack (zip or .ckpack)",
        ["mm.pack.defaultName"] = "Pack {0:dd.MM HH:mm}",
        ["mm.pack.created"] = "\"{0}\" created ({1} mods).",
        ["mm.pack.exported"] = "{0} saved. It contains HOW TO INSTALL.txt.",
        ["mm.pack.imported"] = "\"{0}\" imported. Install it with \"Apply pack\".",
        ["mm.pack.appliedMissing"] = "\"{0}\" applied; {1} mods are missing. Subscribe or download them from the list.",
        ["mm.state.Ready"] = "ready",
        ["mm.state.WrongVersion"] = "different version",
        ["mm.state.MissingWorkshop"] = "missing: subscribe on the Workshop",
        ["mm.state.MissingBundled"] = "missing: installs from the pack",
        ["mm.state.MissingManual"] = "missing: download by hand",
        ["settings.install.app"] = "CoopKeep app",
    };
}
