using System.Globalization;
using System.Xml.Linq;
using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Encyclopedia;

public sealed record EncKingdom(string Id, string Name, string Text, string Culture, string? RulerId, uint Color);

public sealed record EncClan(string Id, string Name, string Text, string Culture, string? KingdomId, string? LeaderId,
    int Tier, bool IsMinor, bool IsBandit, uint Color, string? HomeSettlementId);

/// <summary>Bir lord. <see cref="Traits"/>: oyunun kişilik ve yetenek özellikleri (<c>Commander</c>, <c>Honor</c> …).</summary>
public sealed record EncLord(string Id, string Name, string Text, string Culture, string? ClanId, int Age, bool IsFemale,
    IReadOnlyDictionary<string, int> Traits, string? SpouseId, string? FatherId, string? MotherId);

/// <summary>Bir yerleşim. Köylerde <see cref="Produces"/> (<c>silk_plant</c>) ve <see cref="BoundTo"/> dolu.</summary>
public sealed record EncSettlement(string Id, string Name, string Text, string Culture, SettlementKind Kind, double X, double Y,
    string? OwnerClanId, int Prosperity, int Hearth, string? BoundTo, string? Produces, IReadOnlyDictionary<string, int> Buildings)
{
    public int Fortifications => Buildings.Where(b => b.Key.EndsWith("_fortifications", StringComparison.Ordinal)).Select(b => b.Value).FirstOrDefault();
}

/// <summary>Bir asker: kademesi, sınıfı, terfileri, becerileri ve ilk kuşanım setindeki eşyalar.</summary>
public sealed record EncTroop(string Id, string Name, string Culture, int Level, int Tier, string Class,
    IReadOnlyList<string> UpgradeTargets, IReadOnlyDictionary<string, int> Skills, IReadOnlyList<string> Equipment)
{
    public int SkillTotal => Skills.Values.Sum();
}

/// <summary>
/// Ansiklopedinin oyun verileri: krallıklar, klanlar, lordlar, yerleşimler, askerler.
/// </summary>
/// <remarks>
/// Hepsi kullanıcının Bannerlord kurulumundaki XML'lerden okunur (Native/SandBoxCore/SandBox).
/// Metinler oyunun kendi ansiklopedi metinleri (<c>text</c> alanları); Türkçe çeviri yine
/// oyunun kendi dosyalarından (<see cref="GameTexts"/>). Canlı veriler (güncel sahip, refah,
/// lordun yeri) arayüzde sunucudan eklenir.
/// </remarks>
public sealed class EncyclopediaData
{
    public IReadOnlyList<EncKingdom> Kingdoms { get; private init; } = [];
    public IReadOnlyList<EncClan> Clans { get; private init; } = [];
    public IReadOnlyList<EncLord> Lords { get; private init; } = [];
    public IReadOnlyList<EncSettlement> Settlements { get; private init; } = [];
    public IReadOnlyList<EncTroop> Troops { get; private init; } = [];
    public IReadOnlyDictionary<string, string> Cultures { get; private init; } = new Dictionary<string, string>();

    /// <summary>Modül klasöründen okur. Bulunamayan dosyalar boş liste olur.</summary>
    public static EncyclopediaData Load(string modulesDirectory, GameTexts texts)
    {
        string P(string module, string file) => Path.Combine(modulesDirectory, module, "ModuleData", file);

        return Load(
            kingdomsXml: P("SandBox", "spkingdoms.xml"),
            clansXml: P("SandBox", "spclans.xml"),
            lordsXml: P("SandBox", "lords.xml"),
            heroesXml: P("SandBox", "heroes.xml"),
            settlementsXml: P("SandBox", "settlements.xml"),
            troopsXml: P("SandBoxCore", "spnpccharacters.xml"),
            culturesXml: P("SandBoxCore", "spcultures.xml"),
            texts);
    }

    public static EncyclopediaData Load(string kingdomsXml, string clansXml, string lordsXml, string heroesXml,
        string settlementsXml, string troopsXml, string? culturesXml, GameTexts texts)
    {
        XDocument? Doc(string? path)
        {
            try { return path is not null && File.Exists(path) ? XDocument.Load(path) : null; }
            catch (Exception e) when (e is System.Xml.XmlException or IOException) { return null; }
        }

        string T(XElement e, string attr) => texts.Resolve((string?)e.Attribute(attr));
        static string? Ref(XElement e, string attr) => Strip((string?)e.Attribute(attr));

        var kingdoms = Doc(kingdomsXml)?.Descendants("Kingdom")
            .Select(k => new EncKingdom((string)k.Attribute("id")!, T(k, "name"), T(k, "text"), Ref(k, "culture") ?? "",
                Ref(k, "owner"), GameCatalogs.ParseColor((string?)k.Attribute("color2") ?? (string?)k.Attribute("color"))))
            .ToList() ?? [];

        var clans = Doc(clansXml)?.Descendants("Faction")
            .Where(c => c.Attribute("id") is not null)
            .Select(c => new EncClan((string)c.Attribute("id")!, T(c, "name"), T(c, "text"), Ref(c, "culture") ?? "",
                Ref(c, "super_faction"), Ref(c, "owner"), Int(c, "tier"),
                (bool?)c.Attribute("is_minor_faction") == true, (bool?)c.Attribute("is_bandit") == true,
                GameCatalogs.ParseColor((string?)c.Attribute("color")), Ref(c, "initial_home_settlement")))
            .ToList() ?? [];

        // Lordun metni ve ailesi heroes.xml'de; adı, yaşı ve özellikleri lords.xml'de.
        var heroes = Doc(heroesXml)?.Descendants("Hero")
            .Where(h => h.Attribute("id") is not null)
            .GroupBy(h => (string)h.Attribute("id")!)
            .ToDictionary(g => g.Key, g => g.First()) ?? [];

        var lords = Doc(lordsXml)?.Descendants("NPCCharacter")
            .Where(n => (string?)n.Attribute("occupation") == "Lord" && n.Attribute("id") is not null)
            .Select(n =>
            {
                var id = (string)n.Attribute("id")!;
                heroes.TryGetValue(id, out var h);
                var traits = n.Descendants("Trait")
                    .Where(t => t.Attribute("id") is not null)
                    .GroupBy(t => (string)t.Attribute("id")!)
                    .ToDictionary(g => g.Key, g => Int(g.First(), "value"));
                return new EncLord(id, T(n, "name"), h is null ? "" : T(h, "text"), Ref(n, "culture") ?? "",
                    h is null ? null : Ref(h, "faction"), Int(n, "age"), (bool?)n.Attribute("is_female") == true, traits,
                    h is null ? null : Ref(h, "spouse"), h is null ? null : Ref(h, "father"), h is null ? null : Ref(h, "mother"));
            })
            .ToList() ?? [];

        var settlements = Doc(settlementsXml)?.Descendants("Settlement")
            .Where(s => s.Attribute("id") is not null)
            .Select(s =>
            {
                var town = s.Descendants("Town").FirstOrDefault();
                var village = s.Descendants("Village").FirstOrDefault();
                var kind = town is not null
                    ? (bool?)town.Attribute("is_castle") == true ? SettlementKind.Castle : SettlementKind.Town
                    : village is not null ? SettlementKind.Village
                    : s.Descendants("Hideout").Any() ? SettlementKind.Hideout : SettlementKind.Other;
                var buildings = s.Descendants("Building")
                    .Where(b => b.Attribute("id") is not null)
                    .GroupBy(b => (string)b.Attribute("id")!)
                    .ToDictionary(g => g.Key, g => Int(g.First(), "level"));
                return new EncSettlement((string)s.Attribute("id")!, T(s, "name"), T(s, "text"), Ref(s, "culture") ?? "", kind,
                    Dbl(s, "posX"), Dbl(s, "posY"), Ref(s, "owner"), town is null ? 0 : Int(town, "prosperity"),
                    village is null ? 0 : Int(village, "hearth"), village is null ? null : Ref(village, "bound"),
                    village is null ? null : Ref(village, "village_type"), buildings);
            })
            .Where(s => s.Kind != SettlementKind.Other)
            .ToList() ?? [];

        var troops = Doc(troopsXml)?.Descendants("NPCCharacter")
            .Where(n => (string?)n.Attribute("occupation") == "Soldier" && n.Attribute("id") is not null)
            .Select(n =>
            {
                var level = Int(n, "level");
                var skills = n.Element("skills")?.Elements("skill")
                    .Where(k => k.Attribute("id") is not null)
                    .GroupBy(k => (string)k.Attribute("id")!)
                    .ToDictionary(g => g.Key, g => Int(g.First(), "value")) ?? [];
                var upgrades = n.Element("upgrade_targets")?.Elements("upgrade_target")
                    .Select(u => Strip((string?)u.Attribute("id")))
                    .OfType<string>()
                    .ToList() ?? [];
                var gear = n.Descendants("EquipmentRoster").FirstOrDefault()?.Elements("equipment")
                    .Select(e => Strip((string?)e.Attribute("id")))
                    .OfType<string>()
                    .ToList() ?? [];
                return new EncTroop((string)n.Attribute("id")!, T(n, "name"), Ref(n, "culture") ?? "", level,
                    GameCatalogs.TierOf(level), (string?)n.Attribute("default_group") ?? "Infantry", upgrades, skills, gear);
            })
            .ToList() ?? [];

        var cultures = Doc(culturesXml)?.Descendants("Culture")
            .Where(c => c.Attribute("id") is not null)
            .GroupBy(c => (string)c.Attribute("id")!)
            .ToDictionary(g => g.Key, g => T(g.First(), "name")) ?? [];

        return new EncyclopediaData
        {
            Kingdoms = kingdoms,
            Clans = clans,
            Lords = lords,
            Settlements = settlements,
            Troops = troops,
            Cultures = cultures,
        };
    }

    /// <summary><c>Faction.clan_x</c> → <c>clan_x</c>.</summary>
    private static string? Strip(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var dot = reference.IndexOf('.');
        return dot >= 0 ? reference[(dot + 1)..] : reference;
    }

    private static int Int(XElement e, string attr) =>
        double.TryParse((string?)e.Attribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;

    private static double Dbl(XElement e, string attr) =>
        double.TryParse((string?)e.Attribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
}
