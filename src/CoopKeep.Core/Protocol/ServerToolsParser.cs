using System.Globalization;
using System.Text.RegularExpressions;

namespace CoopKeep.Core.Protocol;

/// <summary>Kahramanın gelişimi (<c>coopkeep.hero_stats</c>).</summary>
public sealed record HeroStats(
    string Id, string Name, int Level, int TotalXp, int UnspentAttributePoints, int UnspentFocusPoints,
    int HitPoints, int MaxHitPoints, int Gold, double Age,
    IReadOnlyList<HeroAttribute> Attributes, IReadOnlyList<HeroSkill> Skills);

/// <summary>Özellik: <c>vigor</c>, <c>control</c>, <c>endurance</c>, <c>cunning</c>, <c>social</c>, <c>intelligence</c>.</summary>
public sealed record HeroAttribute(string Id, string Name, int Value);

/// <summary>Beceri: seviye, odak puanı, bir sonraki seviyeye biriken XP ve bağlı özellik.</summary>
public sealed record HeroSkill(string Id, string Name, int Value, int Focus, int XpProgress, string AttributeId);

/// <summary><c>COOPKEEP_DEV hero=… what=skill:Leadership old=… new=…</c>.</summary>
public sealed record DevChange(string HeroId, string Kind, string Target, int Old, int New);

/// <summary>Kuşanımdaki tek parça. <see cref="Set"/>: <c>battle</c> ya da <c>civilian</c>.</summary>
public sealed record EquipmentPiece(string Set, string Slot, string ItemId, string Name);

/// <summary><c>coopkeep.roster</c>: parti ve kadrosu.</summary>
public sealed record ToolsRoster(string PartyId, int Total, int Wounded, IReadOnlyList<ToolsTroop> Members, IReadOnlyList<ToolsTroop> Prisoners)
{
    /// <summary>Kahramanlar hariç asker sayısı.</summary>
    public int TroopCount => Members.Where(m => !m.IsHero).Sum(m => m.Number);
}

public sealed record ToolsTroop(string Id, string Name, int Number, int Wounded, int Xp, bool IsHero);

/// <summary>Bir lord ya da oyuncu partisi (<c>coopkeep.parties</c>).</summary>
public sealed record WorldParty(
    string PartyId, string HeroId, string HeroName, string ClanId, string KingdomId, double X, double Y,
    int Troops, int Wounded, string SettlementId, string Behavior, bool InArmy, bool InBattle, bool IsPlayer,
    string TargetSettlementId = "", bool Besieging = false);

/// <summary>Kasaba ya da kale durumu (<c>coopkeep.settlement_stats</c>).</summary>
public sealed record SettlementStats(string Id, double Prosperity, double Security, double Loyalty, double Militia, string OwnerClanId);

/// <summary>Silah, zırh ve at değerleri (<c>coopkeep.item_stats</c>).</summary>
public sealed record ItemStats(
    string Id, int Swing, int Thrust, int SwingSpeed, int ThrustSpeed, int Length, int Accuracy,
    int HeadArmor, int BodyArmor, int ArmArmor, int LegArmor, int HorseSpeed, int Maneuver, int Charge, int HitPoints)
{
    public int TotalArmor => HeadArmor + BodyArmor + ArmArmor + LegArmor;
    public bool IsWeapon => Swing + Thrust + SwingSpeed + ThrustSpeed > 0;
    public bool IsArmor => TotalArmor > 0;
    public bool IsMount => HorseSpeed > 0;
}

/// <summary>
/// CoopKeep sunucu eklentisinin (v2) okuma komutlarının çıktıları.
/// </summary>
/// <remarks>
/// Hepsi <c>|</c> ayrılmış satırlar. Biçimler test sunucusunda alındı (<c>Fixtures/server-tools-v2.txt</c>).
/// Tanınmayan satırlar (sunucu günlüğü, nabız) atlanır.
/// </remarks>
public static partial class ServerToolsParser
{
    public const string End = "COOPKEEP_END";

    [GeneratedRegex(@"COOPKEEP_DEV hero=(?<hero>\S+) what=(?<kind>\w+):(?<target>\S+) old=(?<old>-?\d+) new=(?<new>-?\d+)")]
    private static partial Regex DevLine();

    public static HeroStats? ParseHeroStats(IEnumerable<string> lines)
    {
        string[]? head = null;
        var attributes = new List<HeroAttribute>();
        var skills = new List<HeroSkill>();

        foreach (var f in Fields(lines))
        {
            switch (f[0])
            {
                case "HERO" when f.Length >= 11: head = f; break;
                case "ATTR" when f.Length >= 4: attributes.Add(new HeroAttribute(f[1], f[2], Int(f[3]))); break;
                case "SKILL" when f.Length >= 7: skills.Add(new HeroSkill(f[1], f[2], Int(f[3]), Int(f[4]), Int(f[5]), f[6])); break;
            }
        }

        if (head is null) return null;
        return new HeroStats(head[1], head[2], Int(head[3]), Int(head[4]), Int(head[5]), Int(head[6]),
            Int(head[7]), Int(head[8]), Int(head[9]), Dbl(head[10]), attributes, skills);
    }

    /// <summary>Son <c>COOPKEEP_DEV</c> satırı.</summary>
    public static DevChange? ParseDevChange(IEnumerable<string> lines)
    {
        DevChange? last = null;
        foreach (var line in lines)
        {
            var m = DevLine().Match(line);
            if (m.Success)
                last = new DevChange(m.Groups["hero"].Value, m.Groups["kind"].Value, m.Groups["target"].Value,
                    Int(m.Groups["old"].Value), Int(m.Groups["new"].Value));
        }

        return last;
    }

    public static IReadOnlyList<EquipmentPiece> ParseEquipment(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "EQ" && f.Length >= 5)
            .Select(f => new EquipmentPiece(f[1], f[2], f[3], f[4]))
            .ToList();

    public static ToolsRoster? ParseRoster(IEnumerable<string> lines)
    {
        string[]? party = null;
        var members = new List<ToolsTroop>();
        var prisoners = new List<ToolsTroop>();

        foreach (var f in Fields(lines))
        {
            if (f[0] == "PARTY" && f.Length >= 4) party = f;
            else if (f[0] == "TROOP" && f.Length >= 8)
            {
                var troop = new ToolsTroop(f[2], f[3], Int(f[4]), Int(f[5]), Int(f[6]), f[7] == "1");
                (f[1] == "prisoners" ? prisoners : members).Add(troop);
            }
        }

        return party is null ? null : new ToolsRoster(party[1], Int(party[2]), Int(party[3]), members, prisoners);
    }

    public static IReadOnlyList<WorldParty> ParseParties(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "MP" && f.Length >= 15)
            .Select(f => new WorldParty(f[1], f[2], f[3], f[4], f[5], Dbl(f[6]), Dbl(f[7]), Int(f[8]), Int(f[9]),
                f[10], f[11], f[12] == "1", f[13] == "1", f[14] == "1",
                f.Length > 15 ? f[15] : "", f.Length > 16 && f[16] == "1"))
            .ToList();

    /// <summary><c>OWN|yerleşim|klan</c> → yerleşim → klan.</summary>
    public static IReadOnlyDictionary<string, string> ParseOwners(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Fields(lines))
            if (f[0] == "OWN" && f.Length >= 3 && f[2].Length > 0) result[f[1]] = f[2];
        return result;
    }

    public static IReadOnlyList<SettlementStats> ParseSettlementStats(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "SET" && f.Length >= 7)
            .Select(f => new SettlementStats(f[1], Dbl(f[2]), Dbl(f[3]), Dbl(f[4]), Dbl(f[5]), f[6]))
            .ToList();

    public static IReadOnlyList<ItemStats> ParseItemStats(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "STAT" && f.Length >= 16)
            .Select(f => new ItemStats(f[1], Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), Int(f[7]),
                Int(f[8]), Int(f[9]), Int(f[10]), Int(f[11]), Int(f[12]), Int(f[13]), Int(f[14]), Int(f[15])))
            .ToList();

    /// <summary>Çıktı bitiş işaretine ulaştı mı (uzun çıktılar parça parça gelir).</summary>
    public static bool IsComplete(IEnumerable<string> lines) => lines.Any(l => l.Trim() == End);

    private static IEnumerable<string[]> Fields(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).Where(l => l.Length > 0 && l.Contains('|') && !l.StartsWith('[')).Select(l => l.Split('|'));

    private static int Int(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;

    private static double Dbl(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
}
