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
public sealed partial class Loc : INotifyPropertyChanged
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
        (IsTurkish ? TurkishRelease : EnglishRelease).TryGetValue(key, out var release) ? release
        : (IsTurkish ? TurkishCoop : EnglishCoop).TryGetValue(key, out var coop) ? coop
        : (IsTurkish ? TurkishV4 : EnglishV4).TryGetValue(key, out var v4) ? v4
        : (IsTurkish ? Turkish : English).TryGetValue(key, out var value) ? value
        : (IsTurkish ? TurkishV2 : EnglishV2).TryGetValue(key, out var v2) ? v2
        : (IsTurkish ? TurkishUi : EnglishUi).TryGetValue(key, out var ui) ? ui
        : (IsTurkish ? TurkishV3 : EnglishV3).TryGetValue(key, out var v3) ? v3
        : (IsTurkish ? TurkishMods : EnglishMods).TryGetValue(key, out var mods) ? mods
        : key;

    private static string DetectSystemLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    // ------------------------------------------------------------------

    private static readonly Dictionary<string, string> Turkish = new(StringComparer.Ordinal)
    {
        // Düğme, geçilecek dili gösterir — tıklayınca ne olacağını söyler.
        ["lang.label"] = "EN",


        ["status.unknown"] = "Bilinmiyor",
        ["status.stopped"] = "Kapalı",
        ["status.starting"] = "Başlatılıyor",
        ["status.boot"] = "Motor açılıyor",
        ["status.loading"] = "Dünya yükleniyor",
        ["status.serving"] = "Yayında",
        ["status.stopping"] = "Kapatılıyor",
        ["status.shutdown"] = "Kapandı",

        ["btn.announce"] = "Duyuru",
        ["btn.refresh"] = "Yenile",
        ["btn.kick"] = "At",
        ["btn.apply"] = "Kaydet",


        ["col.players"] = "oyuncu",

        ["lbl.port"] = "Port",
        ["lbl.password"] = "Şifre",

        // Birim yazılmazsa "otomatik kayıt" bir sayı gibi görünüp oyuncu sayısı
        // sanılabiliyor. Birim etikete gömülü.
        ["lbl.autosave"] = "Otomatik kayıt (dakika)",
        ["msg.autosaveHint"] = "Sunucu kaç dakikada bir dünyayı kaydetsin. 0 = otomatik kayıt kapalı.",

        ["lbl.steam"] = "Steam keşfi",
        ["lbl.madeBy"] = "Geliştiren",

        ["val.on"] = "Açık",
        ["val.off"] = "Kapalı",
        ["val.hasPassword"] = "Var",
        ["val.noPassword"] = "Yok — herkes girebilir",
        ["val.autosaveOff"] = "Kapalı",
        ["val.minutes"] = "dakikada bir",

        ["msg.noCampaigns"] = "Henüz bir dünyan yok. Aşağıdaki \"Yeni dünya\" ile başla.",
        ["msg.noPlayers"] = "Bağlı oyuncu yok.",
        ["msg.announcePlaceholder"] = "Oyunculara gönderilecek mesaj",
        ["msg.readyIn"] = "Dünya yüklemesi yaklaşık 30-60 saniye sürer.",
        ["msg.hostNameUnknown"] = "Sunucu başlayınca görünecek",
        ["msg.searchPlayers"] = "Oyuncu ara",
        ["msg.settingsNeedRestart"] = "Değişiklikler sunucu yeniden başlatılınca geçerli olur.",
        ["msg.settingsSaved"] = "Ayarlar kaydedildi.",
        ["msg.invalidPort"] = "Port 1 ile 65535 arasında bir sayı olmalı.",
        ["msg.invalidAutosave"] = "Otomatik kayıt dakikası 0 veya daha büyük bir sayı olmalı.",

        // Sunucu dünyası işlemleri
        ["btn.rename"] = "Yeniden adlandır",
        ["btn.duplicate"] = "Kopyala",
        ["btn.delete"] = "Sil",
        ["btn.openFolder"] = "Klasörü aç",
        ["btn.confirmDelete"] = "Silmeyi onayla",
        ["btn.cancel"] = "Vazgeç",
        ["msg.deleteWarning"] = "Bu sunucu dünyası, sunucunun kendi yedek kuşakları ve tüm yedekleri kalıcı olarak silinecek.",
        ["msg.deleted"] = "{0} dünya dosyası ve {1} yedek silindi.",

        // Kaynak kullanımı
        ["msg.ramHint"] = "Bannerlord sunucusunun bellek sınırı ayarı yoktur; buradaki değer yalnızca anlık kullanımı gösterir.",

        // Bağlantı bilgisi
        ["msg.copied"] = "Kopyalandı.",

        // Modül uyumluluğu
        ["msg.modulesUnknown"] = "Modül listesi okunamadı.",

        // Yönetici işlemleri
        ["btn.heal"] = "Tam can ver",
        ["msg.goldInvalid"] = "Altın miktarı bir sayı olmalı.",

        // Sunucu zaten çalışıyor
        ["msg.alreadyRunning"] = "Bu bilgisayarda zaten çalışan bir Coop sunucusu var. CoopKeep ona bağlanamaz; önce Görev Yöneticisi'nden kapatın veya kendi penceresinden durdurun.",
        ["msg.closingServer"] = "Sunucu güvenle kapatılıyor, dünya kaydediliyor...",
        ["sec.auditLog"] = "Yönetici kayıtları",
        ["audit.heal"] = "tam can verildi",
        ["audit.gold"] = "altını {0} olarak ayarlandı",

        // Gizlilik
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
        ["tut.close"] = "Başlayalım",
    };

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["lang.label"] = "TR",


        ["status.unknown"] = "Unknown",
        ["status.stopped"] = "Stopped",
        ["status.starting"] = "Starting",
        ["status.boot"] = "Booting engine",
        ["status.loading"] = "Loading world",
        ["status.serving"] = "Online",
        ["status.stopping"] = "Stopping",
        ["status.shutdown"] = "Shut down",

        ["btn.announce"] = "Announce",
        ["btn.refresh"] = "Refresh",
        ["btn.kick"] = "Kick",
        ["btn.apply"] = "Save",


        ["col.players"] = "players",

        ["lbl.port"] = "Port",
        ["lbl.password"] = "Password",
        ["lbl.autosave"] = "Autosave (minutes)",
        ["msg.autosaveHint"] = "How often the server saves the world. 0 disables autosaving.",
        ["lbl.steam"] = "Steam discovery",
        ["lbl.madeBy"] = "Made by",

        ["val.on"] = "On",
        ["val.off"] = "Off",
        ["val.hasPassword"] = "Set",
        ["val.noPassword"] = "None — anyone can join",
        ["val.autosaveOff"] = "Off",
        ["val.minutes"] = "min interval",

        ["msg.noCampaigns"] = "No worlds yet. Start with \"New world\" below.",
        ["msg.noPlayers"] = "No players connected.",
        ["msg.announcePlaceholder"] = "Message to broadcast to players",
        ["msg.readyIn"] = "Loading the world takes about 30-60 seconds.",
        ["msg.hostNameUnknown"] = "Appears once the server starts",
        ["msg.searchPlayers"] = "Search players",
        ["msg.settingsNeedRestart"] = "Changes take effect after the server restarts.",
        ["msg.settingsSaved"] = "Settings saved.",
        ["msg.invalidPort"] = "The port must be a number between 1 and 65535.",
        ["msg.invalidAutosave"] = "Autosave minutes must be a number of 0 or more.",

        // Server world actions
        ["btn.rename"] = "Rename",
        ["btn.duplicate"] = "Duplicate",
        ["btn.delete"] = "Delete",
        ["btn.openFolder"] = "Open folder",
        ["btn.confirmDelete"] = "Confirm delete",
        ["btn.cancel"] = "Cancel",
        ["msg.deleteWarning"] = "This server world, the server's own backup generations and all backups will be permanently deleted.",
        ["msg.deleted"] = "Deleted {0} world files and {1} backups.",

        // Resource usage
        ["msg.ramHint"] = "The Bannerlord server has no memory limit setting; this only shows current usage.",

        // Connection info
        ["msg.copied"] = "Copied.",

        // Module compatibility
        ["msg.modulesUnknown"] = "Could not read the module list.",

        // Admin actions
        ["btn.heal"] = "Full health",
        ["msg.goldInvalid"] = "The gold amount must be a number.",

        // Server already running
        ["msg.alreadyRunning"] = "A Coop server is already running on this machine. CoopKeep cannot attach to it; close it from Task Manager or stop it from its own window first.",
        ["msg.closingServer"] = "Stopping the server safely, saving the world...",
        ["sec.auditLog"] = "Admin log",
        ["audit.heal"] = "given full health",
        ["audit.gold"] = "gold set to {0}",

        // Privacy
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
        ["tut.close"] = "Let's go",
    };
}
