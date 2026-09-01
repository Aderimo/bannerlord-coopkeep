using System.Text.Json;

namespace CoopKeep.Core.Protocol;

/// <summary>
/// Server çıktısındaki <c>@DS@</c> önekli satırları tipli olaylara çevirir.
/// </summary>
/// <remarks>
/// <para>
/// Ayrıştırıcı <b>asla fırlatmaz</b>. Bozuk, yarım veya tanınmayan JSON sessizce
/// reddedilir. Sebebi: bu satırlar canlı bir process'in stdout'undan geliyor;
/// satır sınırında kesilmiş bir JSON yüzünden manager'ın çökmesi kabul edilemez.
/// </para>
/// <para>
/// Satır iki kaynaktan gelebilir ve ikisi de desteklenir:
/// <list type="bullet">
///   <item>stdout: <c>@DS@{"ev":...}</c> (ham)</item>
///   <item>log dosyası: <c>21:24:25.353  @DS@{"ev":...}</c> (zaman damgalı)</item>
/// </list>
/// Bu yüzden işaretçi satırın başında değil, <i>herhangi bir yerinde</i> aranır.
/// </para>
/// </remarks>
public static class DsEventParser
{
    /// <summary>Olay satırlarının öneki.</summary>
    public const string Marker = "@DS@";

    /// <summary>
    /// Tek bir çıktı satırını ayrıştırmayı dener.
    /// </summary>
    /// <returns>
    /// Satır bir <c>@DS@</c> olayıysa ve JSON'u geçerliyse <see langword="true"/>.
    /// Sıradan log satırları için <see langword="false"/> döner — bu bir hata değil,
    /// beklenen durumdur.
    /// </returns>
    public static bool TryParse(string? line, out DsEvent? evt)
    {
        evt = null;
        if (string.IsNullOrEmpty(line)) return false;

        var markerAt = line.IndexOf(Marker, StringComparison.Ordinal);
        if (markerAt < 0) return false;

        var json = line[(markerAt + Marker.Length)..].Trim();
        if (json.Length == 0 || json[0] != '{') return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (!root.TryGetProperty("ev", out var evProp) || evProp.ValueKind != JsonValueKind.String)
                return false;

            var name = evProp.GetString() ?? string.Empty;
            evt = name switch
            {
                "state" => ParseState(root),
                "players" => ParsePlayers(root),
                "commands" => ParseCommands(root),
                _ => new DsEvent.Unknown(name, json),
            };
            return evt is not null;
        }
        catch (JsonException)
        {
            // Yarım satır veya bozuk JSON. Sessizce yok say.
            return false;
        }
    }

    private static DsEvent ParseState(JsonElement root)
    {
        var rawPhase = GetString(root, "phase") ?? string.Empty;
        return new DsEvent.State(
            Phase: ServerPhaseExtensions.ParsePhase(rawPhase),
            RawPhase: rawPhase,
            SaveName: GetString(root, "save"),
            PasswordRequired: GetBool(root, "pw"));
    }

    private static DsEvent ParsePlayers(JsonElement root)
    {
        var players = new List<ConnectedPlayer>();

        if (root.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                players.Add(new ConnectedPlayer(
                    Id: GetInt(item, "id") ?? -1,
                    Name: GetString(item, "name") ?? ConnectedPlayer.JoiningPlaceholder,
                    State: GetString(item, "state") ?? "unknown",
                    Address: GetString(item, "addr")));
            }
        }

        return new DsEvent.Players(players);
    }

    private static DsEvent ParseCommands(JsonElement root) =>
        new DsEvent.Commands(
            Builtin: GetStringArray(root, "builtin"),
            Game: GetStringArray(root, "game"));

    // --- küçük yardımcılar: tip uyuşmazlığında fırlatmak yerine null döner ---

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)
            ? v
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                result.Add(s);
        }
        return result;
    }
}
