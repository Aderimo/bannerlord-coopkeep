using System.Globalization;
using System.Text.RegularExpressions;

namespace CoopKeep.Core.Protocol;

/// <summary><c>coop.debug.hero.info</c> çıktısından okunan kahraman bilgisi.</summary>
public sealed record HeroInfo(string? Name, int? Gold, int? Health, int? Age)
{
    /// <summary>Kahramanın partisinin kimliği (<c>_partyBelongedTo</c>); partisi yoksa null.</summary>
    public string? PartyId { get; init; }

    /// <summary>Klan adı (<c>_clan = (1207959553) Therycos</c> → <c>Therycos</c>).</summary>
    public string? ClanName { get; init; }
}

/// <summary>Bir yönetici komutunun sonucu.</summary>
public enum AdminOutcome
{
    /// <summary>Sunucu işlemin yapıldığını doğruladı.</summary>
    Success,

    /// <summary>Hedef kahraman bulunamadı.</summary>
    NotFound,

    /// <summary>Komut bu Coop sürümünde yok.</summary>
    UnknownCommand,

    /// <summary>Hile modu kapalı (vanilla hile komutları).</summary>
    CheatsDisabled,

    /// <summary>Sunucu tanıdığımız bir yanıt vermedi.</summary>
    NoResponse,

    /// <summary>
    /// Komut kabul edildi, hata yok; ama başarı mesajının biçimi henüz doğrulanmadı.
    /// Arayüz "yapıldı" değil "gönderildi" der.
    /// </summary>
    Sent,

    /// <summary>Komut parametreleri reddedildi (<c>Usage:</c> yanıtı).</summary>
    InvalidArguments,
}

/// <summary>
/// Yönetici komutlarının konsol yanıtlarını yorumlar.
/// </summary>
/// <remarks>
/// Yanıt biçimleri Coop v0.1.6 sunucusundan birebir alındı (bkz. test fikstürü
/// <c>admin-responses.txt</c>). Eski sürüm sonucu hiç okumuyordu: başarısız bir komut da
/// denetim kaydına "yapıldı" diye yazılıyordu.
/// </remarks>
public static partial class AdminResponseParser
{
    [GeneratedRegex(@"HERO_GOLD_SET hero=(?<hero>\S+) oldGold=(?<old>-?\d+) newGold=(?<new>-?\d+)")]
    private static partial Regex GoldSet();

    [GeneratedRegex(@"^Hero HitPoints changed to: (?<hp>\d+)")]
    private static partial Regex HitPointsChanged();

    [GeneratedRegex(@"^Set age to (?<age>\d+) for (?<count>\d+) hero")]
    private static partial Regex AgeSet();

    [GeneratedRegex(@"^(Unable to find hero with (id|name): |No hero named .* found|ID: '.*' not found|.* not found\.?$)")]
    private static partial Regex NotFound();

    /// <summary>Bu satır, gönderilen komutun yanıtının sonu mu?</summary>
    public static bool IsTerminal(string line) =>
        GoldSet().IsMatch(line) || HitPointsChanged().IsMatch(line) || AgeSet().IsMatch(line) ||
        NotFound().IsMatch(line) || IsUnknownCommand(line) || IsCheatsDisabled(line) ||
        // hero.info dökümünün son alanı.
        line.StartsWith("_heroDeveloper = ", StringComparison.Ordinal);

    public static bool IsUnknownCommand(string line) =>
        line.Contains("no game console command", StringComparison.Ordinal);

    public static bool IsCheatsDisabled(string line) =>
        line.Contains("Cheat mode is disabled", StringComparison.Ordinal);

    /// <summary>Komut yanıtındaki satırlardan sonucu çıkarır.</summary>
    public static AdminOutcome Classify(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (IsUnknownCommand(line)) return AdminOutcome.UnknownCommand;

            // Diğer günlük satırları ("[Coop] …", "[DedicatedServer] …") komutun yanıtı değil.
            if (line.StartsWith('[')) continue;

            if (GoldSet().IsMatch(line) || HitPointsChanged().IsMatch(line)) return AdminOutcome.Success;
            if (AgeSet().Match(line) is { Success: true } age)
                return age.Groups["count"].Value == "0" ? AdminOutcome.NotFound : AdminOutcome.Success;
            if (NotFound().IsMatch(line)) return AdminOutcome.NotFound;
            if (IsUnknownCommand(line)) return AdminOutcome.UnknownCommand;
            if (IsCheatsDisabled(line)) return AdminOutcome.CheatsDisabled;
            if (line.StartsWith("Usage:", StringComparison.Ordinal)) return AdminOutcome.InvalidArguments;
        }

        // Hata satırı yok ama sunucu bir şey yazdı: komut çalıştı, biçimini bilmiyoruz.
        return lines.Any(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('[') && !l.StartsWith("@DS@", StringComparison.Ordinal))
            ? AdminOutcome.Sent
            : AdminOutcome.NoResponse;
    }

    /// <summary><c>HERO_GOLD_SET</c> satırından eski ve yeni altını okur.</summary>
    public static (int Old, int New)? ParseGoldSet(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var m = GoldSet().Match(line);
            if (m.Success)
                return (int.Parse(m.Groups["old"].Value, CultureInfo.InvariantCulture),
                        int.Parse(m.Groups["new"].Value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>
    /// <c>coop.debug.hero.info</c> dökümünden ad, altın, can ve yaşı okur.
    /// </summary>
    /// <remarks>
    /// Döküm kahraman nesnesinin tüm alanlarını <c>alan = değer</c> biçiminde yazıyor.
    /// Yaş: <c>_defaultAge</c> (set_age ile değiştiğini doğruladık).
    /// </remarks>
    public static HeroInfo? ParseHeroInfo(IEnumerable<string> lines)
    {
        string? name = null, party = null, clan = null;
        int? gold = null, health = null, age = null;
        var any = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq <= 0) continue;

            var key = line[..eq];
            var value = line[(eq + 3)..].Trim();

            switch (key)
            {
                case "_name": name = value; any = true; break;
                case "_gold": gold = ParseInt(value); any = true; break;
                case "_health": health = ParseInt(value); any = true; break;
                case "_defaultAge": age = ParseInt(value); any = true; break;
                case "_partyBelongedTo": party = value.Split(':', 2)[0].Trim(); break;
                case "_clan": clan = Regex.Replace(value, @"^\(\d+\)\s*", ""); break;
            }
        }

        return any
            ? new HeroInfo(name, gold, health, age)
            {
                PartyId = string.IsNullOrWhiteSpace(party) ? null : party,
                ClanName = string.IsNullOrWhiteSpace(clan) ? null : clan,
            }
            : null;

        static int? ParseInt(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)d : null;
    }

    /// <summary>
    /// <c>Anahtar: Değer</c> listesini okur (<c>campaign_options.list</c>, <c>mod_config.list</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseKeyValueList(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0 || line.StartsWith('[')) continue;

            var key = line[..colon];
            if (!key.All(char.IsLetterOrDigit)) continue;

            result[key] = line[(colon + 2)..].Trim();
        }

        return result;
    }
}
