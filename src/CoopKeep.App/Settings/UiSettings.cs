using System;
using System.IO;
using System.Text.Json;

namespace CoopKeep.App.Settings;

/// <summary>
/// Uygulamanın kendi tercihleri — sunucu ayarlarından tamamen ayrı.
/// </summary>
/// <remarks>
/// Sunucunun <c>server-config.json</c> dosyasına <b>kesinlikle karışmaz</b>. O dosya
/// mod ekibine ait; oraya kendi arayüz tercihlerimizi yazmak, mod güncellendiğinde
/// çakışma yaratırdı.
/// </remarks>
public sealed class UiSettings
{
    /// <summary>Arayüz dili: <c>tr</c> veya <c>en</c>. Boşsa sistem dilinden seçilir.</summary>
    public string? Language { get; set; }

    /// <summary>Karşılama ekranı kapatıldı mı? (kullanıcı "bir daha gösterme" dediyse)</summary>
    public bool TutorialDismissed { get; set; }

    /// <summary>
    /// Hassas bilgiler gizli mi? (IP/port, Steam adı, konsol)
    /// </summary>
    /// <remarks>
    /// <b>Varsayılan olarak açık.</b> Yayın yapan biri uygulamayı ilk kez açtığında
    /// bilgileri kazara ifşa etmemeli; göstermek bilinçli bir tıklama gerektirir.
    /// Tercih kaydedildiği için bir kez kapatan kullanıcı tekrar uğraşmaz.
    /// </remarks>
    public bool PrivacyMode { get; set; } = true;

    /// <summary>Sunucu her kaydettiğinde otomatik yedek alınsın mı?</summary>
    public bool AutoBackup { get; set; } = true;

    /// <summary>Sunucu çökerse otomatik yeniden başlatılsın mı?</summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>
    /// Kullanıcının CoopKeep'te onayladığı sunucu EULA'sının parmak izi.
    /// Sözleşme değişirse eşleşmez ve onay yeniden istenir.
    /// </summary>
    public string? AcceptedEulaFingerprint { get; set; }

    /// <summary>Sunucu süreçleri "normalin üstü" öncelikle çalışsın mı?</summary>
    public bool HighPriority { get; set; }

    /// <summary>
    /// Kullanıcının kendi Nexus Mods API anahtarı (isteğe bağlı). Premium üyelikte modlar
    /// programdan doğrudan iner; yoksa mod sayfası tarayıcıda açılır.
    /// </summary>
    public string? NexusApiKey { get; set; }

    /// <summary>
    /// Sunucu araçları eklentisi (eşya ver/al, karakter düzenleme) Coop sunucusuna kurulsun mu?
    /// Varsayılan kapalı: eklenti Coop'un sunucu klasörüne eklenir; kullanıcı isterse açar.
    /// </summary>
    public bool ServerTools { get; set; }

    /// <summary>"Arkadaşının sunucusuna katıl"da en son yazılan adres.</summary>
    public string? LastJoinAddress { get; set; }

    /// <summary>
    /// Sunucu motorunun klasörü, elle seçildiyse. Boşsa Steam kütüphanelerinde aranır.
    /// </summary>
    public string? ServerEngineDirectory { get; set; }

    /// <summary>Sunucu bu kadar GB'ı aşınca ana sayfada uyarı gösterilir. 0 = kapalı.</summary>
    public int MemoryWarningGb { get; set; } = 3;

    /// <summary>Açılışta GitHub'da yeni sürüm var mı diye bakılsın mı?</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>Yan menüdeki "Araçlar" grubu açık mı.</summary>
    public bool ToolsExpanded { get; set; }

    /// <summary>En son uygulanan mod paketinin dosyası (Paketler listesinde işaretlenir).</summary>
    public string? LastAppliedPack { get; set; }

    /// <summary>En son görülen Coop sürümü: değişince "Coop güncellendi" notu çıkar.</summary>
    public string? LastCoopVersion { get; set; }

    /// <summary>Zemin teması (System, Classic, Night, Graphite, Light).</summary>
    public string? ThemeBase { get; set; }

    /// <summary>Vurgu rengi (Gold, Blue, Purple, Red, Green, Orange, Yellow, Pink, Teal).</summary>
    public string? ThemeAccent { get; set; }

    /// <summary>
    /// Gelişmiş görünüm: yan menüde Araçlar (yönetici araçları, harita, yardım wiki, konsol).
    /// Kayıtta yoksa eski "Araçlar açık" tercihine göre.
    /// </summary>
    public bool? AdvancedMode { get; set; }

    /// <summary>Modlar sayfasında kompakt satırlar.</summary>
    public bool CompactMods { get; set; }

    /// <summary>Oyuncu katılınca ses ve görev çubuğu uyarısı (pencere arkadayken).</summary>
    public bool JoinAlert { get; set; } = true;

    /// <summary>Harita izleyicilerle paylaşılıyor mu?</summary>
    public bool ShareMap { get; set; }

    /// <summary>İzleyici portu; 0 ise oyun portu + 1.</summary>
    public int SharePort { get; set; }

    /// <summary>İzleyici paylaşım kodu.</summary>
    public string? ShareCode { get; set; }

    /// <summary>İzleyici olarak son bağlanılan ev sahibi (adres, port, kod).</summary>
    public string? ViewerHost { get; set; }
    public int ViewerPort { get; set; }
    public string? ViewerCode { get; set; }

    // ------------------------------------------------------------------

    private static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep");

    private static string FilePath => Path.Combine(Directory, "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath)) ?? new UiSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Bozuk veya okunamayan tercih dosyası uygulamayı açılmaz hâle getirmemeli.
        }

        return new UiSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tercih kaydedilemedi; işlevsel bir kayıp değil.
        }
    }
}
