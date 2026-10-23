using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CoopKeep.Core.Images;

/// <summary>İstenen görselin türü.</summary>
public enum GameImageKind
{
    /// <summary>Asker portresi (ad: <c>Imperial Legionary</c>).</summary>
    Troop,

    /// <summary>Klan sancağı (ad: <c>Osticos</c>).</summary>
    ClanBanner,

    /// <summary>Lord portresi (ad: <c>Lucon</c>).</summary>
    Lord,

    /// <summary>Yerleşim görüntüsü (ad: <c>Epicrotea</c>).</summary>
    Settlement,

    /// <summary>Eşya (ad: <c>Desert Horse</c>).</summary>
    Item,

    /// <summary>Beceri ya da özellik ikonu (ad: oyundaki kimlik, <c>OneHanded</c>, <c>vigor</c>).</summary>
    Skill,
}

/// <summary>
/// Oyunun gerçek görsellerini topluluk wiki'lerinden indirip bilgisayarda saklar.
/// </summary>
/// <remarks>
/// <para>
/// Oyun asker, sancak ve eşya görsellerini diske PNG olarak yazmıyor (oyun içinde anlık
/// çiziliyor); harita dokusu da paketlenmiş biçimde. Bu yüzden görseller iki
/// MediaWiki sitesinden alınır:
/// </para>
/// <list type="bullet">
/// <item><b>mountandblade.fandom.com</b>: asker portreleri, klan sancakları, harita.</item>
/// <item><b>mountandblade2bannerlord.wiki.fextralife.com</b>: lord portreleri, yerleşim görüntüleri, klan logoları.</item>
/// </list>
/// <para>
/// Görseller TaleWorlds'ündür; CoopKeep bunları reposunda taşımaz, yalnızca kullanıcının
/// bilgisayarındaki önbelleğe indirir (<c>%LOCALAPPDATA%\CoopKeep\images</c>). Bulunamayan
/// görseller de not edilir, her açılışta yeniden sorulmaz.
/// </para>
/// </remarks>
public sealed class GameImageService : IDisposable
{
    public const string FandomApi = "https://mountandblade.fandom.com/api.php";
    public const string FextralifeApi = "https://mountandblade2bannerlord.wiki.fextralife.com/api.php";

    /// <summary>Oyun içi Calradia haritası (bkz. <see cref="Map.MapProjection"/>).</summary>
    public const string MapUrl =
        "https://static.wikia.nocookie.net/mountandblade/images/1/1d/Calradia_Bannerlord_Lite.png/revision/latest?cb=20241114220941&format=original";

    private static readonly TimeSpan MissRetry = TimeSpan.FromDays(7);

    private readonly HttpClient _http;
    private readonly string _root;
    private readonly ConcurrentDictionary<string, Task<string?>> _inflight = new();
    private readonly SemaphoreSlim _gate = new(4);
    private readonly object _indexLock = new();
    private Dictionary<string, IndexEntry> _index;
    private Task<IReadOnlyDictionary<string, string>>? _fextralifeFiles;

    private sealed record IndexEntry(string? File, DateTimeOffset At);

    /// <summary>
    /// Önce bakılan hazır görsel klasörleri: görselli indirme paketi (exe'nin yanındaki
    /// <c>images</c>) ve oyundan çıkarılan görseller. Düzen: <c>&lt;tür&gt;\&lt;ad&gt;.png</c>.
    /// </summary>
    private readonly IReadOnlyList<string> _packDirectories;

    public GameImageService(string cacheDirectory, HttpClient? http = null, IEnumerable<string>? packDirectories = null)
    {
        _root = cacheDirectory;
        // Oyundan çıkarılan görseller ("game" alt klasörü) wiki'dekilerin önüne geçer.
        var roots = (packDirectories ?? []).Append(cacheDirectory).Distinct().ToList();
        _packDirectories = roots.Select(r => Path.Combine(r, "game")).Concat(roots).ToList();
        _http = http ?? Net.FastHttp.Create(TimeSpan.FromSeconds(20));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CoopKeep/0.4 (+https://github.com/Aderimo/bannerlord-coopkeep)");
        _index = LoadIndex();
    }

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoopKeep", "images");

    private string IndexPath => Path.Combine(_root, "index.json");

    /// <summary>
    /// Oyundan çıkarılmış görsel: dosya adı oyunun iç kimliği (<c>imperial_legionary.png</c>).
    /// Oyunun dili ne olursa olsun aynıdır; bu yüzden addan önce bakılır.
    /// </summary>
    public string? TryGetById(GameImageKind kind, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        var folder = kind.ToString().ToLowerInvariant();
        foreach (var dir in _packDirectories)
        {
            var direct = Path.Combine(dir, folder, id + ".png");
            if (File.Exists(direct)) return direct;
        }

        return null;
    }

    /// <summary>Önbellekte varsa yolu hemen döner (ağa çıkmaz).</summary>
    public string? TryGetCached(GameImageKind kind, string name)
    {
        var folder = kind.ToString().ToLowerInvariant();
        var slug = Slug(name);
        foreach (var dir in _packDirectories)
            foreach (var ext in new[] { ".png", ".jpg" })
            {
                var direct = Path.Combine(dir, folder, slug + ext);
                if (File.Exists(direct)) return direct;
            }

        lock (_indexLock)
        {
            return _index.TryGetValue(Key(kind, name), out var e) && e.File is { } f && File.Exists(Path.Combine(_root, f))
                ? Path.Combine(_root, f)
                : null;
        }
    }

    /// <summary>Görseli getirir: önbellekte yoksa indirir. Bulunamazsa <see langword="null"/>.</summary>
    public Task<string?> GetAsync(GameImageKind kind, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.FromResult<string?>(null);
        if (TryGetCached(kind, name) is { } hit) return Task.FromResult<string?>(hit);

        var key = Key(kind, name);
        lock (_indexLock)
        {
            if (_index.TryGetValue(key, out var e) && e.File is null && DateTimeOffset.UtcNow - e.At < MissRetry)
                return Task.FromResult<string?>(null);
        }

        return _inflight.GetOrAdd(key, _ => FetchAsync(kind, name, key, ct));
    }

    /// <summary>Harita görüntüsünü getirir.</summary>
    public async Task<string?> GetMapAsync(CancellationToken ct = default)
    {
        foreach (var dir in _packDirectories)
        {
            var packed = Path.Combine(dir, "map", "calradia.png");
            if (File.Exists(packed)) return packed;
        }

        var path = Path.Combine(_root, "map", "calradia.png");

        return await DownloadAsync(MapUrl, path, ct).ConfigureAwait(false) ? path : null;
    }

    private async Task<string?> FetchAsync(GameImageKind kind, string name, string key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var url = await ResolveAsync(kind, name, ct).ConfigureAwait(false);
            string? relative = null;

            if (url is not null)
            {
                var ext = url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) || url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
                relative = Path.Combine(kind.ToString().ToLowerInvariant(), Slug(name) + ext);
                if (!await DownloadAsync(url, Path.Combine(_root, relative), ct).ConfigureAwait(false)) relative = null;
            }

            Remember(key, relative);
            return relative is null ? null : Path.Combine(_root, relative);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            // Çevrimdışı: kaydetme, sonra tekrar denensin.
            return null;
        }
        finally
        {
            _gate.Release();
            _inflight.TryRemove(key, out _);
        }
    }

    private async Task<string?> ResolveAsync(GameImageKind kind, string name, CancellationToken ct)
    {
        switch (kind)
        {
            case GameImageKind.Troop:
            case GameImageKind.Item:
                return await FandomPageImageAsync(name, ct).ConfigureAwait(false);

            case GameImageKind.ClanBanner:
                // Wiki'lerdeki sancaklar tutarsız (ansiklopedi ekran görüntüleri, eski oyunun
                // bayrakları). Sancaklar yalnızca oyunun kendisinden çıkarılır (CoopKeep.ImageExporter).
                return null;

            case GameImageKind.Lord:
                return await FextralifeFileUrlAsync($"File:{name.Trim().Replace(' ', '_')}.png", ct).ConfigureAwait(false);

            case GameImageKind.Settlement:
                return await FextralifeFileMatchAsync(name, preferLogo: false, ct).ConfigureAwait(false);

            case GameImageKind.Skill:
                return SkillIconFiles.TryGetValue(name, out var file)
                    ? await FextralifeFileUrlAsync("File:" + file, ct).ConfigureAwait(false)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Beceri ikonlarının wiki'deki dosya adları. Oyunun beceri ekranındaki mozaik ikonların
    /// aynısı; adlar tutarsız olduğu için elle eşlendi (2026-10-17).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SkillIconFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OneHanded"] = "1-handed-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["TwoHanded"] = "2-handed-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Polearm"] = "Polearm-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Bow"] = "Bow-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Crossbow"] = "Crossbow-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Throwing"] = "Throwing-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Riding"] = "Riding-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Athletics"] = "Athletic-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Crafting"] = "Smithing-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Scouting"] = "Scouting-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Tactics"] = "Tactics-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Roguery"] = "Roguery-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Charm"] = "Charm-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Leadership"] = "Leadership-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Trade"] = "Trade-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Steward"] = "Steward-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Medicine"] = "Medicine-mount-&-blade-2-bannerlord-wiki-guide.png",
            ["Engineering"] = "Engineering-mount-&-blade-2-bannerlord-wiki-guide.png",
        };

    /// <summary>Fandom sayfasının ana görseli (PageImages).</summary>
    private async Task<string?> FandomPageImageAsync(string title, CancellationToken ct)
    {
        var url = $"{FandomApi}?action=query&format=json&prop=pageimages&pithumbsize=240&redirects=1&titles={Uri.EscapeDataString(title)}";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct).ConfigureAwait(false));

        foreach (var page in doc.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
            if (page.Value.TryGetProperty("thumbnail", out var t) && t.TryGetProperty("source", out var s))
                return WithFormat(s.GetString());

        return null;
    }

    private async Task<string?> FextralifeFileUrlAsync(string fileTitle, CancellationToken ct)
    {
        var url = $"{FextralifeApi}?action=query&format=json&prop=imageinfo&iiprop=url&iiurlwidth=240&titles={Uri.EscapeDataString(fileTitle)}";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct).ConfigureAwait(false));

        foreach (var page in doc.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
        {
            if (!page.Value.TryGetProperty("imageinfo", out var info) || info.GetArrayLength() == 0) continue;
            var ii = info[0];
            if (ii.TryGetProperty("thumburl", out var thumb)) return thumb.GetString();
            if (ii.TryGetProperty("url", out var full)) return full.GetString();
        }

        return null;
    }

    /// <summary>
    /// Fextralife'ın dosya adları tutarsız (<c>0_0001_dey_meroc_logo.png</c>,
    /// <c>Clan_0000_osticos.png</c>, <c>0_0006_epicrotea.png</c>): tüm dosya listesi bir kez
    /// alınır ve ad parçasına göre eşleştirilir.
    /// </summary>
    private async Task<string?> FextralifeFileMatchAsync(string name, bool preferLogo, CancellationToken ct)
    {
        var files = await (_fextralifeFiles ??= LoadFextralifeFilesAsync(ct)).ConfigureAwait(false);
        var needle = Slug(name).Replace("-", "_");
        if (needle.Length < 3) return null;

        var candidates = files
            .Where(f => Slug(f.Key).Replace("-", "_").Contains(needle, StringComparison.Ordinal))
            .Where(f => !f.Key.Contains("hall", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => preferLogo && (f.Key.Contains("logo", StringComparison.OrdinalIgnoreCase) || f.Key.StartsWith("Clan", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(f => f.Key.Length)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0].Value;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadFextralifeFilesAsync(CancellationToken ct)
    {
        var cache = Path.Combine(_root, "fextralife-files.json");
        if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(14))
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false)) ?? [];
            }
            catch (JsonException) { }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? next = null;
        for (var page = 0; page < 20; page++)
        {
            var url = $"{FextralifeApi}?action=query&format=json&list=allimages&ailimit=500&aiprop=url" +
                      (next is null ? "" : "&aicontinue=" + Uri.EscapeDataString(next));
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct).ConfigureAwait(false));

            foreach (var img in doc.RootElement.GetProperty("query").GetProperty("allimages").EnumerateArray())
                if (img.TryGetProperty("name", out var n) && img.TryGetProperty("url", out var u))
                    result[n.GetString()!] = u.GetString()!;

            next = doc.RootElement.TryGetProperty("continue", out var c) && c.TryGetProperty("aicontinue", out var ac) ? ac.GetString() : null;
            if (next is null) break;
        }

        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(result), ct).ConfigureAwait(false);
        return result;
    }

    private async Task<bool> DownloadAsync(string url, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Fandom aksi halde WebP döndürüyor.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg", 0.9));

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return false;

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (!LooksLikeImage(bytes)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        return true;
    }

    /// <summary>PNG, JPEG ya da WebP imzası.</summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> b) =>
        (b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') ||
        (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) ||
        (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E');

    private static string? WithFormat(string? url) =>
        url is null ? null : url + (url.Contains('?') ? "&" : "?") + "format=original";

    private static string Key(GameImageKind kind, string name) => kind + ":" + Slug(name);

    /// <summary>Dosya adı ve karşılaştırma için: küçük harf, harf/rakam ve tire.</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    private void Remember(string key, string? file)
    {
        lock (_indexLock)
        {
            _index[key] = new IndexEntry(file, DateTimeOffset.UtcNow);
            try
            {
                Directory.CreateDirectory(_root);
                File.WriteAllText(IndexPath, JsonSerializer.Serialize(_index));
            }
            catch (IOException) { }
        }
    }

    private Dictionary<string, IndexEntry> LoadIndex()
    {
        try
        {
            if (File.Exists(IndexPath))
                return JsonSerializer.Deserialize<Dictionary<string, IndexEntry>>(File.ReadAllText(IndexPath)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException) { }

        return [];
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
