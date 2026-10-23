using System.Diagnostics;
using System.Xml.Linq;

namespace CoopKeep.Core.Installation;

/// <summary>Bir modülün Coop sunucusuyla uyumu.</summary>
public enum ModuleStatus
{
    /// <summary>Sunucu da yüklüyor; sorun yok.</summary>
    Supported,

    /// <summary>Coop açıkça reddediyor (War Sails, Birth and Aging Options, Fast Mode).</summary>
    Blocked,

    /// <summary>Sunucu bu modülü yüklemiyor; bağlantı reddedilebilir.</summary>
    NotOnServer,
}

/// <param name="Id">Modül kimliği (<c>LauncherData.xml</c>'deki <c>Id</c>).</param>
/// <param name="IsSelected">Oyun başlatıcısında işaretli mi?</param>
public sealed record ModuleEntry(string Id, string? Version, bool IsSelected, ModuleStatus Status);

/// <summary>Modül uyumluluk denetiminin sonucu.</summary>
/// <param name="EnabledModules">Oyun başlatıcısında seçili olan modüller.</param>
/// <param name="ExtraModules">Sunucunun beklemediği, fazladan seçili modüller.</param>
/// <param name="CoopEnabled">Coop modülü seçili mi?</param>
public sealed record ModuleCheckResult(
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> ExtraModules,
    bool CoopEnabled)
{
    /// <summary>Başlatıcıdaki tüm modüller, durumlarıyla.</summary>
    public IReadOnlyList<ModuleEntry> All { get; init; } = Array.Empty<ModuleEntry>();

    /// <summary>Seçili ve Coop'un açıkça reddettiği modüller.</summary>
    public IReadOnlyList<string> BlockedModules =>
        All.Where(m => m.IsSelected && m.Status == ModuleStatus.Blocked).Select(m => m.Id).ToList();

    /// <summary>Bağlantı sorunu çıkarabilecek bir durum var mı?</summary>
    public bool HasProblem => !CoopEnabled || ExtraModules.Count > 0;
}

/// <summary>
/// Oyun istemcisinde seçili modülleri, sunucunun yüklediği modüllerle karşılaştırır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kaynaklar</b> (Coop v0.1.6, 2026-10-16):
/// </para>
/// <list type="bullet">
/// <item>Sunucu logu: <c>_MODULES_*Native*SandBoxCore*Sandbox*Coop*DedicatedServer.Windows*_MODULES_</c>.
/// Sunucuya başka modül eklenemiyor: Coop modülü SHA-256 ile doğrulanıyor ve
/// modül listesi sabit. <c>CustomBattle</c> ve <c>StoryMode</c> sunucuda yok ama Coop'un
/// istemci tarafı onlara bağımlı (<c>SubModule.xml</c>) ve talimat onları reddetmiyor.</item>
/// <item>Modun kendi talimatı ("Server Instructions.md"): War Sails (<c>NavalDLC</c>),
/// Birth and Aging Options (<c>BirthAndDeath</c>) ve Fast Mode (<c>FastMode</c>) açık olan
/// herkes <b>reddediliyor</b>. Diğer modlar için: <i>"everyone needs the same mods and
/// versions, and it is best to run no other mods"</i> — hata metni
/// <c>Server does not support module ...</c>.</item>
/// </list>
/// <para>
/// Seçili modüller <c>Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml</c>
/// dosyasındaki <c>IsSelected</c> alanlarından okunuyor.
/// </para>
/// </remarks>
public static class ModuleCompatibility
{
    /// <summary>Sunucunun yüklediği modüller.</summary>
    public static readonly IReadOnlySet<string> ServerModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode",
        "Coop", "CoopNightly",
        // Çok oyunculu modu tek oyunculu oyunu etkilemiyor; başlatıcı onu ayrı listeliyor.
        "Multiplayer",
    };

    /// <summary>Coop'un açıkça reddettiği resmî modüller.</summary>
    public static readonly IReadOnlySet<string> BlockedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NavalDLC", "BirthAndDeath", "FastMode",
        // Başka bir co-op modu (CoopKeep'in kendi modu): Coop ile aynı işi yapar, birlikte açılmaz.
        "CoopKeep",
    };

    public static string LauncherDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "Configs", "LauncherData.xml");

    public static ModuleStatus Classify(string id) =>
        BlockedModules.Contains(id) ? ModuleStatus.Blocked
        : ServerModules.Contains(id) ? ModuleStatus.Supported
        : ModuleStatus.NotOnServer;

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
            var all = ReadEntries(doc);

            var enabled = all.Where(m => m.IsSelected).Select(m => m.Id).ToList();
            if (enabled.Count == 0) return null;

            var extra = all
                .Where(m => m.IsSelected && m.Status != ModuleStatus.Supported)
                .Select(m => m.Id)
                .ToList();

            var coopEnabled = enabled.Any(id =>
                id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("Coop", StringComparison.OrdinalIgnoreCase));

            return new ModuleCheckResult(enabled, extra, coopEnabled) { All = all };
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static List<ModuleEntry> ReadEntries(XDocument doc) =>
        SinglePlayerMods(doc)
            .Select(m => (Id: m.Element("Id")?.Value?.Trim(), Element: m))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(x => new ModuleEntry(
                x.Id!,
                x.Element.Element("LastKnownVersion")?.Value,
                string.Equals(x.Element.Element("IsSelected")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                Classify(x.Id!)))
            .ToList();

    /// <summary>
    /// Tek oyunculu mod listesi. Dosyada ayrıca <c>MultiplayerData</c> bölümü var;
    /// Coop tek oyunculu oyunda çalıştığı için o bölüm karışmamalı.
    /// </summary>
    private static IEnumerable<XElement> SinglePlayerMods(XDocument doc)
    {
        var single = doc.Root?.Element("SingleplayerData");
        return single is not null ? single.Descendants("UserModData") : doc.Descendants("UserModData");
    }

    // ------------------------------------------------------------------
    // Coop profili: sorunlu modları tek tıkla kapatıp geri açmak
    // ------------------------------------------------------------------

    /// <summary>Coop profili uygulanmadan önceki dosyanın yedeği.</summary>
    public static string ProfileBackupPath(string? launcherDataPath = null) =>
        (launcherDataPath ?? LauncherDataPath) + ".coopkeep-backup";

    /// <summary>Oyun veya başlatıcısı açık mı? Açıkken dosyayı değiştirmek boşa gider: kapanırken üzerine yazar.</summary>
    public static bool IsGameRunning() => IsGameRunning(null);

    /// <param name="exceptProcessId">Sayılmayacak süreç (yoksa <c>null</c>).</param>
    public static bool IsGameRunning(int? exceptProcessId)
    {
        string[] names = ["Bannerlord", "Bannerlord.Native", "TaleWorlds.MountAndBlade.Launcher", "Bannerlord.BLSE.Standalone"];

        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            var running = processes.Any(p => p.Id != exceptProcessId);
            foreach (var p in processes) p.Dispose();
            if (running) return true;
        }

        return false;
    }

    /// <summary>
    /// Sunucuyla uyumsuz tüm modülleri kapatır, Coop'u açar. Önce yedek alınır.
    /// </summary>
    /// <returns>Kapatılan modüller.</returns>
    public static IReadOnlyList<string> ApplyCoopProfile(string? launcherDataPath = null)
    {
        var path = launcherDataPath ?? LauncherDataPath;
        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);

        // Yedek yalnızca bir kez alınır: profil iki kez uygulanırsa ilk (asıl) hâl kaybolmasın.
        var backup = ProfileBackupPath(path);
        if (!File.Exists(backup)) File.Copy(path, backup);

        var disabled = new List<string>();
        foreach (var mod in SinglePlayerMods(doc))
        {
            var id = mod.Element("Id")?.Value?.Trim();
            var selected = mod.Element("IsSelected");
            if (id is null || selected is null) continue;

            var shouldBeOn = Classify(id) == ModuleStatus.Supported &&
                             !id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase) &&
                             !id.Equals("Multiplayer", StringComparison.OrdinalIgnoreCase);

            if (id.Equals("Coop", StringComparison.OrdinalIgnoreCase)) shouldBeOn = true;

            var isOn = string.Equals(selected.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (isOn && !shouldBeOn) disabled.Add(id);
            if (isOn != shouldBeOn) selected.Value = shouldBeOn ? "true" : "false";
        }

        SaveAtomically(doc, path);
        return disabled;
    }

    /// <summary>Coop profilinden önceki mod seçimini geri yükler.</summary>
    public static bool RestoreProfile(string? launcherDataPath = null)
    {
        var path = launcherDataPath ?? LauncherDataPath;
        var backup = ProfileBackupPath(path);
        if (!File.Exists(backup)) return false;

        File.Copy(backup, path, overwrite: true);
        File.Delete(backup);
        return true;
    }

    public static bool HasProfileBackup(string? launcherDataPath = null) =>
        File.Exists(ProfileBackupPath(launcherDataPath));

    private static void SaveAtomically(XDocument doc, string path)
    {
        var temp = path + ".tmp";
        doc.Save(temp, SaveOptions.DisableFormatting);
        File.Move(temp, path, overwrite: true);
    }
}
