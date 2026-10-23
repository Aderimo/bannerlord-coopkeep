using System.Xml.Linq;

namespace CoopKeep.Core.Mods;

/// <summary>
/// Bilgisayardaki Bannerlord modlarını bulur: oyunun <c>Modules</c> klasörü ve Steam Atölye.
/// </summary>
/// <remarks>
/// <para>
/// Her modun <c>SubModule.xml</c>'i okunur. Aynı kimlik iki yerde varsa oyunun kendi
/// klasöründeki tercih edilir: oyunun başlatıcısı da önce oraya bakıyor.
/// </para>
/// <para>
/// Aynı mod Atölye'de birden çok sayfada olabiliyor (ör. Mod Configuration Menu v5'in
/// resmî sayfası ve bir yeniden yüklemesi, ikisi de <c>Bannerlord.MBOptionScreen</c> ama
/// sürümleri farklı). Oyun yalnızca açık Steam hesabının abone olduğu öğeleri görür; ikisine
/// de aboneyse hangisini yükleyeceği belli değildir ve arkadaşla sürüm tutmayabilir.
/// </para>
/// </remarks>
public static class ModuleScanner
{
    /// <summary>Sunucu derlemelerinin klasörü (özel sunucu motoru bunu yükler).</summary>
    public const string ServerBin = "Win64_Shipping_Server";

    /// <summary>.NET Core istemci derlemeleri (Game Pass sürümü).</summary>
    public const string DotNetCoreBin = "Gaming.Desktop.x64_Shipping_Client";

    /// <summary>Steam istemcisi derlemeleri (.NET Framework).</summary>
    public const string ClientBin = "Win64_Shipping_Client";

    /// <param name="workshopAvailable">Atölye öğesi açık hesapta abone mi (oyun onu görür mü)?</param>
    public static IReadOnlyList<InstalledModule> Scan(string? gameModulesDirectory, IEnumerable<string>? workshopDirectories,
        Func<string, bool>? workshopAvailable = null) =>
        Pick(ScanCopies(gameModulesDirectory, workshopDirectories), workshopAvailable);

    /// <summary>Bütün kopyalar: aynı kimlik birden çok klasörde olabilir (önce oyunun klasörü).</summary>
    public static IReadOnlyList<InstalledModule> ScanCopies(string? gameModulesDirectory, IEnumerable<string>? workshopDirectories)
    {
        var found = new List<InstalledModule>();

        if (gameModulesDirectory is not null && Directory.Exists(gameModulesDirectory))
        {
            foreach (var dir in SafeDirectories(gameModulesDirectory))
                if (TryRead(dir, ModuleSource.GameFolder) is { } m) found.Add(m);
        }

        foreach (var root in workshopDirectories ?? [])
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeDirectories(root))
                if (TryRead(dir, ModuleSource.Workshop) is { } m) found.Add(m);
        }

        return found;
    }

    /// <summary>
    /// Her kimlik için oyunun kullanacağı kopya: oyunun klasöründeki, yoksa açık hesabın abone
    /// olduğu Atölye öğesi, o da yoksa ilk bulunan.
    /// </summary>
    public static IReadOnlyList<InstalledModule> Pick(IReadOnlyList<InstalledModule> copies, Func<string, bool>? workshopAvailable = null) =>
        copies.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.FirstOrDefault(m => m.Source == ModuleSource.GameFolder)
                         ?? g.FirstOrDefault(m => Visible(m, workshopAvailable))
                         ?? g.First())
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Oyunun ikisini birden gördüğü Atölye kopyaları (oyunun klasöründe kopyası olmayan
    /// kimlikler için): hangisinin yükleneceği belli değil.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<InstalledModule>> Duplicates(IReadOnlyList<InstalledModule> copies,
        Func<string, bool>? workshopAvailable = null) =>
        copies.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.All(m => m.Source == ModuleSource.Workshop))
            .Select(g => (g.Key, Visible: g.Where(m => Visible(m, workshopAvailable)).ToList()))
            .Where(x => x.Visible.Count > 1)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<InstalledModule>)x.Visible, StringComparer.OrdinalIgnoreCase);

    private static bool Visible(InstalledModule m, Func<string, bool>? workshopAvailable) =>
        m.Source != ModuleSource.Workshop || m.WorkshopId is not { } w || workshopAvailable?.Invoke(w) != false;

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Klasördeki <c>SubModule.xml</c>'i okur; mod değilse ya da bozuksa <see langword="null"/>.</summary>
    public static InstalledModule? TryRead(string folder, ModuleSource source)
    {
        var path = Path.Combine(folder, "SubModule.xml");
        if (!File.Exists(path)) return null;

        try
        {
            var root = XDocument.Load(path).Root;
            if (root is null) return null;
            return Parse(root, folder, source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary><c>SubModule.xml</c> içeriğini yorumlar (dosya sistemine yalnızca derleme klasörleri için bakar).</summary>
    public static InstalledModule? Parse(XElement root, string folder, ModuleSource source)
    {
        var id = Value(root, "Id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var type = Value(root, "ModuleType");
        var official = type is "Official" or "OfficialOptional" ||
                       string.Equals(Value(root, "Official"), "true", StringComparison.OrdinalIgnoreCase);
        var category = Value(root, "ModuleCategory");
        var multiplayer = string.Equals(category, "Multiplayer", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(Value(root, "MultiplayerModule"), "true", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(Value(root, "SingleplayerModule"), "true", StringComparison.OrdinalIgnoreCase);

        var deps = new List<ModuleDependency>();
        var loadAfter = new List<string>();
        var loadBefore = new List<string>();

        foreach (var d in root.Element("DependedModules")?.Elements("DependedModule") ?? [])
        {
            var depId = Attr(d, "Id");
            if (depId is null) continue;
            deps.Add(new ModuleDependency(depId, Attr(d, "DependentVersion"),
                string.Equals(Attr(d, "Optional"), "true", StringComparison.OrdinalIgnoreCase)));
            loadAfter.Add(depId);
        }

        foreach (var d in root.Element("ModulesToLoadAfterThis")?.Elements("Module") ?? [])
            if (Attr(d, "Id") is { } after) loadBefore.Add(after);

        // Topluluk bağımlılık bilgisi (BUTR biçimi): sıra ve isteğe bağlılık.
        foreach (var d in root.Element("DependedModuleMetadatas")?.Elements("DependedModuleMetadata") ?? [])
        {
            var depId = Attr(d, "id") ?? Attr(d, "Id");
            if (depId is null) continue;
            var optional = string.Equals(Attr(d, "optional"), "true", StringComparison.OrdinalIgnoreCase);
            var incompatible = string.Equals(Attr(d, "incompatible"), "true", StringComparison.OrdinalIgnoreCase);
            if (incompatible) continue;

            switch (Attr(d, "order"))
            {
                case "LoadBeforeThis":
                    loadAfter.Add(depId);
                    if (!optional && deps.All(x => !x.Id.Equals(depId, StringComparison.OrdinalIgnoreCase)))
                        deps.Add(new ModuleDependency(depId, Attr(d, "version"), Optional: false));
                    break;
                case "LoadAfterThis":
                    loadBefore.Add(depId);
                    break;
            }
        }

        var hasCode = root.Element("SubModules")?.Elements("SubModule")
            .Any(s => !string.IsNullOrWhiteSpace(Value(s, "DLLName"))) == true;

        var bin = Path.Combine(folder, "bin");
        bool HasDlls(string name) => Directory.Exists(Path.Combine(bin, name)) &&
                                     Directory.EnumerateFiles(Path.Combine(bin, name), "*.dll").Any();

        return new InstalledModule
        {
            Id = id.Trim(),
            Name = Value(root, "Name")?.Trim() is { Length: > 0 } n ? n : id.Trim(),
            Version = Value(root, "Version")?.Trim(),
            Folder = folder,
            Source = source,
            WorkshopId = source == ModuleSource.Workshop ? Path.GetFileName(folder) : null,
            IsOfficial = official,
            IsMultiplayer = multiplayer,
            Dependencies = deps,
            LoadAfter = loadAfter.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            LoadBefore = loadBefore.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            HasCode = hasCode,
            HasServerBinaries = HasDlls(ServerBin),
            HasDotNetCoreBinaries = HasDlls(DotNetCoreBin),
            HasClientBinaries = HasDlls(ClientBin),
            GameVersion = deps.FirstOrDefault(x => x.Id.Equals("Native", StringComparison.OrdinalIgnoreCase))?.Version,
        };
    }

    private static string? Value(XElement parent, string name) => parent.Element(name)?.Attribute("value")?.Value;

    private static string? Attr(XElement e, string name)
    {
        var v = e.Attribute(name)?.Value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// İki oyun sürümünün ana ve ara numarası aynı mı (<c>v1.2.12</c> ile <c>v1.4.8</c> farklı)?
    /// Bilinmeyen sürümde <see langword="true"/> döner: uyarı yalnızca emin olunca verilir.
    /// </summary>
    public static bool SameMinorVersion(string? a, string? b)
    {
        static (int, int)? Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.TrimStart('v', 'V', 'e', 'E').Split('.');
            return parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor)
                ? (major, minor)
                : null;
        }

        var x = Parse(a);
        var y = Parse(b);
        return x is null || y is null || x == y;
    }
}
