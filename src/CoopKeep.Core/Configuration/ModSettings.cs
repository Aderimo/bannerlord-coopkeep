using System.Globalization;
using System.Text.Json;

namespace CoopKeep.Core.Configuration;

/// <summary>Bir dünya ayarının değer türü.</summary>
public enum ModSettingKind
{
    /// <summary><c>"VeryEasy" | "Easy" | "Realistic"</c> — oyunun kendi zorluk seviyeleri.</summary>
    Level,
    Bool,
    Int,
    Number,

    /// <summary>Sabit metin seçenekleri (<see cref="ModSettingDefinition.Choices"/>).</summary>
    Choice,
}

/// <summary>
/// <c>mod-config.json</c> içindeki tek bir oyun ayarının tanımı.
/// </summary>
/// <param name="Section"><c>difficulty</c> veya <c>modOptions</c>.</param>
/// <param name="Key">Dosyadaki anahtar.</param>
/// <param name="Group">Arayüzde gruplamak için.</param>
/// <param name="Default">Modun kendi varsayılanı (<c>mod-config.default.json</c>).</param>
public sealed record ModSettingDefinition(
    string Section,
    string Key,
    ModSettingKind Kind,
    ModSettingGroup Group,
    object Default,
    double Min = 0,
    double Max = 0,
    IReadOnlyList<string>? Choices = null)
{
    public string Id => Section + "." + Key;
}

public enum ModSettingGroup
{
    /// <summary>Oyunda yeni kampanya açarken çıkan zorluk ekranı.</summary>
    Difficulty,

    /// <summary>Ölüm, yaşlanma, infaz.</summary>
    LifeAndDeath,

    /// <summary>Savaş boyutu, haydutlar, yapay zekâ.</summary>
    World,

    /// <summary>Hız, duraklatma, ekonomi — çok oyunculu oyuna özgü.</summary>
    Multiplayer,

    /// <summary>Hileler.</summary>
    Cheats,
}

/// <summary>
/// CoopKeep'in arayüzde sunduğu dünya ayarları.
/// </summary>
/// <remarks>
/// <para>
/// Kaynak: Coop v0.1.6 ile gelen <c>mod-config.default.json</c> (2026-10-16). Sunucunun
/// kendi açıklaması: <i>"Campaign difficulty, applied by the HOSTING side at every boot;
/// joining players inherit the values with the save transfer."</i> — yani oyunda yeni
/// kampanya açarken yapılan zorluk seçimlerinin birebir karşılığı ve her açılışta
/// yeniden uygulanıyor.
/// </para>
/// <para>
/// Dosyadaki diğer anahtarlar (ses mesafeleri, demirci dayanıklılığı vb.) burada yok ama
/// <see cref="JsoncEditor"/> sayesinde dokunulmadan korunuyor.
/// </para>
/// </remarks>
public static class ModSettingsCatalog
{
    public const string DifficultySection = "difficulty";
    public const string OptionsSection = "modOptions";
    public const string VoiceSection = "voice";

    public static readonly IReadOnlyList<string> Levels = ["VeryEasy", "Easy", "Realistic"];

    private static ModSettingDefinition Level(string key, ModSettingGroup group = ModSettingGroup.Difficulty) =>
        new(DifficultySection, key, ModSettingKind.Level, group, "VeryEasy", Choices: Levels);

    public static readonly IReadOnlyList<ModSettingDefinition> All =
    [
        // --- Zorluk (oyunun "Yeni kampanya" ekranı) ---------------------
        Level("playerReceivedDamage"),
        Level("playerTroopsReceivedDamage"),
        Level("combatAIDifficulty"),
        Level("recruitmentDifficulty"),
        Level("playerMapMovementSpeed"),
        Level("stealthAndDisguiseDifficulty"),
        Level("persuasionSuccessChance"),

        // --- Yaşam ve ölüm ---------------------------------------------
        Level("clanMemberDeathChance", ModSettingGroup.LifeAndDeath),
        Level("battleDeath", ModSettingGroup.LifeAndDeath),
        new(DifficultySection, "birthAndDeath", ModSettingKind.Bool, ModSettingGroup.LifeAndDeath, true),
        new(DifficultySection, "autoAllocateClanMemberPerks", ModSettingKind.Bool, ModSettingGroup.LifeAndDeath, false),
        new(OptionsSection, "enableHeroExecutions", ModSettingKind.Bool, ModSettingGroup.LifeAndDeath, false),
        new(OptionsSection, "enablePlayerClanMemberExecutions", ModSettingKind.Bool, ModSettingGroup.LifeAndDeath, false),
        new(OptionsSection, "enablePlayerExecutions", ModSettingKind.Bool, ModSettingGroup.LifeAndDeath, false),

        // --- Dünya -----------------------------------------------------
        new(OptionsSection, "battleSize", ModSettingKind.Int, ModSettingGroup.World, 1000, 200, 1000),
        new(OptionsSection, "maximumLootersMultiplier", ModSettingKind.Number, ModSettingGroup.World, 1.0, 0.1, 5),
        new(OptionsSection, "looterPartySizeMultiplier", ModSettingKind.Number, ModSettingGroup.World, 1.0, 0.1, 5),
        new(OptionsSection, "wandererLimit", ModSettingKind.Int, ModSettingGroup.World, 32, 0, 200),
        new(OptionsSection, "ensureUnaffiliatedWanderers", ModSettingKind.Bool, ModSettingGroup.World, true),
        new(OptionsSection, "wandererLimitScalesWithPlayers", ModSettingKind.Bool, ModSettingGroup.World, false),
        new(OptionsSection, "smithingStaminaRecoveryOutsideSettlements", ModSettingKind.Bool, ModSettingGroup.World, true),
        new(OptionsSection, "smithingStaminaRecoveryMultiplier", ModSettingKind.Number, ModSettingGroup.World, 0.1, 0.1, 5),
        new(OptionsSection, "playerKingdomClanTierRequired", ModSettingKind.Int, ModSettingGroup.World, 4, 0, 6),
        new(OptionsSection, "blockAiWarDeclarationsOnOfflinePlayers", ModSettingKind.Bool, ModSettingGroup.World, false),
        new(OptionsSection, "playerBattleAiJoinWindowHours", ModSettingKind.Int, ModSettingGroup.World, 6, 0, 48),

        // --- Çok oyunculu ----------------------------------------------
        new(OptionsSection, "fastForwardEnabled", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(OptionsSection, "autoPauseEnabled", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(OptionsSection, "speedLimitWhilePlayersInBattle", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(OptionsSection, "playerWoundedBattleEntry", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(OptionsSection, "voiceEnabled", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(VoiceSection, "mapMaximumDistance", ModSettingKind.Int, ModSettingGroup.Multiplayer, 20, 5, 100),
        new(VoiceSection, "sceneMaximumDistance", ModSettingKind.Int, ModSettingGroup.Multiplayer, 50, 10, 200),
        new(OptionsSection, "showPlayerNameplates", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(OptionsSection, "coopClansEnabled", ModSettingKind.Bool, ModSettingGroup.Multiplayer, true),
        new(OptionsSection, "goldFoodInfluenceChangeForDisconnectedPlayers", ModSettingKind.Bool, ModSettingGroup.Multiplayer, false),
        new(OptionsSection, "goldFoodInfluenceChangeInSettlements", ModSettingKind.Bool, ModSettingGroup.Multiplayer, false),
        new(OptionsSection, "goldFoodInfluenceChangeInBattles", ModSettingKind.Choice, ModSettingGroup.Multiplayer,
            "Disabled", Choices: ["Disabled", "OneDayMax", "Enabled"]),
        new(OptionsSection, "lordDefectionRetries", ModSettingKind.Choice, ModSettingGroup.Multiplayer,
            "Vanilla", Choices: ["Vanilla", "NeverExpire", "AlwaysRetry"]),

        // --- Hileler ---------------------------------------------------
        new(OptionsSection, "clientsCanUseCheats", ModSettingKind.Bool, ModSettingGroup.Cheats, false),
    ];

    public static ModSettingDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    /// <summary>Modun varsayılanları.</summary>
    public static Dictionary<string, object> Defaults() => All.ToDictionary(d => d.Id, d => d.Default);

    /// <summary>
    /// Zorluk ön ayarı: tüm seviye ayarlarını aynı seviyeye çeker.
    /// </summary>
    /// <remarks>Oyunun yeni kampanya ekranındaki "Kolay / Normal / Gerçekçi" düğmeleriyle aynı davranış.</remarks>
    public static void ApplyPreset(IDictionary<string, object> values, string level)
    {
        if (!Levels.Contains(level))
            throw new ArgumentException($"Bilinmeyen seviye: '{level}'", nameof(level));

        foreach (var d in All.Where(d => d.Kind == ModSettingKind.Level))
            values[d.Id] = level;
    }

    /// <summary>Tüm seviye ayarları aynıysa o seviye, değilse <see langword="null"/> (özel).</summary>
    public static string? DetectPreset(IReadOnlyDictionary<string, object> values)
    {
        var levels = All.Where(d => d.Kind == ModSettingKind.Level)
            .Select(d => values.TryGetValue(d.Id, out var v) ? v as string : null)
            .Distinct()
            .ToList();

        return levels.Count == 1 ? levels[0] : null;
    }

    /// <summary>Değeri tanıma göre doğrular ve sınırlar; geçersizse varsayılana döner.</summary>
    public static object Normalize(ModSettingDefinition d, object? value)
    {
        switch (d.Kind)
        {
            case ModSettingKind.Bool:
                return value is bool b ? b : d.Default;

            case ModSettingKind.Int:
                if (!TryNumber(value, out var i)) return d.Default;
                return (int)Math.Clamp(Math.Round(i), d.Min, d.Max);

            case ModSettingKind.Number:
                if (!TryNumber(value, out var n)) return d.Default;
                return Math.Round(Math.Clamp(n, d.Min, d.Max), 2);

            default:
                return value is string s && d.Choices is { } c && c.Contains(s) ? s : d.Default;
        }
    }

    /// <summary>Değeri <c>mod-config.json</c>'a yazılacak JSON literaline çevirir.</summary>
    public static string ToJsonLiteral(ModSettingDefinition d, object value) => Normalize(d, value) switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        double n => n.ToString("0.0#", CultureInfo.InvariantCulture),
        string s => JsonSerializer.Serialize(s),
        var other => throw new InvalidOperationException($"Desteklenmeyen değer: {other}"),
    };

    /// <summary>JSON değerini tanımın türüne çevirir.</summary>
    public static object FromJson(ModSettingDefinition d, JsonElement e) => d.Kind switch
    {
        ModSettingKind.Bool when e.ValueKind is JsonValueKind.True or JsonValueKind.False => e.GetBoolean(),
        ModSettingKind.Int or ModSettingKind.Number when e.ValueKind == JsonValueKind.Number =>
            Normalize(d, e.GetDouble()),
        ModSettingKind.Level or ModSettingKind.Choice when e.ValueKind == JsonValueKind.String =>
            Normalize(d, e.GetString()),
        _ => d.Default,
    };

    private static bool TryNumber(object? value, out double result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double d when double.IsFinite(d): result = d; return true;
            case float f when float.IsFinite(f): result = f; return true;
            case decimal m: result = (double)m; return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p):
                result = p; return true;
            default: result = 0; return false;
        }
    }
}
