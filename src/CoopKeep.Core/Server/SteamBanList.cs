using System.Text.Json;
using System.Text.Json.Nodes;
using CoopKeep.Core.Saves;

namespace CoopKeep.Core.Server;

/// <summary>Banlı bir oyuncu.</summary>
/// <param name="SteamId">SteamID64.</param>
/// <param name="Name">Banlandığı andaki karakter ya da Steam adı (yalnızca gösterim için).</param>
public sealed record SteamBan(string SteamId, string? Name, DateTimeOffset? BannedAt, string? Note);

/// <summary>
/// Coop'un kendi ban listesi: <c>steam-bans.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Biçim Coop'un belgesinden (<c>doc/SteamBans.md</c>):
/// <c>{"steamIds": ["7656…"], "entries": [{"steamId": "7656…", "heroName": "…"}]}</c>.
/// Sunucu dosyayı <b>her girişte</b> yeniden okur ve banlı oyuncuyu karakteri yüklenmeden
/// reddeder; yeniden başlatmak gerekmez. Bozuk JSON'da son geçerli listeyi kullanmaya
/// devam eder.
/// </para>
/// <para>
/// Dosya <c>COOP_DATA_DIR</c> klasöründe aranır; CoopKeep sunucuyu bu değişkenle başlatır.
/// <c>entries</c> içine CoopKeep'e ait <c>bannedAt</c> ve <c>note</c> alanları da yazılır;
/// sunucu tanımadığı alanları yok sayar.
/// </para>
/// </remarks>
public sealed class SteamBanList
{
    public const string FileName = "steam-bans.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly List<SteamBan> _bans = [];

    public SteamBanList(string path) => Path = path;

    public string Path { get; }

    public IReadOnlyList<SteamBan> Bans => _bans;

    public bool IsBanned(string? steamId) =>
        steamId is not null && _bans.Any(b => b.SteamId == steamId);

    /// <summary>Dosyayı okur; yoksa ya da bozuksa liste boş kalır.</summary>
    public static SteamBanList Load(string path)
    {
        var list = new SteamBanList(path);
        if (!File.Exists(path)) return list;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var entries = (root?["entries"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(e => (string?)e["steamId"])
                .Zip((root?["entries"] as JsonArray ?? []).OfType<JsonObject>())
                .Where(p => CoopSessionPlayer.IsSteamId64(p.First))
                .GroupBy(p => p.First!)
                .ToDictionary(g => g.Key, g => g.Last().Second);

            foreach (var id in (root?["steamIds"] as JsonArray ?? []).Select(n => n?.ToString()).Distinct())
            {
                if (!CoopSessionPlayer.IsSteamId64(id)) continue;

                entries.TryGetValue(id!, out var e);
                list._bans.Add(new SteamBan(
                    id!,
                    (string?)e?["heroName"],
                    DateTimeOffset.TryParse((string?)e?["bannedAt"], out var at) ? at : null,
                    (string?)e?["note"]));
            }
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException)
        {
            // Sunucu gibi davran: okunamayan dosya kimseyi banlamaz.
        }

        return list;
    }

    /// <summary>Oyuncuyu banlar. Zaten banlıysa yalnızca adı ve notu güncellenir.</summary>
    /// <exception cref="ArgumentException">SteamID64 geçerli değil.</exception>
    public void Ban(string steamId, string? name, string? note = null)
    {
        if (!CoopSessionPlayer.IsSteamId64(steamId))
            throw new ArgumentException($"Geçersiz SteamID64: '{steamId}'", nameof(steamId));

        _bans.RemoveAll(b => b.SteamId == steamId);
        _bans.Add(new SteamBan(steamId, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim()));
    }

    /// <summary>Banı kaldırır.</summary>
    /// <returns>Liste değiştiyse <see langword="true"/>.</returns>
    public bool Unban(string steamId) => _bans.RemoveAll(b => b.SteamId == steamId) > 0;

    /// <summary>
    /// Listeyi diske yazar. Önce geçici dosyaya yazılıp yer değiştirilir: sunucu yarım
    /// yazılmış bir dosya okumaz.
    /// </summary>
    public void Save()
    {
        var root = new JsonObject
        {
            ["steamIds"] = new JsonArray(_bans.Select(b => (JsonNode)JsonValue.Create(b.SteamId)!).ToArray()),
            ["entries"] = new JsonArray(_bans.Select(b =>
            {
                var o = new JsonObject { ["steamId"] = b.SteamId, ["heroName"] = b.Name ?? "" };
                if (b.BannedAt is { } at) o["bannedAt"] = at.ToString("O");
                if (b.Note is { } n) o["note"] = n;
                return (JsonNode)o;
            }).ToArray()),
        };

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(WriteOptions));
        File.Move(temp, Path, overwrite: true);
    }
}
