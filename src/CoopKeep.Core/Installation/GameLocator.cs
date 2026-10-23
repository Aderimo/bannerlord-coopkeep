using System.Runtime.Versioning;
using System.Xml.Linq;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Microsoft.Win32;

namespace CoopKeep.Core.Installation;

/// <summary>
/// Steam kurulumunu, Bannerlord'u ve Coop modunu diskte bulur.
/// </summary>
/// <remarks>
/// <para>
/// Sıra: registry → <c>libraryfolders.vdf</c> → <c>appmanifest_261550.acf</c> → dizin doğrulaması.
/// Her adayın gerçekten var olduğu ayrıca kontrol edilir; Steam bir oyun kütüphaneler
/// arasında taşındığında registry yolunu her zaman güncellemiyor.
/// </para>
/// <para>
/// Workshop öğe kimliği <b>sabit yazılmaz</b> — Coop her sürümde yeni bir öğe kimliği
/// alabiliyor. Bunun yerine <c>DedicatedServer\BannerlordCoopServer.exe</c> içeren
/// klasör aranır.
/// </para>
/// </remarks>
public static class GameLocator
{
    /// <summary>Mount &amp; Blade II: Bannerlord'un Steam uygulama kimliği.</summary>
    public const string BannerlordAppId = "261550";

    private const string ServerExeName = "BannerlordCoopServer.exe";

    // ------------------------------------------------------------------
    // Steam
    // ------------------------------------------------------------------

    public static string? FindSteamRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromRegistry = ReadSteamPathFromRegistry();
            if (fromRegistry is not null && Directory.Exists(fromRegistry))
                return fromRegistry;
        }

        foreach (var candidate in DefaultSteamRoots())
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadSteamPathFromRegistry()
    {
        // HKCU en güvenilir kaynak: kullanıcıya özel ve güncel.
        var candidates = new (RegistryKey Hive, string Path, string Value)[]
        {
            (Registry.CurrentUser,  @"Software\Valve\Steam",              "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam",  "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam",              "InstallPath"),
        };

        foreach (var (hive, path, value) in candidates)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key?.GetValue(value) is string s && !string.IsNullOrWhiteSpace(s))
                    return Path.GetFullPath(s.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
            {
                // Erişim yoksa sıradaki adaya geç.
            }
        }

        return null;
    }

    private static IEnumerable<string> DefaultSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
        }
    }

    /// <summary>
    /// Tüm Steam kütüphane klasörlerini döndürür (ana kurulum dahil).
    /// </summary>
    public static IReadOnlyList<string> FindSteamLibraries()
    {
        var root = FindSteamRoot();
        if (root is null) return Array.Empty<string>();

        var libraries = new List<string> { root };

        var vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return libraries;

        try
        {
            var parsed = VdfConvert.Deserialize(File.ReadAllText(vdfPath));

            foreach (var child in parsed.Value.Children<VProperty>())
            {
                // Modern biçim: her kütüphane bir blok, içinde "path" anahtarı.
                if (child.Value is VObject obj &&
                    obj["path"]?.ToString() is { } p &&
                    Directory.Exists(p))
                {
                    libraries.Add(Path.GetFullPath(p));
                }
            }
        }
        catch (Exception e) when (e is IOException or VdfException or InvalidOperationException)
        {
            // Bozuk vdf: en azından ana kütüphane elimizde.
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------
    // Bannerlord
    // ------------------------------------------------------------------

    public static BannerlordInstallation? FindBannerlord()
    {
        foreach (var library in FindSteamLibraries())
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{BannerlordAppId}.acf");
            if (!File.Exists(manifest)) continue;

            var installDir = ReadInstallDir(manifest);
            if (installDir is null) continue;

            var full = Path.Combine(library, "steamapps", "common", installDir);
            if (IsBannerlordDirectory(full))
                return Describe(full);
        }

        // Manifest okunamadıysa bilinen klasör adıyla dene.
        foreach (var library in FindSteamLibraries())
        {
            var guess = Path.Combine(library, "steamapps", "common", "Mount & Blade II Bannerlord");
            if (IsBannerlordDirectory(guess))
                return Describe(guess);
        }

        // Epic Games: Coop orada da çalışıyor (kullanıcılar Epic'ten girebildiklerini bildirdi).
        if (FindEpicGame(EpicManifestDirectory) is { } epic)
            return Describe(epic) with { IsEpic = true };

        return null;

        static BannerlordInstallation Describe(string dir) => new()
        {
            InstallDirectory = dir,
            GameVersion = ReadGameVersion(dir),
        };
    }

    public static string EpicManifestDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>Epic Games kurulum bildirimlerinden (<c>*.item</c>, JSON) Bannerlord'u bulur.</summary>
    public static string? FindEpicGame(string manifestDirectory)
    {
        if (!Directory.Exists(manifestDirectory)) return null;

        foreach (var file in Directory.EnumerateFiles(manifestDirectory, "*.item"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? "" : "";
                if (!name.Contains("Bannerlord", StringComparison.OrdinalIgnoreCase)) continue;

                var location = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                if (location is not null && IsBannerlordDirectory(location)) return location;
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                // Bozuk ya da okunamayan bildirim: sonrakine bak.
            }
        }

        return null;
    }

    private static bool IsBannerlordDirectory(string dir) =>
        Directory.Exists(Path.Combine(dir, "Modules")) &&
        Directory.Exists(Path.Combine(dir, "bin"));

    private static string? ReadInstallDir(string acfPath)
    {
        try
        {
            var parsed = VdfConvert.Deserialize(File.ReadAllText(acfPath));
            return parsed.Value["installdir"]?.ToString();
        }
        catch (Exception e) when (e is IOException or VdfException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Oyun sürümünü <c>Modules\Native\SubModule.xml</c>'den okur.
    /// </summary>
    /// <remarks>
    /// <b>Kasıtlı olarak <c>package_info.txt</c> kullanılmıyor.</b> O dosyadaki
    /// <c>Environment: PC@…</c> alanı güncellenmiyor ve gerçek sürümden geride kalıyor;
    /// ona bakan bir kontrol, uyumlu kurulumu uyumsuz sanır.
    /// </remarks>
    public static string? ReadGameVersion(string installDirectory)
    {
        var path = Path.Combine(installDirectory, "Modules", "Native", "SubModule.xml");
        return ReadSubModuleVersion(path);
    }

    private static string? ReadSubModuleVersion(string subModuleXmlPath)
    {
        try
        {
            if (!File.Exists(subModuleXmlPath)) return null;

            var doc = XDocument.Load(subModuleXmlPath);
            return doc.Root?.Element("Version")?.Attribute("value")?.Value?.Trim();
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Coop modu
    // ------------------------------------------------------------------

    /// <summary>
    /// Workshop içeriğinde dedicated server barındıran tüm Coop kurulumlarını bulur.
    /// </summary>
    public static IReadOnlyList<CoopInstallation> FindCoopInstallations()
    {
        var found = new List<CoopInstallation>();

        foreach (var library in FindSteamLibraries())
        {
            var workshopContent = Path.Combine(library, "steamapps", "workshop", "content", BannerlordAppId);
            if (!Directory.Exists(workshopContent)) continue;

            foreach (var itemDir in Directory.EnumerateDirectories(workshopContent))
            {
                var exe = Path.Combine(itemDir, "DedicatedServer", ServerExeName);
                if (!File.Exists(exe)) continue;

                found.Add(new CoopInstallation
                {
                    ModRoot = itemDir,
                    DedicatedServerExe = exe,
                    WorkshopItemId = Path.GetFileName(itemDir),
                    ModuleId = ReadSubModuleId(Path.Combine(itemDir, "SubModule.xml")) ?? "Coop",
                    ModVersion = ReadSubModuleVersion(Path.Combine(itemDir, "SubModule.xml")),
                    RequiredGameVersion = ReadRequiredGameVersion(Path.Combine(itemDir, "SubModule.xml")),
                });
            }
        }

        return found;
    }

    /// <summary>Bannerlord'un Steam Atölyesi klasörleri (her kütüphanede bir tane olabilir).</summary>
    public static IReadOnlyList<string> FindWorkshopDirectories() =>
        FindSteamLibraries()
            .Select(l => Path.Combine(l, "steamapps", "workshop", "content", BannerlordAppId))
            .Where(Directory.Exists)
            .ToList();

    private static string? ReadSubModuleId(string subModuleXmlPath)
    {
        try
        {
            return File.Exists(subModuleXmlPath) ? XDocument.Load(subModuleXmlPath).Root?.Element("Id")?.Attribute("value")?.Value : null;
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// En son güncellenmiş Coop kurulumu: Steam Atölyesi'nden, yoksa oyunun <c>Modules\Coop</c>
    /// klasöründen (Epic ya da elle kurulum). Elle kurulumda sunucu paketi olmayabilir; o zaman
    /// oyuncu katılabilir ama sunucu açamaz (kurulum denetimi söyler).
    /// </summary>
    public static CoopInstallation? FindCoop(string? gameDirectory = null)
    {
        var workshop = FindCoopInstallations()
            .OrderByDescending(c => Directory.GetLastWriteTimeUtc(c.ModRoot))
            .FirstOrDefault();
        if (workshop is not null || gameDirectory is null) return workshop;

        foreach (var id in new[] { "Coop", "CoopNightly" })
        {
            var root = Path.Combine(gameDirectory, "Modules", id);
            var subModule = Path.Combine(root, "SubModule.xml");
            if (!File.Exists(subModule)) continue;

            return new CoopInstallation
            {
                ModRoot = root,
                DedicatedServerExe = Path.Combine(root, "DedicatedServer", ServerExeName),
                ModuleId = ReadSubModuleId(subModule) ?? id,
                ModVersion = ReadSubModuleVersion(subModule),
                RequiredGameVersion = ReadRequiredGameVersion(subModule),
            };
        }

        return null;
    }

    /// <summary>
    /// Modun bağımlı olduğu oyun sürümünü <c>DependedModule</c> girdilerinden okur.
    /// </summary>
    private static string? ReadRequiredGameVersion(string subModuleXmlPath)
    {
        try
        {
            if (!File.Exists(subModuleXmlPath)) return null;

            var doc = XDocument.Load(subModuleXmlPath);

            return doc.Root?
                .Element("DependedModules")?
                .Elements("DependedModule")
                .Select(e => e.Attribute("DependentVersion")?.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?
                .Trim();
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
