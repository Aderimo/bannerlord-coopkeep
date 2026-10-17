using System.Globalization;
using System.Text.RegularExpressions;

namespace CoopKeep.Core.Protocol;

/// <summary>Bir partinin kadrosundaki tek satır (<c>character_ids</c>).</summary>
public sealed record RosterEntry(string TroopId, int Number, int Wounded, int Xp, bool IsHero);

/// <summary>Partinin kadrosu: askerler ve esirler.</summary>
public sealed record PartyRoster(string? HeroName, IReadOnlyList<RosterEntry> Members, IReadOnlyList<RosterEntry> Prisoners)
{
    /// <summary>Kahramanlar hariç asker sayısı.</summary>
    public int TroopCount => Members.Where(m => !m.IsHero).Sum(m => m.Number);
    public int WoundedCount => Members.Where(m => !m.IsHero).Sum(m => m.Wounded);
}

/// <summary><c>coop.debug.mobile_party.position</c> sonucu.</summary>
public sealed record PartyPosition(string PartyId, double X, double Y, bool OnLand, string? SettlementId, string? MoveMode);

/// <summary><c>coop.debug.clan.info</c> sonucu.</summary>
public sealed record ClanInfo(string Id, string Name, string? Leader, string? Kingdom, int? Influence, int? Renown, int? Tier,
    IReadOnlyList<string> Members);

/// <summary>Sunucudaki bir kahraman (<c>coop.debug.hero.list</c>).</summary>
public sealed record HeroListEntry(string RegistryId, string Name, string StringId);

/// <summary>Kimlik + ad çifti (klan ve krallık listeleri).</summary>
public sealed record NamedId(string Id, string Name);

/// <summary>
/// Coop'un oyun durumu komutlarının çıktılarını okur.
/// </summary>
/// <remarks>
/// Biçimler 2026-10-16'da Coop v0.1.6 sunucusundan birebir alındı; örnekler test
/// fikstürlerinde (<c>troops-clans.txt</c>, <c>lists.txt</c>).
/// </remarks>
public static partial class GameStateParser
{
    [GeneratedRegex(@"TROOP_STATE_SET party=(?<party>\S+) character=(?<troop>\S+) oldExists=\w+ oldNumber=(?<old>\d+) .* newNumber=(?<new>\d+)")]
    private static partial Regex TroopStateSet();

    [GeneratedRegex(@"^(?<id>[\w\.\-]+): number=(?<n>\d+) wounded=(?<w>\d+) xp=(?<xp>\d+) hero=(?<hero>True|False)")]
    private static partial Regex RosterLine();

    [GeneratedRegex(@"^##(?<name>.*?)\s+\(hero id:")]
    private static partial Regex RosterHeader();

    [GeneratedRegex(@"settlement=(?<s>\S+) ownerClan=(?<clan>\S+)(?: ownerLeader=(?<leader>\S+))?")]
    private static partial Regex OwnerState();

    [GeneratedRegex(@"^ID: '(?<rid>[^']+)', Name: '(?<name>.*)', Game ID: \d+, Game StringId (?<sid>\S+)")]
    private static partial Regex HeroListLine();

    [GeneratedRegex(@"^ID: '(?<id>[^']+)' Name: '(?<name>.*)'$")]
    private static partial Regex ClanListLine();

    [GeneratedRegex(@"^Applied (?<amount>-?\d+) influence to '.*'; clan is now at (?<now>-?[\d\.]+) influence")]
    private static partial Regex InfluenceApplied();

    /// <summary><c>TROOP_STATE_SET</c> satırından eski ve yeni sayıyı okur.</summary>
    public static (string TroopId, int Old, int New)? ParseTroopStateSet(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var m = TroopStateSet().Match(line);
            if (m.Success)
                return (m.Groups["troop"].Value, Int(m.Groups["old"].Value), Int(m.Groups["new"].Value));
        }

        return null;
    }

    /// <summary><c>character_ids</c> dökümünden kadroyu okur.</summary>
    public static PartyRoster? ParseRoster(IEnumerable<string> lines)
    {
        string? hero = null;
        var members = new List<RosterEntry>();
        var prisoners = new List<RosterEntry>();
        List<RosterEntry>? current = null;
        var any = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (RosterHeader().Match(line) is { Success: true } h) { hero = h.Groups["name"].Value.Trim(); any = true; continue; }
            if (line == "Member roster:") { current = members; any = true; continue; }
            if (line == "Prisoner roster:") { current = prisoners; continue; }

            if (current is not null && RosterLine().Match(line) is { Success: true } m)
                current.Add(new RosterEntry(m.Groups["id"].Value, Int(m.Groups["n"].Value), Int(m.Groups["w"].Value),
                    Int(m.Groups["xp"].Value), m.Groups["hero"].Value == "True"));
        }

        return any ? new PartyRoster(hero, members, prisoners) : null;
    }

    /// <summary><c>id=…|party=…|x=…|y=…|isOnLand=…|settlement=…|moveMode=…</c></summary>
    public static PartyPosition? ParsePosition(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("id=", StringComparison.Ordinal) || !line.Contains("|x=", StringComparison.Ordinal)) continue;

            var fields = line.Split('|')
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);

            if (!fields.TryGetValue("x", out var xs) || !fields.TryGetValue("y", out var ys)) continue;
            if (!double.TryParse(xs, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(ys, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;

            var settlement = fields.GetValueOrDefault("settlement");
            return new PartyPosition(
                fields.GetValueOrDefault("party") ?? "",
                x, y,
                fields.GetValueOrDefault("isOnLand") == "True",
                settlement is null or "none" ? null : settlement,
                fields.GetValueOrDefault("moveMode"));
        }

        return null;
    }

    /// <summary><c>SERVER settlement=town_EN1 ownerClan=clan_empire_north_2 ownerLeader=lord_1_3</c></summary>
    public static (string SettlementId, string ClanId)? ParseOwnerState(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("LIVE_TEST_JSON", StringComparison.Ordinal)) continue;
            var m = OwnerState().Match(line);
            if (m.Success) return (m.Groups["s"].Value, m.Groups["clan"].Value);
        }

        return null;
    }

    /// <summary><c>coop.debug.clan.info</c>: <c>Anahtar: Değer</c> satırları.</summary>
    public static ClanInfo? ParseClanInfo(IEnumerable<string> lines)
    {
        var kv = AdminResponseParser.ParseKeyValueList(lines);
        if (!kv.TryGetValue("StringId", out var id) || !kv.TryGetValue("Name", out var name)) return null;

        var members = kv.TryGetValue("Members", out var m)
            ? m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new ClanInfo(id, name, kv.GetValueOrDefault("Leader"), kv.GetValueOrDefault("Kingdom"),
            NInt(kv.GetValueOrDefault("Influence")), NInt(kv.GetValueOrDefault("Renown")), NInt(kv.GetValueOrDefault("Tier")),
            members);
    }

    /// <summary><c>Applied -50 influence to 'Osticos'; clan is now at 415 influence</c> → 415.</summary>
    public static int? ParseInfluenceApplied(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var m = InfluenceApplied().Match(line.Trim());
            if (m.Success) return NInt(m.Groups["now"].Value);
        }

        return null;
    }

    /// <summary><c>coop.debug.hero.list</c>.</summary>
    public static IReadOnlyList<HeroListEntry> ParseHeroList(IEnumerable<string> lines) =>
        lines.Select(l => HeroListLine().Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => new HeroListEntry(m.Groups["rid"].Value, m.Groups["name"].Value, m.Groups["sid"].Value))
            .ToList();

    /// <summary><c>coop.debug.clan.list</c>: <c>ID: 'x' Name: 'y'</c>.</summary>
    public static IReadOnlyList<NamedId> ParseClanList(IEnumerable<string> lines) =>
        lines.Select(l => ClanListLine().Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => new NamedId(m.Groups["id"].Value, m.Groups["name"].Value))
            .ToList();

    /// <summary><c>coop.debug.kingdom.list</c>: <c>Name: 'x'</c> ardından <c> Id : 'y'</c>.</summary>
    public static IReadOnlyList<NamedId> ParseKingdomList(IEnumerable<string> lines)
    {
        var result = new List<NamedId>();
        string? name = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("Name: '", StringComparison.Ordinal)) name = Quoted(line);
            else if (line.StartsWith("Id : '", StringComparison.Ordinal) && name is not null)
            {
                result.Add(new NamedId(Quoted(line), name));
                name = null;
            }
        }

        return result;

        static string Quoted(string s)
        {
            var a = s.IndexOf('\'');
            var b = s.LastIndexOf('\'');
            return b > a ? s[(a + 1)..b] : "";
        }
    }

    /// <summary><c>coop.debug.item_object.data</c> → <c>Value: 10</c>.</summary>
    public static int? ParseItemValue(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim())
            .Where(l => l.StartsWith("Value: ", StringComparison.Ordinal))
            .Select(l => NInt(l[7..]))
            .FirstOrDefault();

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int? NInt(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
}
