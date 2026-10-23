namespace CoopKeep.Core.Coop;

/// <summary>Bir denetimin sonucu ne kadar önemli.</summary>
public enum CheckSeverity
{
    /// <summary>Sorun yok.</summary>
    Ok,

    /// <summary>Bilgi: bilmekte yarar var, engel değil.</summary>
    Info,

    /// <summary>Çalışır ama bir şey ters gidebilir (ör. arkadaşlar bulamayabilir).</summary>
    Warning,

    /// <summary>Düzelmeden Coop çalışmaz.</summary>
    Error,
}

/// <summary>Programın sunabileceği tek tıklık düzeltme.</summary>
public enum CheckFix
{
    None,
    OpenGameStore,
    OpenCoopWorkshop,
    DisableDlc,
    UnblockFiles,
    AddFirewallRule,
    StartSteam,
    OpenBackups,
    OpenWorldMods,
    OpenModsPage,

    /// <summary>Dünyanın eksik modlarını indir: Atölye'de abone ol, bilinmeyeni Mod bul'da ara.</summary>
    GetMissingMods,
}

/// <summary>Bir denetimin sonucu: metin anahtarı (<c>check.&lt;Id&gt;</c>), önemi, düzeltmesi.</summary>
/// <param name="Values">Metindeki <c>{0}</c>, <c>{1}</c>… yerine gelecekler.</param>
public sealed record CheckResult(string Id, CheckSeverity Severity, CheckFix Fix = CheckFix.None, IReadOnlyList<string>? Values = null)
{
    public IReadOnlyList<string> Args => Values ?? [];
}

/// <summary>Kurulum denetiminin girdileri: bilgisayarda bulunanlar.</summary>
public sealed record SetupFacts
{
    public bool GameFound { get; init; }
    public string? GameVersion { get; init; }
    public bool CoopFound { get; init; }
    public string? CoopVersion { get; init; }
    public string? CoopRequiredGameVersion { get; init; }
    public bool ServerPackageFound { get; init; }

    /// <summary>Başlatıcıda açık olan ve Coop'un reddettiği resmî modüller (War Sails gibi).</summary>
    public IReadOnlyList<string> EnabledBlockedModules { get; init; } = [];

    /// <summary>Windows'un "internetten indirildi" diye engellediği DLL/EXE sayısı.</summary>
    public int BlockedFiles { get; init; }

    /// <summary>Güvenlik duvarı kuralı var mı (bilinmiyorsa <c>null</c>).</summary>
    public bool? FirewallOk { get; init; }
}

/// <summary>Sunucuyu açmadan önceki denetimin girdileri.</summary>
public sealed record PreflightFacts
{
    public required string WorldName { get; init; }
    public bool WorldSaveExists { get; init; }

    /// <summary>Sunucu Steam lobisi açacak mı (<c>server-config.json</c> → <c>steam</c>)?</summary>
    public bool SteamLobbyEnabled { get; init; }
    public bool SteamRunning { get; init; }

    public bool DocumentsOnOneDrive { get; init; }

    /// <summary>Coop'un okuduğu ortam değişkenleri (ad → değer); yalnızca tanımlı olanlar.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Dünyanın modları; boşsa modsuz dünya.</summary>
    public IReadOnlyList<string> WorldMods { get; init; } = [];

    /// <summary>Dünyanın modlarından bu bilgisayarda kurulu olmayanlar.</summary>
    public IReadOnlyList<string> MissingWorldMods { get; init; } = [];

    public int Port { get; init; }
}

/// <summary>
/// Coop'un insanları en çok zorlayan tuzakları: kurulumda ve sunucuyu açmadan önce denetlenir.
/// </summary>
/// <remarks>
/// <para>
/// Kaynak: Coop'un sunucu belgeleri (<c>release-info.txt</c>), README'si, GitHub'daki hata
/// bildirimleri ve CoopKeep kullanıcılarının bildirdikleri (2026-10-22 taraması). Her madde ya
/// tek tıkla düzeltilir ya da ne yapılacağı açıkça söylenir. Coop'un kendisine dokunulmaz.
/// </para>
/// <para>
/// Saf işlevler: girdiler dışarıda toplanır (dosya, süreç, kayıt defteri), burada yalnızca
/// karar verilir. Böylece her kural birim testiyle sabitlenir.
/// </para>
/// </remarks>
public static class CoopChecks
{
    /// <summary>Coop'un okuduğu ve yanlış verilince sessizce bozulan ortam değişkenleri.</summary>
    public static readonly string[] CoopEnvironmentVariables = ["COOP_DATA_DIR", "BANNERLORD_USER_DIR", "COOP_STEAM_BAN_FILE"];

    /// <summary>Kurulum: oyun → sürüm → Coop → sunucu paketi → DLC → engellenmiş dosyalar → güvenlik duvarı.</summary>
    public static IReadOnlyList<CheckResult> Setup(SetupFacts f)
    {
        var list = new List<CheckResult>();

        if (!f.GameFound)
        {
            list.Add(new("gameMissing", CheckSeverity.Error, CheckFix.OpenGameStore));
            return list;
        }

        list.Add(new("gameFound", CheckSeverity.Ok, Values: [f.GameVersion ?? "?"]));

        if (!f.CoopFound)
        {
            list.Add(new("coopMissing", CheckSeverity.Error, CheckFix.OpenCoopWorkshop));
            return list;
        }

        list.Add(new("coopFound", CheckSeverity.Ok, Values: [f.CoopVersion ?? "?"]));

        // Coop, oyunun sürümünü derleme numarası olmadan karşılaştırıyor (v1.4.8 = v1.4.8.118999).
        if (f.CoopRequiredGameVersion is { } need && f.GameVersion is { } have && !SameGameVersion(need, have))
            list.Add(new("versionMismatch", CheckSeverity.Error, CheckFix.None, [have, need]));

        if (!f.ServerPackageFound)
            list.Add(new("serverMissing", CheckSeverity.Error, CheckFix.OpenCoopWorkshop));

        if (f.EnabledBlockedModules.Count > 0)
            list.Add(new("dlcEnabled", CheckSeverity.Error, CheckFix.DisableDlc, [string.Join(", ", f.EnabledBlockedModules)]));

        if (f.BlockedFiles > 0)
            list.Add(new("filesBlocked", CheckSeverity.Error, CheckFix.UnblockFiles, [f.BlockedFiles.ToString()]));

        if (f.FirewallOk == false)
            list.Add(new("firewallMissing", CheckSeverity.Warning, CheckFix.AddFirewallRule));

        return list;
    }

    /// <summary>Sunucuyu açmadan önce: açılırsa sorun çıkacak ya da kullanıcının bilmesi gerekenler.</summary>
    public static IReadOnlyList<CheckResult> Preflight(PreflightFacts f)
    {
        var list = new List<CheckResult>();

        // Kayıt yoksa Coop sessizce yeni bir dünya başlatıyor; kullanıcı dünyasını kaybettiğini sanıyor.
        if (!f.WorldSaveExists)
            list.Add(new("saveMissing", CheckSeverity.Warning, CheckFix.OpenBackups, [f.WorldName]));

        if (f.MissingWorldMods.Count > 0)
            list.Add(new("modsMissing", CheckSeverity.Error, CheckFix.GetMissingMods, [string.Join(", ", f.MissingWorldMods)]));

        // Steam kapalıysa sunucu yalnızca doğrudan bağlantıyla açılır; Steam'de aranınca bulunmaz.
        if (f.SteamLobbyEnabled && !f.SteamRunning)
            list.Add(new("steamNotRunning", CheckSeverity.Warning, CheckFix.StartSteam, [f.Port.ToString()]));

        foreach (var name in CoopEnvironmentVariables)
        {
            if (!f.Environment.TryGetValue(name, out var value)) continue;
            if (value.Contains('"') || !Path.IsPathRooted(value.Trim()))
                list.Add(new("envInvalid", CheckSeverity.Warning, CheckFix.None, [name, value]));
            else
                list.Add(new("envSet", CheckSeverity.Info, CheckFix.None, [name, value]));
        }

        if (f.DocumentsOnOneDrive)
            list.Add(new("oneDrive", CheckSeverity.Info));

        if (f.WorldMods.Count > 0)
            list.Add(new("moddedWorld", CheckSeverity.Info, CheckFix.OpenWorldMods, [f.WorldMods.Count.ToString()]));

        return list;
    }

    /// <summary>Sunucu yine de açılmalı mı? Hata varsa hayır; uyarıda kullanıcıya sorulur.</summary>
    public static bool HasErrors(IEnumerable<CheckResult> results) => results.Any(r => r.Severity == CheckSeverity.Error);

    public static bool HasWarnings(IEnumerable<CheckResult> results) => results.Any(r => r.Severity == CheckSeverity.Warning);

    /// <summary>Oyun sürümü eşit mi (baştaki "v"/"e" ve derleme numarası yok sayılır)?</summary>
    public static bool SameGameVersion(string a, string b)
    {
        static string Core(string v)
        {
            var t = v.Trim().TrimStart('v', 'V', 'e', 'E');
            var parts = t.Split('.');
            return string.Join(".", parts.Take(3));
        }

        return string.Equals(Core(a), Core(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Belgeler klasörü OneDrive'ın altında mı?</summary>
    public static bool IsOneDrivePath(string documents) =>
        documents.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => p.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase));
}
