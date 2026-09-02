using System.Text.Json;

namespace CoopKeep.Core.Saves;

/// <summary>
/// <c>Game Saves</c> dizinindeki kampanyaları keşfeder ve üzerlerinde dosya işlemleri yapar.
/// </summary>
/// <remarks>
/// Tüm işlemler <see cref="SaveSet"/> üzerinden, yani <c>.sav</c>+<c>.json</c> çifti
/// birlikte taşınır. Yollar her zaman kök dizin altında olacak şekilde doğrulanır.
/// </remarks>
public sealed class SaveRepository(string gameSavesDirectory)
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Katılan istemcilere gönderilen anlık görüntü. Bir kampanya değil, listede gösterilmemeli.
    /// </summary>
    private const string TransferSnapshotName = "TransferSave";

    public string Directory { get; } = Path.GetFullPath(gameSavesDirectory);

    /// <summary>
    /// Dizindeki tüm save'leri, eş JSON'larından okunan oyuncu listeleriyle birlikte döndürür.
    /// Server çalışmıyorken de çalışır.
    /// </summary>
    public IReadOnlyList<SaveSet> Discover()
    {
        if (!System.IO.Directory.Exists(Directory))
            return Array.Empty<SaveSet>();

        var results = new List<SaveSet>();

        foreach (var savePath in System.IO.Directory.EnumerateFiles(Directory, "*" + SaveSet.SaveExtension))
        {
            var name = Path.GetFileNameWithoutExtension(savePath);
            if (name.Equals(TransferSnapshotName, StringComparison.OrdinalIgnoreCase))
                continue;

            var companionPath = Path.Combine(Directory, name + SaveSet.CompanionExtension);
            var info = new FileInfo(savePath);
            var companionExists = File.Exists(companionPath);

            results.Add(new SaveSet
            {
                Name = name,
                SavePath = savePath,
                CompanionPath = companionPath,
                SizeBytes = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc,
                IsCoopBackup = LooksLikeCoopBackup(name),
                IsComplete = companionExists,
                Players = companionExists ? ReadPlayers(companionPath) : Array.Empty<CoopSessionPlayer>(),
            });
        }

        return results
            .OrderByDescending(s => s.LastModifiedUtc)
            .ToList();
    }

    /// <summary>Sunulabilir kampanyalar: Coop yedekleri ve şablon hariç.</summary>
    public IReadOnlyList<SaveSet> DiscoverCampaigns() =>
        Discover().Where(s => !s.IsCoopBackup && !s.IsTemplate).ToList();

    public SaveSet? Find(string name) =>
        Discover().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool Exists(string name) =>
        SaveSet.IsValidSaveName(name) && File.Exists(ResolveSavePath(name));

    /// <summary>
    /// Eş JSON'dan oyuncu listesini okur. Dosya bozuksa boş liste döner — bozuk bir
    /// yan dosya yüzünden tüm save listesi çökmemeli.
    /// </summary>
    public static IReadOnlyList<CoopSessionPlayer> ReadPlayers(string companionPath)
    {
        try
        {
            using var stream = File.Open(companionPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var session = JsonSerializer.Deserialize<CoopSession>(stream, SessionJsonOptions);
            return session?.Players ?? Array.Empty<CoopSessionPlayer>();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<CoopSessionPlayer>();
        }
    }

    // ------------------------------------------------------------------
    // Dosya işlemleri — hepsi çifti birlikte taşır
    // ------------------------------------------------------------------

    /// <summary>Save'i yeni bir adla kopyalar.</summary>
    public SaveSet Duplicate(string sourceName, string targetName)
    {
        var source = RequireExisting(sourceName);
        var targetSave = ResolveSavePath(targetName);
        var targetCompanion = ResolveCompanionPath(targetName);

        if (File.Exists(targetSave))
            throw new InvalidOperationException($"'{targetName}' adlı bir save zaten var.");

        File.Copy(source.SavePath, targetSave);
        if (source.IsComplete)
            File.Copy(source.CompanionPath, targetCompanion);

        return Find(targetName)!;
    }

    /// <summary>Save'i yeniden adlandırır. Çift birlikte taşınır.</summary>
    public SaveSet Rename(string oldName, string newName)
    {
        var source = RequireExisting(oldName);
        var targetSave = ResolveSavePath(newName);
        var targetCompanion = ResolveCompanionPath(newName);

        if (File.Exists(targetSave))
            throw new InvalidOperationException($"'{newName}' adlı bir save zaten var.");

        File.Move(source.SavePath, targetSave);
        if (source.IsComplete)
            File.Move(source.CompanionPath, targetCompanion);

        return Find(newName)!;
    }

    /// <summary>
    /// Save'i ve ona ait Coop yedek kuşaklarını siler.
    /// </summary>
    /// <param name="name">Silinecek dünyanın adı.</param>
    /// <param name="includeCoopBackups">
    /// Sunucunun kendi ürettiği <c>&lt;ad&gt;.backup1</c> / <c>.backup2</c> kuşakları da
    /// silinsin mi? Varsayılan <see langword="true"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Çağıranın, save'in aktif server tarafından kullanılmadığını doğrulaması gerekir —
    /// bu sınıf server durumunu bilmez.
    /// </para>
    /// <para>
    /// Coop yedek kuşakları da siliniyor: her biri asıl save kadar yer kaplıyor
    /// (~5 MB × 2) ve dünya silindikten sonra hiçbir işe yaramıyor. Bunları bırakmak,
    /// kullanıcının "sildim ama yer hâlâ dolu" demesine yol açıyordu.
    /// </para>
    /// </remarks>
    /// <returns>Silinen dosya sayısı.</returns>
    public int Delete(string name, bool includeCoopBackups = true)
    {
        var target = RequireExisting(name);
        var removed = 0;

        removed += TryDelete(target.SavePath);
        removed += TryDelete(target.CompanionPath);

        if (includeCoopBackups)
        {
            foreach (var generation in CoopBackupGenerations(name))
            {
                removed += TryDelete(ResolveSavePath(generation));
                removed += TryDelete(ResolveCompanionPath(generation));
            }
        }

        return removed;
    }

    /// <summary>Bir dünyanın Coop tarafından üretilen yedek kuşaklarının adları.</summary>
    public static IEnumerable<string> CoopBackupGenerations(string name)
    {
        yield return name + ".backup1";
        yield return name + ".backup2";
    }

    /// <summary>
    /// Bir dünyanın diskte kapladığı toplam alan: kendisi, eş JSON'u ve Coop yedek kuşakları.
    /// </summary>
    public long TotalSizeOnDisk(string name)
    {
        long total = 0;

        foreach (var candidate in AllRelatedPaths(name))
        {
            if (File.Exists(candidate)) total += new FileInfo(candidate).Length;
        }

        return total;
    }

    private IEnumerable<string> AllRelatedPaths(string name)
    {
        yield return ResolveSavePath(name);
        yield return ResolveCompanionPath(name);

        foreach (var generation in CoopBackupGenerations(name))
        {
            yield return ResolveSavePath(generation);
            yield return ResolveCompanionPath(generation);
        }
    }

    private static int TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            File.Delete(path);
            return 1;
        }
        catch (IOException)
        {
            // Dosya kilitliyse (sunucu hâlâ açık olabilir) sessizce geç;
            // çağıran zaten sunucunun durduğunu varsayıyor.
            return 0;
        }
    }

    // ------------------------------------------------------------------
    // Yol çözümleme — path traversal koruması
    // ------------------------------------------------------------------

    public string ResolveSavePath(string name) => ResolveWithinRoot(name, SaveSet.SaveExtension);

    public string ResolveCompanionPath(string name) => ResolveWithinRoot(name, SaveSet.CompanionExtension);

    /// <summary>
    /// Save adını kök dizin altına kilitler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Doğrulama <b>ham ada</b> uygulanır, türetilmiş bir dosya adına değil. Bu ayrım
    /// önemli: <c>Path.GetFileNameWithoutExtension("alt/klasor.sav")</c> sessizce
    /// <c>"klasor"</c> döndürür ve dizin bileşenini yutar — böyle bir ad doğrulamayı
    /// atlatıp alt dizine yazabilirdi.
    /// </para>
    /// <para>
    /// Ardından sonuç yolun gerçekten kökün altında kaldığı ayrıca kontrol edilir
    /// (savunmada derinlik).
    /// </para>
    /// </remarks>
    private string ResolveWithinRoot(string name, string extension)
    {
        if (!SaveSet.IsValidSaveName(name))
            throw new ArgumentException($"Geçersiz save adı: '{name}'", nameof(name));

        var full = Path.GetFullPath(Path.Combine(Directory, name + extension));

        var root = Directory.EndsWith(Path.DirectorySeparatorChar)
            ? Directory
            : Directory + Path.DirectorySeparatorChar;

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Yol kök dizinin dışına çıkıyor: '{name}'", nameof(name));

        return full;
    }

    private SaveSet RequireExisting(string name) =>
        Find(name) ?? throw new FileNotFoundException($"'{name}' adlı save bulunamadı.");

    private static bool LooksLikeCoopBackup(string name) =>
        name.EndsWith(".backup1", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".backup2", StringComparison.OrdinalIgnoreCase);
}
