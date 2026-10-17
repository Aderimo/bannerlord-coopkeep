using System.Text.Json.Serialization;

namespace CoopKeep.Core.Viewer;

/// <summary>Haritadaki bir oyuncu, izleyicilere gönderilen biçimde.</summary>
public sealed record PlayerSnapshot
{
    [JsonPropertyName("heroId")] public required string HeroId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("clan")] public string? ClanName { get; init; }
    [JsonPropertyName("clanId")] public string? ClanId { get; init; }
    [JsonPropertyName("kingdomId")] public string? KingdomId { get; init; }
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("activity")] public string? Activity { get; init; }
    [JsonPropertyName("troops")] public int? Troops { get; init; }
    [JsonPropertyName("wounded")] public int? Wounded { get; init; }
    [JsonPropertyName("health")] public int? Health { get; init; }
    [JsonPropertyName("online")] public bool Online { get; init; }
}

/// <summary>Haritada izlenen bir lord ya da hükümdar.</summary>
public sealed record LordSnapshot
{
    [JsonPropertyName("heroId")] public required string HeroId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("clanId")] public string? ClanId { get; init; }
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("ruler")] public bool IsRuler { get; init; }
}

/// <summary>
/// İzleyici modunda ev sahibinin paylaştığı harita durumu.
/// </summary>
/// <remarks>
/// Yalnızca haritada zaten görünen bilgiler: konumlar, ne yapıldığı, asker sayısı,
/// yerleşimlerin sahipleri. SteamID, IP, altın ve yönetim bilgileri <b>yoktur</b>.
/// </remarks>
public sealed record MapSnapshot
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")] public int Version { get; init; } = CurrentVersion;
    [JsonPropertyName("at")] public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("world")] public string? World { get; init; }
    [JsonPropertyName("gameDate")] public string? GameDate { get; init; }
    [JsonPropertyName("players")] public IReadOnlyList<PlayerSnapshot> Players { get; init; } = [];
    [JsonPropertyName("lords")] public IReadOnlyList<LordSnapshot> Lords { get; init; } = [];

    /// <summary>Yerleşim kimliği → güncel sahip klan kimliği.</summary>
    [JsonPropertyName("owners")] public IReadOnlyDictionary<string, string> Owners { get; init; } = new Dictionary<string, string>();
}
