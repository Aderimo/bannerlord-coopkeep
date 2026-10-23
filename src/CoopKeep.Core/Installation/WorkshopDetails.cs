using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoopKeep.Core.Installation;

/// <summary>Bir Steam Workshop öğesinin herkese açık bilgileri.</summary>
public sealed record WorkshopDetails
{
    public required string Id { get; init; }
    public string Title { get; init; } = "";

    /// <summary>BBCode etiketleri temizlenmiş açıklama.</summary>
    public string Description { get; init; } = "";

    public string? PreviewUrl { get; init; }
    public long Subscriptions { get; init; }
    public DateTimeOffset? Updated { get; init; }

    /// <summary>Önbellekteki kapak görseli (indirilebildiyse).</summary>
    public string? ImagePath { get; init; }

    /// <summary>Ağa ulaşılamadı; süresi geçmiş önbellek kullanıldı.</summary>
    public bool IsStale { get; init; }

    public string PageUrl => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + Id;

    /// <summary>Workshop kimliği yalnızca rakamlardan oluşur (dosya adında da kullanılır).</summary>
    public static bool IsValidId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 20 && id.All(char.IsAsciiDigit);

    /// <summary>
    /// <c>ISteamRemoteStorage/GetPublishedFileDetails</c> yanıtını okur. Öğe yoksa,
    /// gizliyse veya yanıt bozuksa <see langword="null"/>.
    /// </summary>
    public static WorkshopDetails? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("publishedfiledetails", out var list) ||
                list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                return null;

            return FromItem(list[0]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Birden çok öğe soran yanıttaki bütün geçerli öğeler, yanıttaki sırayla.</summary>
    public static IReadOnlyList<WorkshopDetails> ParseMany(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("publishedfiledetails", out var list) ||
                list.ValueKind != JsonValueKind.Array)
                return [];

            return list.EnumerateArray().Select(FromItem).Where(d => d is not null).Select(d => d!).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static WorkshopDetails? FromItem(JsonElement item)
    {
        if (Long(item, "result") != 1) return null;

        var id = Text(item, "publishedfileid");
        if (!IsValidId(id)) return null;

        var updated = Long(item, "time_updated");
        return new WorkshopDetails
        {
            Id = id!,
            Title = Text(item, "title") ?? "",
            Description = CleanBbCode(Text(item, "description") ?? ""),
            PreviewUrl = Text(item, "preview_url") is { Length: > 0 } p ? p : null,
            Subscriptions = Long(item, "subscriptions") ?? 0,
            Updated = updated is > 0 ? DateTimeOffset.FromUnixTimeSeconds(updated.Value) : null,
        };
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        } : null;

    private static readonly Regex DropBlocks = new(
        @"\[(img|previewyoutube|previewimg)(=[^\]]*)?\].*?\[/\1\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex ListItem = new(@"\[\*\]\s*", RegexOptions.IgnoreCase);

    private static readonly Regex Tags = new(
        @"\[/?(b|i|u|s|strike|h1|h2|h3|url|list|olist|quote|code|noparse|spoiler|hr|table|tr|th|td|img|previewyoutube)(=[^\]]*)?\]",
        RegexOptions.IgnoreCase);

    private static readonly Regex ManyBlankLines = new(@"\n[ \t]*\n(?:[ \t]*\n)+");

    /// <summary>
    /// Workshop açıklamasındaki BBCode'u düz metne çevirir: görseller ve videolar atılır,
    /// bağlantıların yalnız metni kalır, liste maddeleri "• " olur, satır sonları korunur.
    /// </summary>
    public static string CleanBbCode(string text)
    {
        var s = text.Replace("\r\n", "\n").Replace('\r', '\n');
        s = DropBlocks.Replace(s, "");
        s = ListItem.Replace(s, "• ");
        s = Tags.Replace(s, "");
        s = s.Replace("﻿", "");
        s = ManyBlankLines.Replace(s, "\n\n");
        return string.Join('\n', s.Split('\n').Select(l => l.TrimEnd())).Trim();
    }
}

/// <summary>
/// Workshop bilgilerini Steam'in anahtarsız herkese açık servisinden alır ve önbellekler.
/// </summary>
/// <remarks>
/// Yanıt <c>&lt;id&gt;.json</c>, kapak görseli <c>&lt;id&gt;.jpg</c> olarak saklanır. Önbellek
/// <see cref="MaxAge"/> içindeyse ağa çıkılmaz; ağ yoksa eski kopya kullanılır.
/// </remarks>
public sealed class WorkshopDetailsService(string cacheDirectory)
{
    public const string ApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoopKeep", "workshop");

    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(7);

    public string CacheDirectory { get; } = cacheDirectory;

    private string JsonPath(string id) => Path.Combine(CacheDirectory, id + ".json");
    private string ImagePath(string id) => Path.Combine(CacheDirectory, id + ".jpg");

    /// <summary>Yalnızca önbellekten okur (ağa çıkmaz).</summary>
    public WorkshopDetails? TryGetCached(string id)
    {
        if (!WorkshopDetails.IsValidId(id)) return null;
        var path = JsonPath(id);
        if (!File.Exists(path)) return null;

        string json;
        try { json = File.ReadAllText(path); }
        catch (IOException) { return null; }

        return WorkshopDetails.Parse(json) is { } d
            ? d with
            {
                ImagePath = File.Exists(ImagePath(id)) ? ImagePath(id) : null,
                IsStale = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge,
            }
            : null;
    }

    /// <summary>
    /// Bilgileri getirir: taze önbellek → Steam → eski önbellek. Hiçbiri yoksa <see langword="null"/>.
    /// </summary>
    public async Task<WorkshopDetails?> GetAsync(HttpClient http, string id, CancellationToken cancellationToken = default)
    {
        if (!WorkshopDetails.IsValidId(id)) return null;

        var cached = TryGetCached(id);
        if (cached is { IsStale: false })
            return cached.ImagePath is null ? await WithImageAsync(http, cached, cancellationToken).ConfigureAwait(false) : cached;

        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = id,
            });
            using var response = await http.PostAsync(ApiUrl, form, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (WorkshopDetails.Parse(json) is { } fresh && fresh.Id == id)
            {
                Directory.CreateDirectory(CacheDirectory);
                await File.WriteAllTextAsync(JsonPath(id), json, cancellationToken).ConfigureAwait(false);

                // Görsel değişmiş olabilir; yeniden indir.
                return await WithImageAsync(http, fresh, cancellationToken, refresh: true).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            // Çevrimdışı ya da Steam'e ulaşılamıyor: eski önbelleğe düş.
        }

        return cached;
    }

    private async Task<WorkshopDetails> WithImageAsync(HttpClient http, WorkshopDetails details, CancellationToken ct,
        bool refresh = false)
    {
        var path = ImagePath(details.Id);
        if (!refresh && File.Exists(path)) return details with { ImagePath = path };
        if (details.PreviewUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return details with { ImagePath = File.Exists(path) ? path : null };

        try
        {
            var bytes = await http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            return details with { ImagePath = path };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return details with { ImagePath = File.Exists(path) ? path : null };
        }
    }
}
