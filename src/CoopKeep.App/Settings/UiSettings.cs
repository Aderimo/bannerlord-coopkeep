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
