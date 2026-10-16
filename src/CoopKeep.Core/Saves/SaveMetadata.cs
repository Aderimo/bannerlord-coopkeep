using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CoopKeep.Core.Saves;

/// <summary>
/// Bir <c>.sav</c> dosyasının başlığındaki bilgiler.
/// </summary>
/// <remarks>
/// <para>
/// Bannerlord her save'in başına düz bir JSON yazar:
/// <c>{"List":{"Modules":"Native;SandBoxCore;...","CharacterName":"...","MainHeroLevel":"12",...}}</c>.
/// Sıkıştırılmış oyun verisi bunun arkasından gelir; başlığı okumak için dosyanın
/// yalnızca ilk birkaç kilobaytı yeterli.
/// </para>
/// <para>
/// <b>Neden önemli:</b> 2026-10-16'da 27 modla oluşturulmuş bir save sunucuya verildi.
/// Coop her eksik modül için <c>module mismatch ... Forcing load anyway</c> yazıp yüklemeyi
/// zorladı ve sunucu <c>0xC0000005</c> (erişim ihlali) ile çöktü. İçe aktarmadan önce
/// mod listesini kontrol etmek bu çöküşü kullanıcıya hiç yaşatmamak demek.
/// </para>
/// </remarks>
public sealed record SaveMetadata
{
    public IReadOnlyList<string> Modules { get; init; } = Array.Empty<string>();
    public string? CharacterName { get; init; }
    public int? HeroLevel { get; init; }
    public int? Gold { get; init; }
    public double? Day { get; init; }
    public string? GameVersion { get; init; }
    public bool Ironman { get; init; }

    /// <summary>Sunucunun yükleyebildiği resmî modüller.</summary>
    private static readonly HashSet<string> OfficialModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode", "Multiplayer",
        "Coop", "CoopNightly", "DedicatedServer.Windows",
    };

    /// <summary>Resmî ama Coop'un desteklemediği eklentiler (War Sails vb.).</summary>
    private static readonly HashSet<string> UnsupportedOfficial = new(StringComparer.OrdinalIgnoreCase)
    {
        "NavalDLC", "BirthAndDeath", "FastMode",
    };

    /// <summary>Sunucuda bulunmayan, üçüncü parti modlar. Varsa save sunucuyu çökertebilir.</summary>
    public IReadOnlyList<string> ThirdPartyModules =>
        Modules.Where(m => !OfficialModules.Contains(m) && !UnsupportedOfficial.Contains(m)).ToList();

    /// <summary>Coop'un açıkça desteklemediği resmî eklentiler.</summary>
    public IReadOnlyList<string> UnsupportedOfficialModules =>
        Modules.Where(m => UnsupportedOfficial.Contains(m)).ToList();

    /// <summary>Sunucuda güvenle açılabilir mi?</summary>
    public bool IsServerCompatible => ThirdPartyModules.Count == 0;

    /// <summary>Başlığı okunamayan dosyalar için <see langword="null"/>.</summary>
    public static SaveMetadata? TryRead(string savePath)
    {
        try
        {
            using var stream = File.Open(savePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[64 * 1024];
            var read = stream.Read(buffer, 0, buffer.Length);
            return TryParse(buffer.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SaveMetadata? TryParse(ReadOnlySpan<byte> header)
    {
        var marker = "{\"List\":"u8;
        var start = header.IndexOf(marker);
        if (start < 0) return null;

        var end = FindObjectEnd(header, start);
        if (end < 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(header[start..end].ToArray());
            if (!doc.RootElement.TryGetProperty("List", out var list) || list.ValueKind != JsonValueKind.Object)
                return null;

            string? Get(string key) =>
                list.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            int? GetInt(string key) =>
                double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)d : null;

            return new SaveMetadata
            {
                Modules = (Get("Modules") ?? "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                CharacterName = Get("CharacterName"),
                HeroLevel = GetInt("MainHeroLevel"),
                Gold = GetInt("MainHeroGold"),
                Day = double.TryParse(Get("DayLong"), NumberStyles.Float, CultureInfo.InvariantCulture, out var day) ? day : null,
                GameVersion = Get("ApplicationVersion"),
                Ironman = Get("IronmanMode") == "1",
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Dengeli süslü parantezle biten JSON nesnesinin sonunu bulur (string içleri atlanır).</summary>
    private static int FindObjectEnd(ReadOnlySpan<byte> data, int start)
    {
        var depth = 0;
        var inString = false;

        for (var i = start; i < data.Length; i++)
        {
            var c = data[i];

            if (inString)
            {
                if (c == (byte)'\\') { i++; continue; }
                if (c == (byte)'"') inString = false;
                continue;
            }

            switch (c)
            {
                case (byte)'"': inString = true; break;
                case (byte)'{': depth++; break;
                case (byte)'}':
                    depth--;
                    if (depth == 0) return i + 1;
                    break;
            }
        }

        return -1;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{CharacterName} lvl {HeroLevel}, {Modules.Count} modules");
}
