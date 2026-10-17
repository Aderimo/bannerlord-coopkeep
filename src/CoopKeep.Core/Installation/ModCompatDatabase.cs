using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopKeep.Core.Installation;

/// <summary>Topluluğun bildirdiği uyumluluk durumu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommunityStatus>))]
public enum CommunityStatus
{
    Unknown,

    /// <summary>Coop ile birlikte çalışıyor (sunucuda olmasa da istemcide sorun çıkarmıyor).</summary>
    Works,

    /// <summary>Coop ile birlikte hata veriyor veya bağlantıyı engelliyor.</summary>
    Broken,

    /// <summary>Yalnızca istemcide çalışan, görsel/arayüz modu.</summary>
    ClientOnly,
}

/// <summary>Listede tek bir mod.</summary>
public sealed record ModCompatEntry
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("status")] public CommunityStatus Status { get; init; }

    /// <summary>Bilginin geçerli olduğu Coop sürümü (örn. <c>v0.1.6</c>).</summary>
    [JsonPropertyName("coop")] public string? CoopVersion { get; init; }

    [JsonPropertyName("note")] public string? Note { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("workshop")] public string? Workshop { get; init; }
    [JsonPropertyName("nexus")] public JsonElement? Nexus { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("works")] public int Works { get; init; }
    [JsonPropertyName("broken")] public int Broken { get; init; }

    /// <summary>Gerçek Module Id bilinmiyor; <see cref="Id"/> görünen ad.</summary>
    [JsonPropertyName("idGuess")] public bool IdGuess { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;

    /// <summary>Steam Workshop kimliği (yalnız rakamlar), yoksa <see langword="null"/>.</summary>
    public string? WorkshopId => WorkshopDetails.IsValidId(Workshop?.Trim()) ? Workshop!.Trim() : null;

    public string? WorkshopUrl => WorkshopId is { } id ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id : null;

    public string? NexusUrl =>
        Nexus is { } n && n.ValueKind is JsonValueKind.Number or JsonValueKind.String && n.ToString() is { Length: > 0 } id
            ? "https://www.nexusmods.com/mountandblade2bannerlord/mods/" + id
            : null;

    /// <summary>Modun sayfası: Steam Workshop, yoksa Nexus.</summary>
    public string? Url => WorkshopUrl ?? NexusUrl;
}

/// <summary>
/// Topluluk uyumluluk listesi: GitHub reposundaki <c>compat/mods.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Uygulama listeyi indirir ve yerel olarak saklar; çevrimdışıyken son indirilen kopya
/// kullanılır. Listeye ekleme kullanıcıların açtığı GitHub issue'ları üzerinden, depo
/// sahibinin onayıyla yapılır — otomatik veri toplanmaz.
/// </para>
/// </remarks>
public sealed class ModCompatDatabase
{
    public const string DefaultUrl = "https://raw.githubusercontent.com/Aderimo/bannerlord-coopkeep/main/compat/mods.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, ModCompatEntry> _byId;

    /// <summary>Gerçek kimliği bilinmeyen (<see cref="ModCompatEntry.IdGuess"/>) modlar için ada göre eşleşme.</summary>
    private readonly Dictionary<string, ModCompatEntry> _byName;

    private ModCompatDatabase(IEnumerable<ModCompatEntry> entries)
    {
        _byId = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => Normalize(e.Id))
            .ToDictionary(g => g.Key, g => g.Last());

        _byName = _byId.Values
            .GroupBy(e => Normalize(e.DisplayName))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    public static ModCompatDatabase Empty { get; } = new([]);

    public int Count => _byId.Count;

    /// <summary>Listedeki bütün modlar (yüklü olmayanlar dahil).</summary>
    public IEnumerable<ModCompatEntry> All => _byId.Values;

    public ModCompatEntry? Find(string moduleId) =>
        _byId.TryGetValue(Normalize(moduleId), out var e) ? e
        : _byName.TryGetValue(Normalize(moduleId), out var n) && n.IdGuess ? n
        : null;

    /// <summary>Liste metnini okur; bozuksa <see langword="null"/>.</summary>
    public static ModCompatDatabase? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var array = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("mods", out var mods) ? mods : default;

            if (array.ValueKind != JsonValueKind.Array) return null;

            var entries = array.Deserialize<List<ModCompatEntry>>(Options) ?? [];
            return new ModCompatDatabase(entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Önbellekteki kopyayı okur.</summary>
    public static ModCompatDatabase LoadCached(string cachePath) =>
        File.Exists(cachePath) && Parse(SafeRead(cachePath)) is { } db ? db : Empty;

    /// <summary>
    /// Listeyi indirir ve önbelleğe yazar. Başarısız olursa önbellekteki kopya döner.
    /// </summary>
    public static async Task<ModCompatDatabase> FetchAsync(HttpClient http, string cachePath, string url = DefaultUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            if (Parse(json) is { } db)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);
                return db;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            // Çevrimdışı ya da GitHub'a ulaşılamıyor: önbelleğe düş.
        }

        return LoadCached(cachePath);
    }

    /// <summary>Kimlik karşılaştırması büyük/küçük harf ve nokta/boşluk farkından bağımsız.</summary>
    public static string Normalize(string id) =>
        new(id.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return ""; }
    }
}
