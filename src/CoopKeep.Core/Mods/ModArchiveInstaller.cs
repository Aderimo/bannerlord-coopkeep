using System.IO.Compression;

namespace CoopKeep.Core.Mods;

/// <summary>
/// Zip ya da klasörden mod kurar: içindeki her <c>SubModule.xml</c>'li klasörü oyunun
/// <c>Modules</c> klasörüne kopyalar.
/// </summary>
/// <remarks>
/// <para>
/// Nexus ve benzeri sitelerden indirilen arşivler farklı biçimlerde gelir:
/// <c>Modules\Mod\SubModule.xml</c>, <c>Mod\SubModule.xml</c> ya da doğrudan
/// <c>SubModule.xml</c>. Hepsi tanınır. Arşivdeki yollar klasör dışına çıkamaz.
/// </para>
/// <para>
/// Var olan bir modun üstüne kurulurken eski klasör silinmez, yanına
/// <c>CoopKeep\removed-mods</c> altına taşınır; yanlışlıkla kurulan sürüm geri alınabilir.
/// </para>
/// </remarks>
public static class ModArchiveInstaller
{
    /// <summary>
    /// Hiçbir zip ya da paketten kurulmayan kimlikler: Coop, CoopKeep ve oyunun kendi modülleri.
    /// Oyun, Modules klasöründeki modu Atölye'dekine tercih ettiği için böyle bir klasör gerçek
    /// Coop'un (ya da oyunun) yerine geçebilirdi.
    /// </summary>
    public static bool IsProtected(string moduleId) =>
        moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
        || moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)
        || moduleId.Equals(LoadOrder.CoopKeepId, StringComparison.OrdinalIgnoreCase)
        || LoadOrder.Official.Contains(moduleId, StringComparer.OrdinalIgnoreCase)
        || moduleId.Equals(ModPlanner.ServerModule, StringComparison.OrdinalIgnoreCase);

    /// <summary>Zip'teki modları kurar.</summary>
    /// <param name="workshopVersions">
    /// Atölye'den kurulu modlar (kimlik → sürüm). Aynı sürüm zaten Atölye'deyse kopyası kurulmaz
    /// (Modules'e girerse Atölye güncellemelerinin önüne geçerdi). Sürüm farklıysa kurulur:
    /// kullanıcı Atölye'dekinden başka bir sürümü (ör. arkadaşının sürümünü) elle indirmiştir.
    /// </param>
    /// <param name="skipped">Kurulmayanlar (korunan ya da Atölye'de aynı sürümü olan kimlikler).</param>
    /// <returns>Kurulan modlar (Id, klasör).</returns>
    public static IReadOnlyList<InstalledModule> InstallZip(string zipPath, string gameModulesDirectory, string backupDirectory,
        IReadOnlyDictionary<string, string?>? workshopVersions = null, List<string>? skipped = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "coopkeep-mod-" + Guid.NewGuid().ToString("N"));
        try
        {
            ExtractSafely(zipPath, temp);
            return InstallFolder(temp, gameModulesDirectory, backupDirectory, workshopVersions, skipped);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Klasördeki (ya da alt klasörlerindeki) modları kurar.</summary>
    /// <inheritdoc cref="InstallZip" path="/param[@name='workshopVersions']"/>
    public static IReadOnlyList<InstalledModule> InstallFolder(string sourceFolder, string gameModulesDirectory, string backupDirectory,
        IReadOnlyDictionary<string, string?>? workshopVersions = null, List<string>? skipped = null)
    {
        var modules = FindModuleFolders(sourceFolder);
        if (modules.Count == 0) throw new InvalidDataException("No Bannerlord module (SubModule.xml) was found in the archive.");

        Directory.CreateDirectory(gameModulesDirectory);
        var result = new List<InstalledModule>();

        foreach (var folder in modules)
        {
            var module = ModuleScanner.TryRead(folder, ModuleSource.GameFolder)
                         ?? throw new InvalidDataException("Unreadable SubModule.xml in " + Path.GetFileName(folder));
            if (module.IsOfficial) continue; // oyunun kendi modüllerine dokunulmaz

            // Coop/CoopKeep/oyunun modülü kılığındaki klasör ya da Atölye'de aynı sürümüyle olan
            // bir mod: Modules'e kurulursa oyun onu tercih eder ve gerçeğinin yerine geçer.
            if (IsProtected(module.Id) || SameAsWorkshop(module, workshopVersions))
            {
                skipped?.Add(module.Id);
                continue;
            }

            var name = SafeFolderName(module.Id);
            var target = Path.Combine(gameModulesDirectory, name);
            if (Directory.Exists(target))
            {
                Directory.CreateDirectory(backupDirectory);
                var backup = Path.Combine(backupDirectory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.Move(target, backup);
            }

            CopyDirectory(folder, target);
            result.Add(module with { Folder = target, Source = ModuleSource.GameFolder });
        }

        return result;
    }

    private static bool SameAsWorkshop(InstalledModule module, IReadOnlyDictionary<string, string?>? workshopVersions) =>
        workshopVersions is not null && workshopVersions.TryGetValue(module.Id, out var version) &&
        string.Equals(version, module.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>Oyunun Modules klasöründeki bir modu kaldırır (yedek klasörüne taşır).</summary>
    public static string Remove(InstalledModule module, string backupDirectory)
    {
        if (module.Source != ModuleSource.GameFolder || module.IsOfficial)
            throw new InvalidOperationException("Only mods in the game's Modules folder can be removed here.");

        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, $"{SafeFolderName(module.Id)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.Move(module.Folder, backup);
        return backup;
    }

    /// <summary><c>SubModule.xml</c> içeren klasörler (iç içe modlarda en üsttekiler).</summary>
    public static IReadOnlyList<string> FindModuleFolders(string root)
    {
        var result = new List<string>();
        void Walk(string dir, int depth)
        {
            if (File.Exists(Path.Combine(dir, "SubModule.xml"))) { result.Add(dir); return; }
            if (depth >= 4) return;
            foreach (var sub in Directory.GetDirectories(dir)) Walk(sub, depth + 1);
        }

        Walk(root, 0);
        return result;
    }

    /// <summary>Zip'i açar; klasörün dışına yazmaya çalışan girdiler reddedilir.</summary>
    public static void ExtractSafely(string zipPath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The archive contains an unsafe path: " + entry.FullName);

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    internal static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static string SafeFolderName(string id)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(id.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return clean.Length == 0 ? "Mod" : clean;
    }
}
