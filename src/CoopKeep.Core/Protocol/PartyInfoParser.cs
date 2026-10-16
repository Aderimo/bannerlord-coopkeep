using System.Globalization;
using System.Text.RegularExpressions;

namespace CoopKeep.Core.Protocol;

/// <summary>Bir partinin haritadaki durumu.</summary>
public sealed record PartyInfo
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>İçinde bulunduğu yerleşim; yoksa null.</summary>
    public string? CurrentSettlement { get; init; }

    /// <summary>Gittiği yerleşim; yoksa null.</summary>
    public string? TargetSettlement { get; init; }

    /// <summary>Oyunun davranış adı: <c>GoToSettlement</c>, <c>Hold</c>, <c>EngageParty</c>…</summary>
    public string? Behavior { get; init; }

    public bool InBattle { get; init; }
    public bool Besieging { get; init; }
    public bool InArmy { get; init; }
    public double? Morale { get; init; }

    /// <summary>Oyunun tahmini parti gücü.</summary>
    public double? Strength { get; init; }

    public int? MountedMen { get; init; }
    public int? PartySizeLimit { get; init; }
}

/// <summary>
/// <c>coop.debug.mobileparty.info &lt;id&gt;</c> dökümünü okur.
/// </summary>
/// <remarks>
/// Biçim Coop v0.1.6 sunucusundan alındı (bkz. fikstür <c>mobileparty-info.txt</c>): önce
/// <c>Name:</c>/<c>Morale:</c> satırları, sonra <c>alan (Tür): değer</c> satırları. Konum
/// <c>_position (…CampaignVec2): (CampaignVec2) X: 464.26944 Y: 469.2502 IsOnLand: True</c>;
/// bu, oyunun <c>settlements.xml</c>'deki <c>posX/posY</c> ile aynı koordinat sistemi.
/// </remarks>
public static partial class PartyInfoParser
{
    [GeneratedRegex(@"^(?<field>[A-Za-z_]+) \((?<type>[^)]*)\): (?<value>.*)$")]
    private static partial Regex Field();

    [GeneratedRegex(@"X: (?<x>-?[\d.]+) Y: (?<y>-?[\d.]+)")]
    private static partial Regex Vector();

    public static PartyInfo? Parse(string partyId, IEnumerable<string> lines)
    {
        string? name = null, current = null, target = null, behavior = null;
        double? x = null, y = null, morale = null, strength = null;
        bool battle = false, siege = false, army = false;
        int? horses = null, limit = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith("Name: ", StringComparison.Ordinal)) { name = line[6..].Trim(); continue; }
            if (line.StartsWith("Morale: ", StringComparison.Ordinal)) { morale = Num(line[8..]); continue; }
            if (line.StartsWith("ID: '", StringComparison.Ordinal) && line.EndsWith("not found", StringComparison.Ordinal)) return null;

            var m = Field().Match(line);
            if (!m.Success) continue;

            var value = m.Groups["value"].Value.Trim();
            var isNull = value == "<null>";

            switch (m.Groups["field"].Value)
            {
                case "_position":
                    if (Vector().Match(value) is { Success: true } v) { x = Num(v.Groups["x"].Value); y = Num(v.Groups["y"].Value); }
                    break;
                case "_currentSettlement": current = isNull ? null : value; break;
                case "_targetSettlement": target = isNull ? null : value; break;
                case "_defaultBehavior": behavior = value; break;
                case "_mapEventSide": battle = !isNull; break;
                case "_besiegerCamp": siege = !isNull; break;
                case "_army": army = !isNull; break;
                case "_cachedEstimatedStrength": strength = Num(value); break;
                case "_numberOfMenWithHorse": horses = (int?)Num(value); break;
                case "_cachedPartyMemberSizeLimit": limit = (int?)Num(value); break;
            }
        }

        if (x is null || y is null) return null;

        return new PartyInfo
        {
            Id = partyId,
            Name = name,
            X = x.Value,
            Y = y.Value,
            CurrentSettlement = current,
            TargetSettlement = target,
            Behavior = behavior,
            InBattle = battle,
            Besieging = siege,
            InArmy = army,
            Morale = morale,
            Strength = strength,
            MountedMen = horses,
            PartySizeLimit = limit,
        };
    }

    private static double? Num(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
