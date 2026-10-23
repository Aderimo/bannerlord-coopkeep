using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Mods;

/// <summary>Mod görselleri için disk önbelleği.</summary>
public static class ModImageCache
{
    /// <summary>Görseli önbellekten verir, yoksa indirir. Olmazsa <see langword="null"/>.</summary>
    public static async Task<string?> GetAsync(HttpClient http, string? url, string cacheDirectory, string key,
        CancellationToken cancellationToken = default)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (extension.Length is 0 or > 5) extension = ".jpg";
        var path = Path.Combine(cacheDirectory, string.Concat(key.Split(Path.GetInvalidFileNameChars())) + extension);
        if (File.Exists(path)) return path;

        try
        {
            var bytes = await http.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Modun bulunduğu site.</summary>
public enum ModSite { Workshop, Nexus }

/// <summary>Arama sonucunda bir mod.</summary>
public sealed record ModSearchResult
{
    public required ModSite Site { get; init; }

    /// <summary>Sitedeki kimlik (Atölye öğe kimliği ya da Nexus mod numarası).</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }
    public string? Author { get; init; }
    public string? Summary { get; init; }
    public string? Version { get; init; }

    /// <summary>Uzak görsel adresi.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Önbellekteki görsel (indirilebildiyse).</summary>
    public string? ImagePath { get; init; }

    /// <summary>Atölye'de abone, Nexus'ta indirme sayısı.</summary>
    public long Popularity { get; init; }

    public int Endorsements { get; init; }
    public DateTimeOffset? Updated { get; init; }

    public string PageUrl => Site == ModSite.Workshop
        ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + Id
        : "https://www.nexusmods.com/mountandblade2bannerlord/mods/" + Id;
}

/// <summary>
/// Steam Atölyesi'nde Bannerlord modu arar.
/// </summary>
/// <remarks>
/// Steam'in arama uç noktası anahtar istiyor; herkese açık arama sayfasındaki öğe
/// kimlikleri okunur, ayrıntılar anahtarsız <c>GetPublishedFileDetails</c> ile alınır
/// (<see cref="WorkshopDetailsService"/>, önbellekli). Sayfa biçimi değişirse arama boş
/// döner; program o durumda Atölye sayfasını tarayıcıda açmayı önerir.
/// </remarks>
public static partial class WorkshopSearch
{
    public const string AppId = "261550";

    [GeneratedRegex(@"filedetails/\?id=(\d{6,20})")]
    private static partial Regex ItemLink();

    public static string SearchUrl(string query, int page) =>
        string.IsNullOrWhiteSpace(query)
            ? $"https://steamcommunity.com/workshop/browse/?appid={AppId}&browsesort=trend&section=readytouseitems&days=-1&p={page}"
            : $"https://steamcommunity.com/workshop/browse/?appid={AppId}&searchtext={Uri.EscapeDataString(query.Trim())}&browsesort=textsearch&section=readytouseitems&p={page}";

    /// <summary>Arama sayfasındaki öğe kimlikleri, sayfadaki sırayla.</summary>
    public static IReadOnlyList<string> ParseIds(string html) =>
        ItemLink().Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Arar; ayrıntılar tek bir toplu istekle gelir. Görseller ayrıca yüklenir (<see cref="ModImageCache"/>).</summary>
    public static async Task<IReadOnlyList<ModSearchResult>> SearchAsync(HttpClient http, string query, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var html = await http.GetStringAsync(SearchUrl(query, page), cancellationToken).ConfigureAwait(false);
        var ids = ParseIds(html).Take(30).ToList();
        if (ids.Count == 0) return [];

        var form = new Dictionary<string, string> { ["itemcount"] = ids.Count.ToString(CultureInfo.InvariantCulture) };
        for (var i = 0; i < ids.Count; i++) form[$"publishedfileids[{i}]"] = ids[i];

        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(WorkshopDetailsService.ApiUrl, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var details = WorkshopDetails.ParseMany(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        return details.Where(d => d.Title.Length > 0).Select(d => new ModSearchResult
        {
            Site = ModSite.Workshop,
            Id = d.Id,
            Title = d.Title,
            Summary = Shorten(d.Description, 220),
            ImageUrl = d.PreviewUrl,
            Popularity = d.Subscriptions,
            Updated = d.Updated,
        }).ToList();
    }

    /// <summary>Steam istemcisinde öğenin sayfası: oyuncu oradan abone olur.</summary>
    public static string SteamClientUrl(string id) => "steam://url/CommunityFilePage/" + id;

    internal static string Shorten(string text, int max)
    {
        text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }
}

/// <summary>
/// Nexus Mods'ta Bannerlord modu arar ve (anahtar varsa) indirir.
/// </summary>
/// <remarks>
/// <para>
/// Arama Nexus'un herkese açık GraphQL uç noktasıyla yapılır; anahtar gerekmez.
/// </para>
/// <para>
/// Doğrudan indirme Nexus'un kuralı gereği yalnızca Premium üyelikte ve kullanıcının kendi
/// API anahtarıyla mümkün. Anahtar yoksa ya da üyelik Premium değilse mod sayfası tarayıcıda
/// açılır; kullanıcı dosyayı indirip "Zip'ten kur" ile kurar.
/// </para>
/// </remarks>
public static class NexusSearch
{
    public const string GameDomain = "mountandblade2bannerlord";
    public const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";
    public const string ApiUrl = "https://api.nexusmods.com/v1";

    private const string Query =
        "query($f: ModsFilter, $count: Int, $offset: Int){ mods(filter:$f, count:$count, offset:$offset, sort:[{endorsements:{direction:DESC}}])" +
        "{ nodes { modId name summary version author thumbnailUrl endorsements downloads updatedAt } } }";

    /// <summary>GraphQL istek gövdesi.</summary>
    public static string BuildRequest(string query, int count, int offset)
    {
        var filter = new Dictionary<string, object>
        {
            ["gameDomainName"] = new[] { new { value = GameDomain, op = "EQUALS" } },
        };
        if (!string.IsNullOrWhiteSpace(query)) filter["nameStemmed"] = new[] { new { value = query.Trim(), op = "EQUALS" } };

        return JsonSerializer.Serialize(new { query = Query, variables = new { f = filter, count, offset } });
    }

    /// <summary>GraphQL yanıtındaki modlar; hata ya da bozuk yanıtta boş liste.</summary>
    public static IReadOnlyList<ModSearchResult> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return [];
            if (!data.TryGetProperty("mods", out var mods) || !mods.TryGetProperty("nodes", out var nodes)) return [];

            var result = new List<ModSearchResult>();
            foreach (var n in nodes.EnumerateArray())
            {
                var id = n.TryGetProperty("modId", out var mid) && mid.ValueKind == JsonValueKind.Number ? mid.GetInt64().ToString(CultureInfo.InvariantCulture) : null;
                var name = Str(n, "name");
                if (id is null || string.IsNullOrEmpty(name)) continue;

                result.Add(new ModSearchResult
                {
                    Site = ModSite.Nexus,
                    Id = id,
                    Title = WebUtility.HtmlDecode(name),
                    Author = Str(n, "author"),
                    Summary = WebUtility.HtmlDecode(Str(n, "summary") ?? ""),
                    Version = Str(n, "version"),
                    ImageUrl = Str(n, "thumbnailUrl"),
                    Popularity = n.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                    Endorsements = n.TryGetProperty("endorsements", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0,
                    Updated = DateTimeOffset.TryParse(Str(n, "updatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var u) ? u : null,
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>Arar. Görseller ayrıca yüklenir (<see cref="ModImageCache"/>).</summary>
    public static async Task<IReadOnlyList<ModSearchResult>> SearchAsync(HttpClient http, string query, int page = 1,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(BuildRequest(query, 30, (Math.Max(1, page) - 1) * 30), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(GraphQlUrl, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Anahtarın sahibi Premium üye mi? (Doğrudan indirme yalnızca Premium'da.)</summary>
    /// <returns>Anahtar geçersizse <see langword="null"/>.</returns>
    public static async Task<bool?> IsPremiumAsync(HttpClient http, string apiKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl + "/users/validate.json");
        request.Headers.Add("apikey", apiKey);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("is_premium", out var p) && p.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Modun ana dosyasını indirir. Premium değilse ya da dosya yoksa <see langword="null"/>
    /// döner (kullanıcı sayfadan elle indirir).
    /// </summary>
    public static async Task<string?> DownloadMainFileAsync(HttpClient http, string apiKey, string modId, string targetDirectory,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage Get(string url)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            r.Headers.Add("apikey", apiKey);
            r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return r;
        }

        using var filesResponse = await http.SendAsync(Get($"{ApiUrl}/games/{GameDomain}/mods/{modId}/files.json?category=main"), cancellationToken).ConfigureAwait(false);
        if (!filesResponse.IsSuccessStatusCode) return null;
        using var files = JsonDocument.Parse(await filesResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var main = files.RootElement.TryGetProperty("files", out var list)
            ? list.EnumerateArray().OrderByDescending(f => f.TryGetProperty("uploaded_timestamp", out var t) ? t.GetInt64() : 0).FirstOrDefault()
            : default;
        if (main.ValueKind != JsonValueKind.Object || !main.TryGetProperty("file_id", out var fileIdElement)) return null;
        var fileId = fileIdElement.GetInt64().ToString(CultureInfo.InvariantCulture);
        var fileName = main.TryGetProperty("file_name", out var fn) ? fn.GetString() ?? fileId + ".zip" : fileId + ".zip";

        using var linkResponse = await http.SendAsync(Get($"{ApiUrl}/games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json"), cancellationToken).ConfigureAwait(false);
        if (!linkResponse.IsSuccessStatusCode) return null; // Premium değil
        using var links = JsonDocument.Parse(await linkResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var uri = links.RootElement.ValueKind == JsonValueKind.Array
            ? links.RootElement.EnumerateArray().Select(l => l.TryGetProperty("URI", out var u) ? u.GetString() : null).FirstOrDefault(u => u is not null)
            : null;
        if (uri is null) return null;

        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, string.Concat(fileName.Split(Path.GetInvalidFileNameChars())));
        using var download = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        download.EnsureSuccessStatusCode();
        var total = download.Content.Headers.ContentLength ?? 0;
        await using var source = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            if (total > 0) progress?.Report((double)done / total);
        }

        return path;
    }
}
