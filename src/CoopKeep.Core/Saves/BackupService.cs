using System.IO.Compression;

namespace CoopKeep.Core.Saves;

/// <summary>Yedek saklama kuralları.</summary>
/// <param name="MaxCount">Sunucu başına saklanacak en fazla yedek sayısı.</param>
/// <param name="MaxAgeDays">Bu günden eski yedekler silinir. 0 = yaş sınırı yok.</param>
/// <remarks>
/// <b>Hem sayı hem yaş</b> kullanılır. Yalnızca sayıya bakmak, yoğun bir oturumda
/// bir gecede üretilen yedeklerin bir haftalık geçmişi silmesine yol açar; yalnızca
/// yaşa bakmak ise diski büyütür.
/// </remarks>
public sealed record BackupPolicy(int MaxCount = 10, int MaxAgeDays = 30)
{
    public static BackupPolicy Default { get; } = new();
}

/// <summary>Diskteki tek bir yedek.</summary>
public sealed record BackupEntry(string FilePath, string SaveName, DateTime CreatedUtc, long SizeBytes)
{
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();
}

/// <summary>
/// Sunucu dünyalarının yedeklerini alır, saklar ve geri yükler.
/// </summary>
/// <remarks>
/// <para>
/// Yedekler <c>Game Saves\_CoopKeepBackups\&lt;ad&gt;\</c> altında zip olarak tutulur.
/// Coop'un kendi <c>.backup1</c>/<c>.backup2</c> dosyalarına <b>dokunulmaz</b> — üstelik
/// onlara güvenilemez: upstream #3341'e göre sunucu F10 ile durdurulunca üç dosya da
/// aynı içeriğe düşüyor.
/// </para>
/// <para>
/// Her yedek <c>.sav</c> ve eş <c>.json</c> dosyasını <b>birlikte</b> içerir; eksik
/// çiftli bir yedek alınmaz, çünkü tek başına <c>.sav</c> geri yüklemek oyuncu
/// eşlemelerini bozar.
/// </para>
/// </remarks>
public sealed class BackupService
{
    /// <summary>Yedek klasörü. Alt çizgi ile başlar ki save listesine karışmasın.</summary>
    public const string BackupFolderName = "_CoopKeepBackups";

    private readonly SaveRepository _saves;
    private readonly BackupPolicy _policy;

    public BackupService(string gameSavesDirectory, BackupPolicy? policy = null)
    {
        GameSavesDirectory = Path.GetFullPath(gameSavesDirectory);
        BackupRoot = Path.Combine(GameSavesDirectory, BackupFolderName);
        _saves = new SaveRepository(GameSavesDirectory);
        _policy = policy ?? BackupPolicy.Default;
    }

    public string GameSavesDirectory { get; }

    public string BackupRoot { get; }

    public string FolderFor(string saveName)
    {
        if (!SaveSet.IsValidSaveName(saveName))
            throw new ArgumentException($"Geçersiz sunucu adı: '{saveName}'", nameof(saveName));

        return Path.Combine(BackupRoot, saveName);
    }

    /// <summary>
    /// Verilen dünyanın anlık yedeğini alır.
    /// </summary>
    /// <remarks>
    /// Çağıran, bunu <b>doğrulanmış bir kayıt olayından sonra</b> tetiklemeli — yazma
    /// sırasında alınan bir yedek yarım dosya içerebilir.
    /// </remarks>
    public BackupEntry Create(string saveName)
    {
        var save = _saves.Find(saveName)
                   ?? throw new FileNotFoundException($"'{saveName}' adlı sunucu bulunamadı.");

        if (!save.IsComplete)
            throw new InvalidOperationException(
                $"'{saveName}' eksik: eş JSON dosyası yok. Yarım bir yedek geri yüklenemez.");

        var folder = FolderFor(saveName);
        Directory.CreateDirectory(folder);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(folder, $"{saveName}_{stamp}.zip");

        // Aynı saniyede ikinci yedek istenirse üzerine yazma.
        var n = 1;
        while (File.Exists(target))
            target = Path.Combine(folder, $"{saveName}_{stamp}-{n++}.zip");

        // Önce geçici dosyaya yaz: yazma yarıda kesilirse bozuk bir zip
        // yedek listesine girmesin.
        var temp = target + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(save.SavePath, Path.GetFileName(save.SavePath), CompressionLevel.Optimal);
                zip.CreateEntryFromFile(save.CompanionPath, Path.GetFileName(save.CompanionPath), CompressionLevel.Optimal);
            }

            File.Move(temp, target);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }

        Prune(saveName);

        var info = new FileInfo(target);
        return new BackupEntry(target, saveName, info.CreationTimeUtc, info.Length);
    }

    /// <summary>Bir dünyanın yedeklerini yeniden eskiye doğru listeler.</summary>
    public IReadOnlyList<BackupEntry> List(string saveName)
    {
        var folder = FolderFor(saveName);
        if (!Directory.Exists(folder)) return Array.Empty<BackupEntry>();

        return Directory.EnumerateFiles(folder, "*.zip")
            .Select(p => new FileInfo(p))
            .Select(f => new BackupEntry(f.FullName, saveName, f.LastWriteTimeUtc, f.Length))
            .OrderByDescending(b => b.CreatedUtc)
            .ToList();
    }

    /// <summary>
    /// Bir yedeği geri yükler. <b>Sunucu durdurulmuş olmalıdır.</b>
    /// </summary>
    /// <remarks>
    /// İşlem geri alınabilir şekilde yapılır:
    /// <list type="number">
    ///   <item>Zip'in her iki dosyayı da içerdiği doğrulanır.</item>
    ///   <item>Mevcut dünyanın yedeği alınır (yanlış yedeği seçtiyseniz kurtuluş yolu).</item>
    ///   <item>İçerik geçici klasöre açılır.</item>
    ///   <item>Mevcut dosyalar kenara alınır, yenileri yerine konur.</item>
    ///   <item>Herhangi bir hatada eski hâl geri konur.</item>
    /// </list>
    /// </remarks>
    public void Restore(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!File.Exists(entry.FilePath))
            throw new FileNotFoundException("Yedek dosyası bulunamadı.", entry.FilePath);

        var savFile = entry.SaveName + SaveSet.SaveExtension;
        var jsonFile = entry.SaveName + SaveSet.CompanionExtension;

        // 1) Doğrula
        using (var zip = ZipFile.OpenRead(entry.FilePath))
        {
            if (zip.GetEntry(savFile) is null || zip.GetEntry(jsonFile) is null)
                throw new InvalidDataException(
                    "Yedek eksik: .sav ve .json dosyalarının ikisi de bulunmalı.");
        }

        // 2) Mevcut hâlin güvenlik yedeği
        if (_saves.Exists(entry.SaveName))
        {
            try { Create(entry.SaveName); }
            catch (InvalidOperationException) { /* mevcut hâl zaten eksikse devam et */ }
        }

        var savePath = _saves.ResolveSavePath(entry.SaveName);
        var companionPath = _saves.ResolveCompanionPath(entry.SaveName);

        var staging = Path.Combine(Path.GetTempPath(), "coopkeep-restore-" + Guid.NewGuid().ToString("N"));
        var rollbackSav = savePath + ".rollback";
        var rollbackJson = companionPath + ".rollback";

        try
        {
            // 3) Geçiciye aç
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(entry.FilePath, staging);

            var stagedSav = Path.Combine(staging, savFile);
            var stagedJson = Path.Combine(staging, jsonFile);

            if (!File.Exists(stagedSav) || !File.Exists(stagedJson))
                throw new InvalidDataException("Yedek açılamadı: beklenen dosyalar çıkmadı.");

            // 4) Mevcut dosyaları kenara al
            if (File.Exists(savePath)) File.Move(savePath, rollbackSav, overwrite: true);
            if (File.Exists(companionPath)) File.Move(companionPath, rollbackJson, overwrite: true);

            File.Move(stagedSav, savePath, overwrite: true);
            File.Move(stagedJson, companionPath, overwrite: true);

            // Başarılı: geri alma kopyalarını temizle
            if (File.Exists(rollbackSav)) File.Delete(rollbackSav);
            if (File.Exists(rollbackJson)) File.Delete(rollbackJson);
        }
        catch
        {
            // 5) Geri al
            TryRollback(rollbackSav, savePath);
            TryRollback(rollbackJson, companionPath);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        static void TryRollback(string from, string to)
        {
            try { if (File.Exists(from)) File.Move(from, to, overwrite: true); }
            catch (IOException) { /* elde ne varsa o kalsın */ }
        }
    }

    /// <summary>
    /// Bir dünyanın <b>tüm</b> yedeklerini ve yedek klasörünü siler.
    /// </summary>
    /// <remarks>
    /// Dünya silindiğinde çağrılır. Aksi hâlde yedekler diskte kalır ve kullanıcı
    /// "sildim ama yer boşalmadı" der — her yedek birkaç megabayt.
    /// </remarks>
    /// <returns>Silinen yedek sayısı.</returns>
    public int DeleteAll(string saveName)
    {
        var folder = FolderFor(saveName);
        if (!Directory.Exists(folder)) return 0;

        var count = List(saveName).Count;

        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { return 0; }

        return count;
    }

    /// <summary>Bir dünyanın yedeklerinin diskte kapladığı toplam alan.</summary>
    public long TotalSizeOnDisk(string saveName) => List(saveName).Sum(b => b.SizeBytes);

    /// <summary>Kurala uymayan yedekleri siler ve silinen sayısını döndürür.</summary>
    public int Prune(string saveName)
    {
        var all = List(saveName);
        if (all.Count == 0) return 0;

        var doomed = new List<BackupEntry>();

        if (_policy.MaxAgeDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_policy.MaxAgeDays);
            doomed.AddRange(all.Where(b => b.CreatedUtc < cutoff));
        }

        if (_policy.MaxCount > 0 && all.Count > _policy.MaxCount)
            doomed.AddRange(all.Skip(_policy.MaxCount));

        var removed = 0;
        foreach (var b in doomed.DistinctBy(b => b.FilePath))
        {
            try { File.Delete(b.FilePath); removed++; }
            catch (IOException) { /* kilitliyse bir dahaki sefere */ }
        }

        return removed;
    }
}
