using System.Xml.Linq;

namespace CoopKeep.Core.Installation;

/// <summary>Modül uyumluluk denetiminin sonucu.</summary>
/// <param name="EnabledModules">Oyun başlatıcısında seçili olan modüller.</param>
/// <param name="ExtraModules">Sunucunun beklemediği, fazladan seçili modüller.</param>
/// <param name="CoopEnabled">Coop modülü seçili mi?</param>
public sealed record ModuleCheckResult(
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> ExtraModules,
    bool CoopEnabled)
{
    /// <summary>Bağlantı sorunu çıkarabilecek bir durum var mı?</summary>
    public bool HasProblem => !CoopEnabled || ExtraModules.Count > 0;
}

/// <summary>
/// Oyun istemcisinde seçili modülleri, sunucunun yüklediği modüllerle karşılaştırır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden bu kontrol var:</b> Sunucu yalnızca resmî modülleri ve Coop'u yüklüyor.
/// İstemcide fazladan bir modül seçiliyse Coop'un modül doğrulaması bağlantıyı
/// reddedebiliyor, ve oyunun verdiği hata bunu açıkça söylemiyor. Pratikte
/// "arkadaşım bağlanamıyor" şikayetlerinin en olası kaynağı bu.
/// </para>
/// <para>
/// Seçili modüller <c>Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml</c>
/// dosyasındaki <c>IsSelected</c> alanlarından okunuyor — yani oyunun başlatıcısında
/// gerçekten işaretli olanlar, kurulu olanların tamamı değil.
/// </para>
/// </remarks>
public static class ModuleCompatibility
{
    /// <summary>
    /// Sunucunun yüklediği modüller. Sunucu logundaki
    /// <c>Command Args: _MODULES_*Native*SandBoxCore*Sandbox*CoopNightly*DedicatedServer.Windows*_MODULES_</c>
    /// satırından alındı.
    /// </summary>
    private static readonly HashSet<string> ServerModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode",
        "CoopNightly", "Coop", "DedicatedServer.Windows",
    };

    public static string LauncherDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "Configs", "LauncherData.xml");

    /// <summary>
    /// İstemcide seçili modülleri okur ve sunucuyla karşılaştırır.
    /// Dosya okunamazsa <see langword="null"/> döner — bu bir hata değil, bilinmezliktir.
    /// </summary>
    public static ModuleCheckResult? Check(string? launcherDataPath = null)
    {
        var path = launcherDataPath ?? LauncherDataPath;
        if (!File.Exists(path)) return null;

        try
        {
            var doc = XDocument.Load(path);

            var enabled = doc.Descendants("UserModData")
                .Where(m => string.Equals(m.Element("IsSelected")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Element("Id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (enabled.Count == 0) return null;

            var extra = enabled
                .Where(id => !ServerModules.Contains(id))
                .ToList();

            var coopEnabled = enabled.Any(id =>
                id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("Coop", StringComparison.OrdinalIgnoreCase));

            return new ModuleCheckResult(enabled, extra, coopEnabled);
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
