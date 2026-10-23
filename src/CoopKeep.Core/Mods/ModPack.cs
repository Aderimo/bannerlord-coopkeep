using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopKeep.Core.Mods;

/// <summary>Bir moddan nasıl edinileceği.</summary>
public enum ModPackSource
{
    /// <summary>Oyunun kendi modülü; herkeste var.</summary>
    Official,

    /// <summary>Steam Atölyesi: abone olunarak iner.</summary>
    Workshop,

    /// <summary>Elle kurulmuş (Nexus, GitHub…): pakete eklenmişse paketten kurulur.</summary>
    Local,
}

/// <summary>Paketteki tek mod.</summary>
/// <param name="Bundled">Modun dosyaları paketin içinde mi (zip'te <c>Modules\&lt;Id&gt;</c>)?</param>
public sealed record ModPackEntry(
    string Id,
    string Name,
    string? Version,
    ModPackSource Source,
    string? WorkshopId = null,
    string? NexusId = null,
    bool Bundled = false);

/// <summary>
/// Mod paketi: bir arada çalışan modlar ve yüklenme sıraları. Arkadaşlar tek dosyayla aynı
/// mod düzenini kurar; sunucu da aynı listeyle açılır.
/// </summary>
public sealed record ModPack
{
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Paketin hazırlandığı oyun sürümü (ör. <c>v1.4.8</c>).</summary>
    public string? GameVersion { get; init; }

    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Modlar, yüklenme sırasıyla. CoopKeep ve resmî modüller dahil.</summary>
    public IReadOnlyList<ModPackEntry> Mods { get; init; } = [];

    /// <summary>Davet dosyasıysa sunucunun bilgileri; düz pakette <c>null</c>.</summary>
    public ModPackInvite? Invite { get; init; }
}

/// <summary>
/// Tek dosyalık davet: sunucuyu açanın gönderdiği paket, modların yanında nasıl katılınacağını
/// da taşır. Arkadaş içe aktarınca "Paketi uygula ve katıl" tek tıkla hazır gelir.
/// </summary>
/// <param name="HostSteamName">Steam'de aranacak ad (sunucuyu açanın Steam adı).</param>
/// <param name="Address">Doğrudan bağlantı adresi (IP:port).</param>
/// <param name="Password">Sunucu şifresi (sunucuyu açan eklemeyi seçtiyse).</param>
/// <param name="World">Dünyanın adı.</param>
/// <param name="CoopVersion">Sunucudaki Coop sürümü: oyuncununki aynı olmalı.</param>
public sealed record ModPackInvite(string? HostSteamName, string? Address, string? Password, string? World, string? CoopVersion);

/// <summary>Paketteki bir modun bu bilgisayardaki durumu.</summary>
public enum ModPackItemState
{
    /// <summary>Kurulu, sürüm aynı.</summary>
    Ready,

    /// <summary>Kurulu ama sürüm farklı.</summary>
    WrongVersion,

    /// <summary>Kurulu değil; Atölye'den abone olunabilir.</summary>
    MissingWorkshop,

    /// <summary>Kurulu değil; paketin içinde var, kurulabilir.</summary>
    MissingBundled,

    /// <summary>Kurulu değil; elle indirilmeli (Nexus vb.).</summary>
    MissingManual,
}

public sealed record ModPackItemStatus(ModPackEntry Entry, ModPackItemState State, string? InstalledVersion);

/// <summary>Paket dosyaları: kaydetme, açma, zip'e aktarma ve bu bilgisayarla karşılaştırma.</summary>
/// <remarks>
/// <para>
/// İki biçim: yalnızca liste (<c>.ckpack</c>, JSON) ve kurulum paketi (<c>.zip</c>: liste,
/// <c>NASIL KURULUR.txt</c> ve seçilen modların klasörleri). Atölye modları varsayılan olarak
/// pakete konmaz; arkadaşlar abone olur. Steam'i olmayan arkadaşlar için istenirse eklenir.
/// </para>
/// </remarks>
public static class ModPackFile
{
    public const string ListExtension = ".ckpack";
    public const string ManifestName = "coopkeep-pack.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ModPack pack) => JsonSerializer.Serialize(pack, Json);

    public static ModPack Deserialize(string json) =>
        JsonSerializer.Deserialize<ModPack>(json, Json) ?? throw new InvalidDataException("Empty mod pack.");

    public static void SaveList(ModPack pack, string path) => File.WriteAllText(path, Serialize(pack));

    public static ModPack LoadList(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>Seçili modlardan paket oluşturur.</summary>
    /// <param name="orderedIds">Açık modlar, yüklenme sırasıyla.</param>
    public static ModPack Create(string name, IReadOnlyList<string> orderedIds, IReadOnlyList<InstalledModule> installed,
        string? gameVersion, IReadOnlyDictionary<string, string>? nexusIds = null)
    {
        var byId = installed.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var entries = orderedIds.Select(id =>
        {
            if (!byId.TryGetValue(id, out var m)) return new ModPackEntry(id, id, null, ModPackSource.Local);
            var source = m.IsOfficial ? ModPackSource.Official : m.Source == ModuleSource.Workshop ? ModPackSource.Workshop : ModPackSource.Local;
            return new ModPackEntry(m.Id, m.Name, m.Version, source, m.WorkshopId,
                nexusIds is not null && nexusIds.TryGetValue(m.Id, out var nexus) ? nexus : null);
        }).ToList();

        return new ModPack { Name = name, GameVersion = gameVersion, Mods = entries };
    }

    /// <summary>Paketi bu bilgisayardaki modlarla karşılaştırır.</summary>
    public static IReadOnlyList<ModPackItemStatus> Compare(ModPack pack, IReadOnlyList<InstalledModule> installed)
    {
        var byId = installed.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return pack.Mods.Select(e =>
        {
            if (byId.TryGetValue(e.Id, out var m))
            {
                var same = string.IsNullOrEmpty(e.Version) || string.Equals(e.Version, m.Version, StringComparison.OrdinalIgnoreCase) || e.Source == ModPackSource.Official;
                return new ModPackItemStatus(e, same ? ModPackItemState.Ready : ModPackItemState.WrongVersion, m.Version);
            }

            var state = e.Bundled ? ModPackItemState.MissingBundled
                : e.WorkshopId is { Length: > 0 } ? ModPackItemState.MissingWorkshop
                : ModPackItemState.MissingManual;
            return new ModPackItemStatus(e, state, null);
        }).ToList();
    }

    /// <summary>Paketin başlatıcıya yazılacak listesi: paketteki modlar açık ve sıralı.</summary>
    public static IReadOnlyList<LauncherMod> LauncherList(ModPack pack, IReadOnlyList<LauncherMod> current) =>
        LauncherList(pack, current, AlwaysOn);

    /// <summary>
    /// Paket uygulanınca da açık kalanlar: oyunun zorunlu modülleri ve Coop. Paket (ör. bir
    /// dünyanın mod paketi) bunları içermese de oyun ve Coop çalışmaya devam etmeli.
    /// </summary>
    public static readonly IReadOnlySet<string> AlwaysOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle", "Coop",
    };

    /// <param name="keepOn">Pakette olmasa da açık kalacaklar (bilgisayarda varsa).</param>
    public static IReadOnlyList<LauncherMod> LauncherList(ModPack pack, IReadOnlyList<LauncherMod> current, IReadOnlySet<string> keepOn)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var on = pack.Mods.Select(m => m.Id)
            .Concat(current.Where(c => keepOn.Contains(c.Id)).Select(c => c.Id))
            .Distinct(comparer)
            .ToList();

        // Açık olanlar: oyunun modülleri kendi sırasıyla, sonra paketin modları (paketin sırasıyla),
        // en sonda co-op modu. Kapalı olanlar arkada, eski sıralarıyla.
        var official = LoadOrder.Official.Where(o => on.Contains(o, comparer));
        var last = LoadOrder.LoadLast.Where(o => on.Contains(o, comparer));
        var middle = on.Where(id => !LoadOrder.Official.Contains(id, comparer) && !LoadOrder.LoadLast.Contains(id, comparer));

        var list = official.Concat(middle).Concat(last).Select(id => new LauncherMod(id, true)).ToList();
        list.AddRange(current.Where(c => !on.Contains(c.Id, comparer)).Select(c => c with { IsSelected = false }));
        return list;
    }

    /// <summary>
    /// Kurulum paketi (zip) hazırlar: liste, yönerge ve elle kurulmuş modların klasörleri.
    /// </summary>
    /// <param name="includeWorkshop">Atölye modlarının dosyaları da eklensin (Steam'i olmayanlar için).</param>
    public static ModPack ExportZip(ModPack pack, IReadOnlyList<InstalledModule> installed, string zipPath, bool includeWorkshop)
    {
        var byId = installed.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var entries = pack.Mods.Select(e =>
        {
            var bundle = e.Source == ModPackSource.Local || (includeWorkshop && e.Source == ModPackSource.Workshop);
            return e with { Bundled = bundle && byId.ContainsKey(e.Id) };
        }).ToList();
        var final = pack with { Mods = entries };

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteText(zip, ManifestName, Serialize(final));
            WriteText(zip, "NASIL KURULUR.txt", Instructions(final, turkish: true));
            WriteText(zip, "HOW TO INSTALL.txt", Instructions(final, turkish: false));

            foreach (var e in entries.Where(e => e.Bundled))
            {
                var folder = byId[e.Id].Folder;
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, $"Modules/{e.Id}/{relative}", CompressionLevel.Optimal);
                }
            }
        }

        return final;
    }

    /// <summary>Paketi açar: <c>.ckpack</c> ya da zip. Zip'teki mod klasörleri geçici klasöre çıkarılır.</summary>
    /// <returns>Paket ve (zip ise) mod klasörlerinin bulunduğu geçici klasör.</returns>
    public static (ModPack Pack, string? ModulesFolder) Open(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return (LoadList(path), null);

        var temp = Path.Combine(Path.GetTempPath(), "coopkeep-pack-" + Guid.NewGuid().ToString("N"));
        ModArchiveInstaller.ExtractSafely(path, temp);
        var manifest = Path.Combine(temp, ManifestName);
        if (!File.Exists(manifest)) throw new InvalidDataException("This zip is not a CoopKeep mod pack (no " + ManifestName + ").");

        var pack = LoadList(manifest);
        var modules = Path.Combine(temp, "Modules");
        return (pack, Directory.Exists(modules) ? modules : null);
    }

    /// <summary>Kurulum yönergesi (paketin içindeki metin dosyası).</summary>
    public static string Instructions(ModPack pack, bool turkish)
    {
        var sb = new StringBuilder();
        sb.AppendLine(turkish ? $"CoopKeep mod paketi: {pack.Name}" : $"CoopKeep mod pack: {pack.Name}");
        if (!string.IsNullOrEmpty(pack.GameVersion)) sb.AppendLine((turkish ? "Oyun sürümü: " : "Game version: ") + pack.GameVersion);
        sb.AppendLine();

        if (pack.Invite is { } invite)
        {
            sb.AppendLine(turkish ? "SUNUCU" : "SERVER");
            if (!string.IsNullOrEmpty(invite.World)) sb.AppendLine((turkish ? "  Dünya: " : "  World: ") + invite.World);
            if (!string.IsNullOrEmpty(invite.CoopVersion)) sb.AppendLine((turkish ? "  Coop sürümü (aynısı olmalı): " : "  Coop version (must match): ") + invite.CoopVersion);
            if (!string.IsNullOrEmpty(invite.HostSteamName)) sb.AppendLine((turkish ? "  Steam'de ara: " : "  Search on Steam: ") + invite.HostSteamName);
            if (!string.IsNullOrEmpty(invite.Address)) sb.AppendLine((turkish ? "  Doğrudan adres: " : "  Direct address: ") + invite.Address);
            if (!string.IsNullOrEmpty(invite.Password)) sb.AppendLine((turkish ? "  Şifre: " : "  Password: ") + invite.Password);
            sb.AppendLine();
        }

        sb.AppendLine(turkish ? "EN KOLAY YOL" : "EASIEST WAY");
        sb.AppendLine(turkish
            ? "CoopKeep programında Modlar > Paketler > \"Paketi içe aktar\" ile bu zip'i seç. Program eksikleri kurar ve sırayı ayarlar."
            : "In the CoopKeep app, open Mods > Packs > \"Import pack\" and pick this zip. The app installs what's missing and sets the order.");
        sb.AppendLine();

        sb.AppendLine(turkish ? "ELLE KURULUM" : "BY HAND");
        sb.AppendLine(turkish
            ? "1. Bu zip'teki Modules klasörünün içindekileri oyunun Modules klasörüne kopyala:"
            : "1. Copy everything inside this zip's Modules folder into the game's Modules folder:");
        sb.AppendLine(@"   ...\steamapps\common\Mount & Blade II Bannerlord\Modules");
        sb.AppendLine(turkish ? "2. Atölye modlarına Steam'den abone ol:" : "2. Subscribe to the Workshop mods on Steam:");
        foreach (var m in pack.Mods.Where(m => m is { Source: ModPackSource.Workshop, Bundled: false } && !string.IsNullOrEmpty(m.WorkshopId)))
            sb.AppendLine($"   - {m.Name}: https://steamcommunity.com/sharedfiles/filedetails/?id={m.WorkshopId}");
        foreach (var m in pack.Mods.Where(m => m is { Source: ModPackSource.Local, Bundled: false }))
            sb.AppendLine($"   - {m.Name} ({m.Id}): " + (m.NexusId is { Length: > 0 } n
                ? $"https://www.nexusmods.com/mountandblade2bannerlord/mods/{n}"
                : turkish ? "elle indir" : "download by hand"));
        sb.AppendLine(turkish ? "3. Oyun başlatıcısında şu modları bu sırayla aç:" : "3. In the game launcher, turn on these mods in this order:");
        var i = 1;
        foreach (var m in pack.Mods) sb.AppendLine($"   {i++,2}. {m.Name} ({m.Id}) {m.Version}");
        return sb.ToString();
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.Write(text);
    }
}
