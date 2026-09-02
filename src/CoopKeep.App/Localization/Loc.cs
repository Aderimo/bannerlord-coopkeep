using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CoopKeep.App.Localization;

/// <summary>
/// Basit, bağımlılıksız yerelleştirme. Türkçe ve İngilizce.
/// </summary>
/// <remarks>
/// XAML'de <c>{Binding L[anahtar]}</c> şeklinde kullanılır. Dil değiştiğinde
/// <c>Item[]</c> için değişiklik bildirilir; Avalonia bunu indeksleyici
/// bağlamalarını tazelemek için kullanır, yani tüm arayüz anında güncellenir.
/// <para>
/// <b>Terminoloji notu:</b> Kullanıcı kendi bilgisayarında bir sunucu açıyor.
/// Dosyalar teknik olarak Bannerlord "kampanya save'leri" ama arayüzde bunlara
/// <b>sunucu</b> diyoruz — kullanıcının zihnindeki karşılığı bu.
/// </para>
/// </remarks>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Current { get; } = new();

    private string _language = DetectSystemLanguage();

    private Loc() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary><c>"tr"</c> veya <c>"en"</c>.</summary>
    public string Language
    {
        get => _language;
        set
        {
            var normalized = value == "tr" ? "tr" : "en";
            if (_language == normalized) return;

            _language = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTurkish)));
        }
    }

    public bool IsTurkish => _language == "tr";

    public void Toggle() => Language = IsTurkish ? "en" : "tr";

    /// <summary>Anahtar bulunamazsa anahtarın kendisi döner — eksik çeviri görünür olur, çökmez.</summary>
    public string this[string key] =>
        (IsTurkish ? Turkish : English).TryGetValue(key, out var value) ? value : key;

    private static string DetectSystemLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    // ------------------------------------------------------------------

    private static readonly Dictionary<string, string> Turkish = new(StringComparer.Ordinal)
    {
        // Düğme, geçilecek dili gösterir — tıklayınca ne olacağını söyler.
        ["lang.label"] = "EN",

        ["app.subtitle"] = "Bannerlord Coop Sunucu Yöneticisi",

        ["status.unknown"] = "Bilinmiyor",
        ["status.stopped"] = "Kapalı",
        ["status.starting"] = "Başlatılıyor",
        ["status.boot"] = "Motor açılıyor",
        ["status.loading"] = "Dünya yükleniyor",
        ["status.serving"] = "Yayında",
        ["status.stopping"] = "Kapatılıyor",
        ["status.shutdown"] = "Kapandı",

        ["btn.start"] = "Başlat",
        ["btn.stop"] = "Durdur",
        ["btn.save"] = "Kaydet",
        ["btn.announce"] = "Duyuru",
        ["btn.new"] = "Yeni sunucu",
        ["btn.refresh"] = "Yenile",
        ["btn.kick"] = "At",
        ["btn.apply"] = "Kaydet",

        ["sec.campaigns"] = "Sunucularım",
        ["sec.players"] = "Oyuncular",
        ["sec.console"] = "Sunucu konsolu",
        ["sec.server"] = "Sunucu",
        ["sec.settings"] = "Ayarlar",
        ["sec.backups"] = "Yedekler",

        ["col.players"] = "Oyuncu",

        ["lbl.port"] = "Port",
        ["lbl.password"] = "Şifre",

        // Birim yazılmazsa "otomatik kayıt" bir sayı gibi görünüp oyuncu sayısı
        // sanılabiliyor. Birim etikete gömülü.
        ["lbl.autosave"] = "Otomatik kayıt (dakika)",
        ["msg.autosaveHint"] = "Sunucu kaç dakikada bir dünyayı kaydetsin. 0 = otomatik kayıt kapalı.",

        ["lbl.steam"] = "Steam keşfi",
        ["lbl.active"] = "Açık sunucu",
        ["lbl.hostName"] = "Steam'de görünen adın",
        ["lbl.madeBy"] = "Geliştiren",

        ["val.on"] = "Açık",
        ["val.off"] = "Kapalı",
        ["val.hasPassword"] = "Var",
        ["val.noPassword"] = "Yok — herkes girebilir",
        ["val.autosaveOff"] = "Kapalı",
        ["val.minutes"] = "dakikada bir",

        ["msg.noCampaigns"] = "Henüz sunucun yok. Alttaki kutuya bir ad yazıp \"Yeni sunucu\"ya bas.",
        ["msg.noPlayers"] = "Bağlı oyuncu yok.",
        ["msg.creating"] = "Sunucu dünyası kuruluyor, yaklaşık bir dakika sürüyor...",
        ["msg.announcePlaceholder"] = "Oyunculara gönderilecek mesaj",
        ["msg.readyIn"] = "Dünya yüklemesi yaklaşık 30-60 saniye sürer.",
        ["msg.hostNameHint"] = "Arkadaşların sunucu tarayıcısında bu adı arar — sunucu adını değil.",
        ["msg.hostNameUnknown"] = "Sunucu başlayınca görünecek",
        ["msg.searchPlayers"] = "Oyuncu ara",
        ["msg.settingsNeedRestart"] = "Değişiklikler sunucu yeniden başlatılınca geçerli olur.",
        ["msg.settingsSaved"] = "Ayarlar kaydedildi.",
        ["msg.steamHint"] = "Kapalıysa sunucu Steam listesinde görünmez; yalnızca IP ile bağlanılır.",

        // Sunucu dünyası işlemleri
        ["lbl.campaignName"] = "Sunucu adı",
        ["btn.rename"] = "Yeniden adlandır",
        ["btn.duplicate"] = "Kopyala",
        ["btn.delete"] = "Sil",
        ["btn.openFolder"] = "Klasörü aç",
        ["btn.confirmDelete"] = "Silmeyi onayla",
        ["btn.cancel"] = "Vazgeç",
        ["msg.deleteWarning"] = "Bu sunucu dünyası, sunucunun kendi yedek kuşakları ve tüm yedekleri kalıcı olarak silinecek.",
        ["msg.deleted"] = "{0} dünya dosyası ve {1} yedek silindi.",

        // Kaynak kullanımı
        ["lbl.cpu"] = "İşlemci",
        ["lbl.ram"] = "Bellek",
        ["msg.ramHint"] = "Bannerlord sunucusunun bellek sınırı ayarı yoktur; buradaki değer yalnızca anlık kullanımı gösterir.",

        // Gizlilik
        ["btn.privacy"] = "Gizle",
        ["msg.privacyOn"] = "Gizli — yayın için güvenli",
        ["msg.privacyHint"] = "IP, port ve Steam adın gizlendi. Göstermek için göze bas.",
        ["msg.consoleHidden"] = "Konsol gizli. Göstermek için üstteki göz simgesine bas.",
        ["val.masked"] = "••••••••",

        // Yedekleme
        ["btn.backupNow"] = "Yedek al",
        ["btn.restore"] = "Geri yükle",
        ["btn.confirmRestore"] = "Geri yüklemeyi onayla",
        ["lbl.autoBackup"] = "Otomatik yedek",
        ["msg.noBackups"] = "Bu sunucu için henüz yedek yok.",
        ["msg.backupTaken"] = "Yedek alındı.",
        ["msg.restoreWarning"] = "Mevcut dünya bu yedekle değiştirilecek. Önce mevcut hâlin yedeği alınır.",
        ["msg.restored"] = "Yedek geri yüklendi.",
        ["msg.backupHint"] = "Sunucu her kaydettiğinde otomatik yedek alınır. Eskiler sırayla silinir.",
        ["msg.stopToRestore"] = "Geri yüklemek için sunucuyu durdurun.",

        // Çökme kurtarma
        ["lbl.autoRestart"] = "Çökerse yeniden başlat",
        ["msg.crashed"] = "Sunucu beklenmedik şekilde kapandı.",
        ["msg.restartingIn"] = "Yeniden başlatılıyor...",
        ["msg.crashLoop"] = "Sunucu üst üste çöküyor. Otomatik yeniden başlatma durduruldu.",
        ["msg.noAutoRestart"] = "Bu hata yeniden başlatmakla düzelmez — otomatik yeniden başlatma yapılmadı.",

        // Karşılama
        ["tut.title"] = "CoopKeep'e hoş geldin",
        ["tut.step1title"] = "1 · Sunucu seç veya yarat",
        ["tut.step1"] = "Soldaki listeden bir sunucu seç. Hiç yoksa alttaki kutuya bir ad yazıp \"Yeni sunucu\"ya bas — oyuna girmene gerek yok, dünyayı sunucu kurar.",
        ["tut.step2title"] = "2 · Başlat",
        ["tut.step2"] = "\"Başlat\"a bas ve yaklaşık bir dakika bekle. Durum \"Yayında\" olduğunda sunucun hazırdır.",
        ["tut.step3title"] = "3 · Arkadaşların nasıl girer",
        ["tut.step3"] = "Oyunu açıp Coop menüsünden Steam sunucu listesine bakarlar. Aradıkları isim SENİN STEAM ADIN, sunucu adı değil — sunucu panelinde yazıyor.",
        ["tut.step4title"] = "4 · Yayın yapıyorsan",
        ["tut.step4"] = "Üstteki göz simgesi IP, port ve Steam adını gizler. Konsol da bulanıklaşır, böylece yayında özel bilgin görünmez.",
        ["tut.dontShow"] = "Bir daha gösterme",
        ["tut.close"] = "Başlayalım",
    };

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["lang.label"] = "TR",

        ["app.subtitle"] = "Bannerlord Coop Server Manager",

        ["status.unknown"] = "Unknown",
        ["status.stopped"] = "Stopped",
        ["status.starting"] = "Starting",
        ["status.boot"] = "Booting engine",
        ["status.loading"] = "Loading world",
        ["status.serving"] = "Online",
        ["status.stopping"] = "Stopping",
        ["status.shutdown"] = "Shut down",

        ["btn.start"] = "Start",
        ["btn.stop"] = "Stop",
        ["btn.save"] = "Save",
        ["btn.announce"] = "Announce",
        ["btn.new"] = "New server",
        ["btn.refresh"] = "Refresh",
        ["btn.kick"] = "Kick",
        ["btn.apply"] = "Save",

        ["sec.campaigns"] = "My servers",
        ["sec.players"] = "Players",
        ["sec.console"] = "Server console",
        ["sec.server"] = "Server",
        ["sec.settings"] = "Settings",
        ["sec.backups"] = "Backups",

        ["col.players"] = "players",

        ["lbl.port"] = "Port",
        ["lbl.password"] = "Password",
        ["lbl.autosave"] = "Autosave (minutes)",
        ["msg.autosaveHint"] = "How often the server saves the world. 0 disables autosaving.",
        ["lbl.steam"] = "Steam discovery",
        ["lbl.active"] = "Running server",
        ["lbl.hostName"] = "Your name on Steam",
        ["lbl.madeBy"] = "Made by",

        ["val.on"] = "On",
        ["val.off"] = "Off",
        ["val.hasPassword"] = "Set",
        ["val.noPassword"] = "None — anyone can join",
        ["val.autosaveOff"] = "Off",
        ["val.minutes"] = "min interval",

        ["msg.noCampaigns"] = "No servers yet. Type a name below and press \"New server\".",
        ["msg.noPlayers"] = "No players connected.",
        ["msg.creating"] = "Building the server world, this takes about a minute...",
        ["msg.announcePlaceholder"] = "Message to broadcast to players",
        ["msg.readyIn"] = "Loading the world takes about 30-60 seconds.",
        ["msg.hostNameHint"] = "Your friends search for this name in the server browser — not the server name.",
        ["msg.hostNameUnknown"] = "Appears once the server starts",
        ["msg.searchPlayers"] = "Search players",
        ["msg.settingsNeedRestart"] = "Changes take effect after the server restarts.",
        ["msg.settingsSaved"] = "Settings saved.",
        ["msg.steamHint"] = "When off, the server is hidden from the Steam list; join by IP only.",

        // Server world actions
        ["lbl.campaignName"] = "Server name",
        ["btn.rename"] = "Rename",
        ["btn.duplicate"] = "Duplicate",
        ["btn.delete"] = "Delete",
        ["btn.openFolder"] = "Open folder",
        ["btn.confirmDelete"] = "Confirm delete",
        ["btn.cancel"] = "Cancel",
        ["msg.deleteWarning"] = "This server world, the server's own backup generations and all backups will be permanently deleted.",
        ["msg.deleted"] = "Deleted {0} world files and {1} backups.",

        // Resource usage
        ["lbl.cpu"] = "CPU",
        ["lbl.ram"] = "Memory",
        ["msg.ramHint"] = "The Bannerlord server has no memory limit setting; this only shows current usage.",

        // Privacy
        ["btn.privacy"] = "Hide",
        ["msg.privacyOn"] = "Hidden — safe to stream",
        ["msg.privacyHint"] = "IP, port and your Steam name are hidden. Press the eye to reveal.",
        ["msg.consoleHidden"] = "Console hidden. Press the eye icon above to reveal.",
        ["val.masked"] = "••••••••",

        // Backups
        ["btn.backupNow"] = "Back up",
        ["btn.restore"] = "Restore",
        ["btn.confirmRestore"] = "Confirm restore",
        ["lbl.autoBackup"] = "Auto backup",
        ["msg.noBackups"] = "No backups for this server yet.",
        ["msg.backupTaken"] = "Backup created.",
        ["msg.restoreWarning"] = "The current world will be replaced by this backup. The current state is backed up first.",
        ["msg.restored"] = "Backup restored.",
        ["msg.backupHint"] = "A backup is taken every time the server saves. Old ones are pruned automatically.",
        ["msg.stopToRestore"] = "Stop the server to restore a backup.",

        // Crash recovery
        ["lbl.autoRestart"] = "Restart on crash",
        ["msg.crashed"] = "The server shut down unexpectedly.",
        ["msg.restartingIn"] = "Restarting...",
        ["msg.crashLoop"] = "The server keeps crashing. Automatic restart has been stopped.",
        ["msg.noAutoRestart"] = "Restarting will not fix this error — no automatic restart was attempted.",

        // Onboarding
        ["tut.title"] = "Welcome to CoopKeep",
        ["tut.step1title"] = "1 · Pick or create a server",
        ["tut.step1"] = "Choose a server on the left. If you have none, type a name in the box below and press \"New server\" — you never have to enter the game, the server builds the world.",
        ["tut.step2title"] = "2 · Start",
        ["tut.step2"] = "Press \"Start\" and wait about a minute. Once the status reads \"Online\", your server is up.",
        ["tut.step3title"] = "3 · How your friends join",
        ["tut.step3"] = "They open the game and browse the Steam server list in the Coop menu. The name they search for is YOUR STEAM NAME, not the server name — it is shown in the server panel.",
        ["tut.step4title"] = "4 · If you stream",
        ["tut.step4"] = "The eye icon at the top hides your IP, port and Steam name, and blurs the console — so nothing private shows up on stream.",
        ["tut.dontShow"] = "Don't show again",
        ["tut.close"] = "Let's go",
    };
}
