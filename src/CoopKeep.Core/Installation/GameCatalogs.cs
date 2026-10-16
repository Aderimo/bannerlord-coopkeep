using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CoopKeep.Core.Installation;

/// <summary>Verilebilecek bir asker türü.</summary>
/// <param name="Id">Oyunun karakter kimliği (örn. <c>imperial_recruit</c>).</param>
/// <param name="Culture">Kültür kimliği (örn. <c>empire</c>).</param>
/// <param name="Tier">Kademe (1-6), oyunun seviye tablosundan.</param>
public sealed record TroopType(string Id, string Name, string Culture, int Level, int Tier);

/// <summary>Haritadaki bir yerleşim.</summary>
public sealed record SettlementInfo(string Id, string Name, string Culture, SettlementKind Kind, double X, double Y);

public enum SettlementKind { Town, Castle, Village, Hideout, Other }

/// <summary>
/// Oyunun kendi veri dosyalarından okunan listeler: askerler ve yerleşimler.
/// </summary>
/// <remarks>
/// Dosyalar kullanıcının Bannerlord kurulumundan okunur; CoopKeep hiçbir oyun verisini
/// kendi içinde taşımaz. Adlar <c>{=anahtar}Metin</c> biçiminde; çeviri anahtarı atılıp
/// İngilizce metin kullanılır.
/// </remarks>
public static partial class GameCatalogs
{
    [GeneratedRegex(@"^\{=[^}]*\}")]
    private static partial Regex LocalizationKey();

    public static string CleanName(string? raw) =>
        LocalizationKey().Replace(raw ?? "", "").Trim();

    /// <summary>
    /// Askerlik yapan karakterler (<c>occupation="Soldier"</c>), kültür ve kademeye göre.
    /// </summary>
    public static IReadOnlyList<TroopType> ReadTroops(string spnpccharactersXml)
    {
        var doc = XDocument.Load(spnpccharactersXml);

        return doc.Root!.Elements("NPCCharacter")
            .Where(e => (string?)e.Attribute("occupation") == "Soldier")
            .Where(e => (string?)e.Attribute("is_template") != "true")
            .Select(e =>
            {
                var level = int.TryParse((string?)e.Attribute("level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;
                var culture = ((string?)e.Attribute("culture") ?? "").Replace("Culture.", "");
                return new TroopType((string)e.Attribute("id")!, CleanName((string?)e.Attribute("name")), culture, level, TierOf(level));
            })
            .Where(t => t.Name.Length > 0 && t.Culture.Length > 0)
            .OrderBy(t => t.Culture, StringComparer.Ordinal)
            .ThenBy(t => t.Tier)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Oyunun seviye → kademe tablosu (6, 11, 16, 21, 26, 31).</summary>
    public static int TierOf(int level) => level switch
    {
        <= 6 => 1,
        <= 11 => 2,
        <= 16 => 3,
        <= 21 => 4,
        <= 26 => 5,
        _ => 6,
    };

    /// <summary>Kasaba, kale, köy ve haydut inlerinin harita konumları.</summary>
    public static IReadOnlyList<SettlementInfo> ReadSettlements(string settlementsXml)
    {
        var doc = XDocument.Load(settlementsXml);

        return doc.Root!.Elements("Settlement")
            .Select(e =>
            {
                var id = (string)e.Attribute("id")!;
                var components = e.Element("Components");
                var kind =
                    components?.Element("Town") is { } town
                        ? ((string?)town.Attribute("is_castle") == "true" ? SettlementKind.Castle : SettlementKind.Town)
                    : components?.Element("Village") is not null ? SettlementKind.Village
                    : components?.Element("Hideout") is not null ? SettlementKind.Hideout
                    : SettlementKind.Other;

                return new SettlementInfo(
                    id,
                    CleanName((string?)e.Attribute("name")),
                    ((string?)e.Attribute("culture") ?? "").Replace("Culture.", ""),
                    kind,
                    ParseDouble((string?)e.Attribute("posX")),
                    ParseDouble((string?)e.Attribute("posY")));
            })
            .Where(s => s.Kind != SettlementKind.Other && s.Name.Length > 0)
            .ToList();
    }

    private static double ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>Kurulu oyundaki veri dosyalarının yolları.</summary>
    public static string TroopsPath(BannerlordInstallation game) =>
        Path.Combine(game.ModulesDirectory, "SandBoxCore", "ModuleData", "spnpccharacters.xml");

    public static string SettlementsPath(BannerlordInstallation game) =>
        Path.Combine(game.ModulesDirectory, "SandBox", "ModuleData", "settlements.xml");
}
