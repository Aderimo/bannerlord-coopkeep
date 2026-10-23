using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Mods;

/// <summary>Bir modun Coop'ta nasıl davrandığı (Modlar sayfasındaki etiket).</summary>
public enum CoopModRole
{
    /// <summary>Coop bu modu reddeder ya da topluluk "çalışmıyor" bildirdi.</summary>
    Incompatible,

    /// <summary>Arayüz / kütüphane: sunucuda yalnızca kimliği bulunur (Harmony, MCM…).</summary>
    Library,

    /// <summary>Veri modu: eşya, birlik, yerleşim verileri sunucuya da kurulur.</summary>
    Data,

    /// <summary>Kodlu mod: kodu da sunucuda çalışır.</summary>
    Code,
}

/// <summary>
/// Modun Coop'taki rolü ve oyun sürümü uyarısı. Coop modları iki yönde birebir
/// karşılaştırdığı için her mod herkeste olmalı; rol yalnızca sunucuya nasıl kurulacağını ve
/// sorun çıkarabileceğini söyler.
/// </summary>
public static class CoopModRoles
{
    public static CoopModRole Classify(InstalledModule module, CommunityStatus community = CommunityStatus.Unknown)
    {
        if (ModuleCompatibility.Classify(module.Id) == ModuleStatus.Blocked || community == CommunityStatus.Broken)
            return CoopModRole.Incompatible;

        // Kurulum kipinden değil modun içeriğinden: kodsuz mod veri modudur (sunucuya olduğu gibi
        // kurulur); arayüz/kütüphane modları sunucuda yalnızca kimlik olarak bulunur.
        if (ModPlanner.ResolveMode(module, ModInstallMode.Auto, community == CommunityStatus.ClientOnly) == ModInstallMode.IdOnly)
            return CoopModRole.Library;
        return module.HasCode ? CoopModRole.Code : CoopModRole.Data;
    }

    /// <summary>
    /// Mod oyunun başka bir ana/ara sürümü için mi yapılmış (ör. e1.5.2 ile v1.4.8)? Yama
    /// numarası farkı sayılmaz; bilinmiyorsa <c>false</c>.
    /// </summary>
    public static bool IsForOtherGameVersion(string? modGameVersion, string? gameVersion)
    {
        if (Parse(modGameVersion) is not { } m || Parse(gameVersion) is not { } g) return false;
        return m.Major != g.Major || m.Minor != g.Minor;
    }

    private static (int Major, int Minor)? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().TrimStart('v', 'V', 'e', 'E').Split('.');
        return parts.Length >= 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b) ? (a, b) : null;
    }

    /// <summary>Atölye açıklamasının ilk anlamlı cümlesi: BBCode etiketleri ve boş satırlar atılır.</summary>
    public static string ShortDescription(string? description, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(description, @"\[[^\]]{0,40}\]", " ");
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 12) ?? "";
        line = System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ");
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }
}
