using System.Text.Json;
using System.Xml.Linq;

namespace CoopKeep.Core.Mods;

/// <summary>Dağıtım sonucu.</summary>
/// <param name="Installed">Yeni kurulan ya da güncellenen modlar.</param>
/// <param name="Removed">Artık seçili olmadığı için kaldırılanlar.</param>
public sealed record DeployResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Removed);

/// <summary>
/// Planın modlarını özel sunucunun modül klasörüne (<c>engine\Modules</c>) koyar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Güvenlik kuralları:</b>
/// </para>
/// <list type="bullet">
/// <item>Yalnızca CoopKeep'in kendi kurduğu klasörlere dokunulur; kayıt defteri
/// <c>engine\Modules\.coopkeep-mods.json</c>. Coop'un ve resmî modüllerin klasörleri hiç değişmez
/// (Coop SHA-256 ile doğrulanıyor).</item>
/// <item>Bağlantı (junction) değil <b>kopya</b>: klasör Coop'un Atölye öğesinin içinde ve
/// Steam o öğeyi güncellerken bağlantıyı izleyip kullanıcının mod dosyalarını silebilirdi.</item>
/// <item>Yalnızca sunucunun okuduğu kısımlar kopyalanır (<c>SubModule.xml</c>, <c>ModuleData</c>,
/// <c>SceneObj</c>, gerekirse derlemeler). Görsel/ses paketleri özel sunucuda yüklenmiyor.</item>
/// </list>
/// </remarks>
public sealed class ServerModDeployer(string engineModulesDirectory)
{
    public const string RegistryFileName = ".coopkeep-mods.json";

    /// <summary>CoopKeep'in kurduğu her klasördeki işaret: kayıt defteri bozulsa da sahiplik bilinir.</summary>
    public const string MarkerFileName = ".coopkeep-mod";

    /// <summary>Sunucuya kopyalanan veri klasörleri.</summary>
    private static readonly string[] DataFolders = ["ModuleData", "SceneObj"];

    public string ModulesDirectory { get; } = Path.GetFullPath(engineModulesDirectory);

    private string RegistryPath => Path.Combine(ModulesDirectory, RegistryFileName);

    private sealed record Entry(string Source, string Mode, string? Version, long Stamp);

    /// <summary>
    /// Planı uygular. Seçimden çıkan modlar kaldırılır, değişmeyenlere dokunulmaz.
    /// </summary>
    /// <exception cref="InvalidOperationException">Hedef klasör CoopKeep'e ait değilse (ör. Coop'un kendisi).</exception>
    public DeployResult Deploy(IReadOnlyList<PlannedMod> mods)
    {
        var registry = LoadRegistry();
        var wanted = mods.ToDictionary(m => m.Module.Id, StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        var installed = new List<string>();

        // 1) Artık istenmeyenleri kaldır (kayıt defterinde olan ya da işaret dosyası taşıyan).
        var owned = registry.Keys
            .Concat(Directory.Exists(ModulesDirectory)
                ? Directory.EnumerateDirectories(ModulesDirectory)
                    .Where(d => File.Exists(Path.Combine(d, MarkerFileName)))
                    .Select(Path.GetFileName)
                    .OfType<string>()
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in owned)
        {
            if (wanted.ContainsKey(id)) continue;
            RemoveFolder(Path.Combine(ModulesDirectory, id));
            registry.Remove(id);
            removed.Add(id);
        }

        // 2) Yenileri kur, değişenleri yenile.
        foreach (var plan in mods)
        {
            var id = plan.Module.Id;
            var target = Path.Combine(ModulesDirectory, id);
            var entry = new Entry(plan.Module.Folder, plan.Mode.ToString(), plan.Module.Version, Stamp(plan.Module.Folder));

            if (registry.TryGetValue(id, out var existing) && existing == entry && Directory.Exists(target))
                continue;

            if (!registry.ContainsKey(id) && Directory.Exists(target) && !File.Exists(Path.Combine(target, MarkerFileName)))
                throw new InvalidOperationException(
                    $"'{id}' sunucunun modül klasöründe zaten var ve CoopKeep'e ait değil; dokunulmadı: {target}");

            if (Directory.Exists(target)) RemoveFolder(target);
            Install(plan, target);
            registry[id] = entry;
            installed.Add(id);
            SaveRegistry(registry); // her moddan sonra: yarıda kalırsa bile kayıt doğru kalsın
        }

        SaveRegistry(registry);
        return new DeployResult(installed, removed);
    }

    /// <summary>CoopKeep'in kurduğu bütün modları kaldırır (sunucu modsuz açılmadan önce).</summary>
    public IReadOnlyList<string> RemoveAll() => Deploy([]).Removed;

    /// <summary>CoopKeep'in şu an sunucuya kurmuş olduğu modlar.</summary>
    public IReadOnlyCollection<string> Deployed => LoadRegistry().Keys;

    private static void Install(PlannedMod plan, string target)
    {
        var source = plan.Module.Folder;
        Directory.CreateDirectory(target);

        // SubModule.xml: kipe göre kod/veri girişleri ayıklanır.
        var doc = XDocument.Load(Path.Combine(source, "SubModule.xml"));
        StripForMode(doc, plan.Mode);
        doc.Save(Path.Combine(target, "SubModule.xml"));
        File.WriteAllLines(Path.Combine(target, MarkerFileName), ["CoopKeep", source, plan.Mode.ToString()]);

        if (plan.Mode == ModInstallMode.IdOnly) return;

        foreach (var folder in DataFolders)
        {
            var from = Path.Combine(source, folder);
            if (Directory.Exists(from)) CopyDirectory(from, Path.Combine(target, folder));
        }

        var bin = plan.Mode switch
        {
            ModInstallMode.Full when plan.Module.HasServerBinaries => ModuleScanner.ServerBin,
            // Deneysel: .NET Core derlemesi varsa o (sunucu motoru .NET Core), yoksa Steam istemcisininki.
            ModInstallMode.RunCode when plan.Module.HasDotNetCoreBinaries => ModuleScanner.DotNetCoreBin,
            ModInstallMode.RunCode when plan.Module.HasClientBinaries => ModuleScanner.ClientBin,
            _ => null,
        };

        if (bin is not null)
            CopyDirectory(Path.Combine(source, "bin", bin), Path.Combine(target, "bin", ModuleScanner.ServerBin));
    }

    /// <summary>
    /// Kipe göre <c>SubModule.xml</c>'i sadeleştirir: veri kipinde kod (<c>SubModules</c>)
    /// çıkar, kimlik kipinde veri listesi (<c>Xmls</c>) de çıkar. Sunucuda olmayan resmî
    /// modüllere (StoryMode, CustomBattle) bağımlılık da silinir.
    /// </summary>
    public static void StripForMode(XDocument doc, ModInstallMode mode)
    {
        var root = doc.Root ?? throw new InvalidOperationException("SubModule.xml boş.");

        if (mode is ModInstallMode.DataOnly or ModInstallMode.IdOnly)
            root.Element("SubModules")?.Remove();

        if (mode == ModInstallMode.IdOnly)
            root.Element("Xmls")?.Remove();

        string[] notOnServer = ["StoryMode", "CustomBattle"];
        if (mode != ModInstallMode.Full)
        {
            foreach (var d in root.Element("DependedModules")?.Elements("DependedModule").ToList() ?? [])
                if (notOnServer.Contains(d.Attribute("Id")?.Value, StringComparer.OrdinalIgnoreCase)) d.Remove();
        }
    }

    /// <summary>Kaynağın değişip değişmediğini anlamak için: <c>SubModule.xml</c>'in yazılma zamanı.</summary>
    private static long Stamp(string folder)
    {
        var file = Path.Combine(folder, "SubModule.xml");
        return File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            // Bağlantıları izleme: kaynakta bir junction varsa içine girilmez.
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// Klasörü siler; içinde bir bağlantı (junction) varsa yalnızca bağlantı kaldırılır,
    /// hedefine girilmez. Modül klasörünün dışına asla çıkılmaz.
    /// </summary>
    private void RemoveFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(ModulesDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Modül klasörünün dışı silinemez: " + full);
        if (!Directory.Exists(full)) return;

        DeleteTree(new DirectoryInfo(full));
    }

    private static void DeleteTree(DirectoryInfo dir)
    {
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            dir.Delete(); // yalnızca bağlantının kendisi
            return;
        }

        foreach (var file in dir.EnumerateFiles())
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }

        foreach (var sub in dir.EnumerateDirectories()) DeleteTree(sub);
        dir.Delete();
    }

    private Dictionary<string, Entry> LoadRegistry()
    {
        try
        {
            if (File.Exists(RegistryPath))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(RegistryPath))
                           is { } d ? new Dictionary<string, Entry>(d, StringComparer.OrdinalIgnoreCase) : [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Bozuk kayıt: boş say. Klasörler işaret dosyasıyla yeniden tanınır.
        }

        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveRegistry(Dictionary<string, Entry> registry)
    {
        if (registry.Count == 0)
        {
            if (File.Exists(RegistryPath)) File.Delete(RegistryPath);
            return;
        }

        var temp = RegistryPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(registry, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, RegistryPath, overwrite: true);
    }
}
