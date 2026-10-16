using System.Text.RegularExpressions;

namespace CoopKeep.Core.Protocol;

/// <summary>Konsol satırının önemi.</summary>
public enum LogSeverity { Harmless, Info, Warning, Error }

/// <summary>Tanınan bir konsol satırı ve ne anlama geldiği.</summary>
/// <param name="Key">Yerelleştirme anahtarı (<c>log.&lt;Key&gt;</c>).</param>
public sealed record KnownLogLine(string Key, LogSeverity Severity, Regex Pattern);

/// <summary>
/// Sunucu konsolunda sık görülen satırların açıklamaları.
/// </summary>
/// <remarks>
/// Konsol binlerce teknik satır basıyor; kullanıcı hangisinin sorun olduğunu bilemiyor.
/// Buradaki desenler canlı Coop v0.1.6 oturumlarında görülen gerçek satırlardan alındı.
/// Açıklama metinleri arayüzdedir; burada yalnızca eşleşme ve önem tutulur.
/// </remarks>
public static class KnownLogLines
{
    private static KnownLogLine L(string key, LogSeverity severity, string pattern) =>
        new(key, severity, new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));

    public static readonly IReadOnlyList<KnownLogLine> All =
    [
        L("dllNoise", LogSeverity.Harmless, @"Cannot load: .*\.dll|Could not load file or assembly"),
        L("eventIndex", LogSeverity.Harmless, @"Could not find the event index for"),
        L("eula", LogSeverity.Error, @"EULA acceptance required"),
        L("moduleMismatch", LogSeverity.Warning, @"module mismatch: .*Forcing load"),
        L("unsupportedModule", LogSeverity.Error, @"(Server does not support module|DLC is not supported)"),
        L("wrongVersion", LogSeverity.Error, @"(Wrong game version|Incompatible co-op mod build|incompatible version of the mod)"),
        L("cheatDisabled", LogSeverity.Info, @"Cheat mode is disabled"),
        L("portInUse", LogSeverity.Error, @"(address already in use|Only one usage of each socket address|failed to bind)"),
        L("moduleVerify", LogSeverity.Error, @"(release pins|verification failed).*(mismatch|fail)"),
        L("saved", LogSeverity.Info, @"world saved to '"),
        L("steamOff", LogSeverity.Info, @"Steam discoverability disabled"),
        L("steamLobby", LogSeverity.Info, @"Steam lobby .* created"),
        L("serving", LogSeverity.Info, @"\] SERVING"),
        L("accessViolation", LogSeverity.Error, @"exited unexpectedly \(code -1073741819\)"),
        L("loadFailed", LogSeverity.Error, @"(load failure|failed to load|LoadGame failed)"),
    ];

    /// <summary>Satırla eşleşen ilk tanım; yoksa <see langword="null"/>.</summary>
    public static KnownLogLine? Match(string line)
    {
        foreach (var known in All)
            if (known.Pattern.IsMatch(line)) return known;
        return null;
    }

    /// <summary>Satır bir hata ya da uyarı mı? ("yalnızca sorunlar" süzgeci için)</summary>
    public static bool IsProblem(string line)
    {
        var known = Match(line);
        if (known is not null) return known.Severity is LogSeverity.Error or LogSeverity.Warning;

        return line.Contains("ERROR", StringComparison.Ordinal)
               || line.Contains("Exception", StringComparison.Ordinal)
               || line.StartsWith("! ", StringComparison.Ordinal);
    }
}
