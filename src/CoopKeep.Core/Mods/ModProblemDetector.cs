using System.Text.RegularExpressions;

namespace CoopKeep.Core.Mods;

/// <summary>Sorunun türü; arayüz buna göre açıklama ve öneri gösterir.</summary>
public enum ModProblemKind
{
    /// <summary>Modun kodu sunucuda yüklenemedi ya da çalışırken hata verdi.</summary>
    CodeFailed,

    /// <summary>Modun verisi (<c>ModuleData</c> XML'leri) sunucuda okunamadı.</summary>
    DataFailed,

    /// <summary>Oyuncuda bu mod var, sunucuda yok ("Server does not support module").</summary>
    PlayerHasExtra,

    /// <summary>Sunucuda bu mod var, oyuncuda yok.</summary>
    PlayerMissing,

    /// <summary>Sürüm farkı: sunucu ve oyuncu farklı sürüm kullanıyor.</summary>
    VersionMismatch,
}

/// <param name="ModId">Sorumlu mod.</param>
/// <param name="Evidence">Kanıt satırı (kısaltılmış).</param>
/// <param name="ServerVersion">Sürüm farkında sunucunun sürümü.</param>
/// <param name="PlayerVersion">Sürüm farkında oyuncunun sürümü.</param>
public sealed record ModProblem(
    string ModId,
    ModProblemKind Kind,
    string Evidence,
    string? ServerVersion = null,
    string? PlayerVersion = null);

/// <summary>
/// Sunucu çıktısından (ya da günlük dosyasından) soruna yol açan modu bulur.
/// </summary>
/// <remarks>
/// <para>
/// <b>Gürültüye dikkat:</b> motor her açılışta önce kendi <c>bin</c> kökünde arayıp
/// <c>Cannot load: 0Harmony.dll / Common.dll / SandBox.dll</c> yazıyor, sonra doğru yerden
/// yüklüyor (modsuz açılışta da, 2026-10-17 günlüğü). Bu yüzden yalnızca güçlü işaretlere
/// bakılır: yolu <c>Modules\&lt;mod&gt;\</c> içinde olan hatalar, istisna yığınında modun
/// derleme adı ve oyuncu reddi satırları.
/// </para>
/// <para>
/// Oyuncu reddinin metinleri Coop'un doğrulayıcısından (<c>ModuleValidator</c>, v0.1.6):
/// "Server does not support module 'X'.", "To join the server the module 'X' with version 'V'
/// is required.", "Wrong version of module 'X' detected. Server uses 'A', client uses 'B'."
/// </para>
/// </remarks>
public static class ModProblemDetector
{
    private static readonly Regex Extra = new(@"Server does not support module '(?<id>[^']+)'", RegexOptions.Compiled);
    private static readonly Regex Missing = new(@"the module '(?<id>[^']+)' with version '(?<v>[^']*)' is required", RegexOptions.Compiled);
    private static readonly Regex Version = new(
        @"Wrong version of module '(?<id>[^']+)' detected\. Server uses '(?<s>[^']*)', client uses '(?<c>[^']*)'",
        RegexOptions.Compiled);

    /// <summary>Sunucunun doğrulama günlüğü: <c>module validation: server=[…] client=[…] → REJECT</c>.</summary>
    private static readonly Regex Validation = new(
        @"module validation: server=\[(?<s>[^\]]*)\] client=\[(?<c>[^\]]*)\].*REJECT", RegexOptions.Compiled);

    private static readonly Regex StackFrame = new(@"^\s*(?:at|konum)\s+(?<frame>[\w.`<>+]+)\.", RegexOptions.Compiled);

    private static readonly string[] FailureWords =
        ["Exception", "Could not load", "Cannot load", "error", "ERROR", "failed", "Failed"];

    /// <param name="lines">Sunucu çıktısı.</param>
    /// <param name="mods">Sunucuya kurulmuş modlar.</param>
    public static IReadOnlyList<ModProblem> Detect(IEnumerable<string> lines, IReadOnlyList<PlannedMod> mods)
    {
        var problems = new List<ModProblem>();
        var assemblies = AssemblyNames(mods);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (Version.Match(line) is { Success: true } v)
            {
                problems.Add(new ModProblem(v.Groups["id"].Value, ModProblemKind.VersionMismatch, Short(line),
                    v.Groups["s"].Value, v.Groups["c"].Value));
                continue;
            }

            if (Missing.Match(line) is { Success: true } mi)
            {
                problems.Add(new ModProblem(mi.Groups["id"].Value, ModProblemKind.PlayerMissing, Short(line),
                    mi.Groups["v"].Value));
                continue;
            }

            var extras = Extra.Matches(line);
            if (extras.Count > 0)
            {
                foreach (Match m in extras)
                    problems.Add(new ModProblem(m.Groups["id"].Value, ModProblemKind.PlayerHasExtra, Short(line)));
                continue;
            }

            if (Validation.Match(line) is { Success: true } val)
            {
                problems.AddRange(CompareLists(val.Groups["s"].Value, val.Groups["c"].Value, Short(line)));
                continue;
            }

            if (!FailureWords.Any(w => line.Contains(w, StringComparison.Ordinal)) && !StackFrame.IsMatch(line))
                continue;

            // Yolu modun klasöründe geçen hata.
            foreach (var mod in mods)
            {
                var id = mod.Module.Id;
                var inFolder = line.Contains($@"Modules\{id}\", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains($"Modules/{id}/", StringComparison.OrdinalIgnoreCase);
                if (!inFolder || !FailureWords.Any(w => line.Contains(w, StringComparison.Ordinal))) continue;

                var kind = line.Contains("ModuleData", StringComparison.OrdinalIgnoreCase) || line.Contains(".xml", StringComparison.OrdinalIgnoreCase)
                    ? ModProblemKind.DataFailed
                    : ModProblemKind.CodeFailed;
                problems.Add(new ModProblem(id, kind, Short(line)));
            }

            // İstisna yığınında modun derlemesi.
            if (StackFrame.Match(line) is { Success: true } frame)
            {
                var name = frame.Groups["frame"].Value;
                foreach (var (prefix, id) in assemblies)
                    if (name.StartsWith(prefix + ".", StringComparison.Ordinal))
                    {
                        problems.Add(new ModProblem(id, ModProblemKind.CodeFailed, Short(line)));
                        break;
                    }
            }
        }

        // Aynı mod + tür bir kez (ilk kanıtla).
        return problems
            .GroupBy(p => (p.ModId.ToLowerInvariant(), p.Kind))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Doğrulama günlüğündeki iki listeyi karşılaştırır. Öğeler "Native" ya da "Coop v0.1.6.0" biçiminde;
    /// resmî modüller Coop'ta da atlandığı için burada da atlanır.
    /// </summary>
    private static IEnumerable<ModProblem> CompareLists(string server, string client, string evidence)
    {
        static Dictionary<string, string?> Parse(string list) => list
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                var space = item.LastIndexOf(" v", StringComparison.Ordinal);
                return space > 0 ? (Id: item[..space], Version: (string?)item[(space + 1)..]) : (Id: item, Version: null);
            })
            .Where(x => !Official.Contains(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);

        var s = Parse(server);
        var c = Parse(client);

        foreach (var (id, version) in c)
        {
            if (!s.TryGetValue(id, out var sv)) yield return new ModProblem(id, ModProblemKind.PlayerHasExtra, evidence);
            else if (sv is not null && version is not null && !string.Equals(sv, version, StringComparison.OrdinalIgnoreCase))
                yield return new ModProblem(id, ModProblemKind.VersionMismatch, evidence, sv, version);
        }

        foreach (var (id, version) in s)
            if (!c.ContainsKey(id)) yield return new ModProblem(id, ModProblemKind.PlayerMissing, evidence, version);
    }

    private static readonly HashSet<string> Official = new(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle", "Multiplayer", "DedicatedServer.Windows",
    };

    /// <summary>
    /// Modların derleme adları (istisna yığınında namespace öneki olarak aranır):
    /// <c>Bannerlord.Diplomacy</c> → <c>Diplomacy.dll</c> → "Diplomacy".
    /// </summary>
    private static List<(string Prefix, string Id)> AssemblyNames(IReadOnlyList<PlannedMod> mods)
    {
        var list = new List<(string, string)>();
        foreach (var mod in mods)
        {
            foreach (var bin in new[] { ModuleScanner.ServerBin, ModuleScanner.DotNetCoreBin, ModuleScanner.ClientBin })
            {
                var dir = Path.Combine(mod.Module.Folder, "bin", bin);
                if (!Directory.Exists(dir)) continue;
                foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
                {
                    var name = Path.GetFileNameWithoutExtension(dll);
                    // Ortak kütüphaneler (Harmony, Newtonsoft…) birden çok modda olabilir; yanlış suçlama olmasın.
                    if (name.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("System.", StringComparison.Ordinal) ||
                        name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                        name.StartsWith("Newtonsoft", StringComparison.Ordinal) ||
                        name.StartsWith("TaleWorlds.", StringComparison.Ordinal))
                        continue;
                    list.Add((name, mod.Module.Id));
                }
            }
        }

        // Uzun önek önce: "Bannerlord.ButterLib.Foo" "Bannerlord.ButterLib"den önce denensin.
        return list.Distinct().OrderByDescending(x => x.Item1.Length).ToList();
    }

    private static string Short(string line) => line.Length <= 240 ? line : line[..240] + "…";
}
