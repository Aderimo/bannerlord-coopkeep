using System.Text.RegularExpressions;

namespace CoopKeep.Core.Installation;

/// <summary>
/// Atölye modlarının o an açık Steam hesabında abone olup olmadığı.
/// </summary>
/// <remarks>
/// <para>
/// Oyun Atölye modlarını klasörden değil Steam'den sorar: yalnızca açık hesabın abone olduğu
/// öğeleri görür. Aynı bilgisayarda iki Steam hesabı kullanılınca diğer hesabın indirdiği modlar
/// diskte kalır ama oyun ve başlatıcısı onları göstermez (kullanıcı bildirdi, 2026-10-23: paket
/// "hazır" görünüyordu, başlatıcıda modların yarısı yoktu).
/// </para>
/// <para>
/// Steam kimin abone olduğunu <c>steamapps\workshop\appworkshop_261550.acf</c> dosyasında
/// (<c>WorkshopItemDetails → subscribedby</c>) tutuyor. Açık hesap kayıt defterindeki
/// <c>HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser</c>'dan okunur.
/// </para>
/// </remarks>
public sealed class WorkshopSubscriptions
{
    private readonly Dictionary<string, HashSet<uint>> _subscribedBy;

    private WorkshopSubscriptions(uint? activeAccount, Dictionary<string, HashSet<uint>> subscribedBy)
    {
        ActiveAccount = activeAccount;
        _subscribedBy = subscribedBy;
    }

    /// <summary>Açık Steam hesabının kimliği (32 bit); bilinmiyorsa <c>null</c>.</summary>
    public uint? ActiveAccount { get; }

    /// <summary>Bilinmiyorsa (Steam kapalı, kayıt yok) herkes için "abone" sayılır.</summary>
    public static WorkshopSubscriptions Unknown { get; } = new(null, new Dictionary<string, HashSet<uint>>());

    /// <summary>
    /// Öğe açık hesapta abone mi? Steam kapalıysa ya da öğe Steam'in kaydında yoksa (elle
    /// kopyalanmış klasör) <c>true</c>: emin olmadığımız şeyi engellemiyoruz.
    /// </summary>
    public bool IsAvailable(string workshopId)
    {
        if (ActiveAccount is not { } account) return true;
        return !_subscribedBy.TryGetValue(workshopId, out var accounts) || accounts.Count == 0 || accounts.Contains(account);
    }

    /// <param name="workshopContentDirectories"><c>…\steamapps\workshop\content\261550</c> klasörleri.</param>
    /// <param name="activeAccount">Açık hesap; verilmezse kayıt defterinden okunur.</param>
    public static WorkshopSubscriptions Load(IEnumerable<string> workshopContentDirectories, uint? activeAccount = null)
    {
        activeAccount ??= ReadActiveAccount();
        if (activeAccount is null) return Unknown;

        var map = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal);
        foreach (var content in workshopContentDirectories)
        {
            var workshop = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(content).TrimEnd(Path.DirectorySeparatorChar)));
            if (workshop is null) continue;
            var acf = Path.Combine(workshop, "appworkshop_" + GameLocator.BannerlordAppId + ".acf");
            try
            {
                if (File.Exists(acf)) Merge(map, ParseAcf(File.ReadAllText(acf)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Okunamayan kayıt: o kütüphanedeki öğeler "bilinmiyor" kalır.
            }
        }

        return new WorkshopSubscriptions(activeAccount, map);
    }

    /// <summary>Test ve kayıt okuma için: öğe kimliği → abone hesaplar.</summary>
    public static Dictionary<string, HashSet<uint>> ParseAcf(string text)
    {
        var map = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal);
        var details = text.IndexOf("\"WorkshopItemDetails\"", StringComparison.Ordinal);
        if (details < 0) return map;

        foreach (Match item in Regex.Matches(text[details..], "\"(\\d+)\"\\s*\\{([^{}]*)\\}"))
        {
            var by = Regex.Match(item.Groups[2].Value, "\"subscribedby\"\\s*\"([^\"]*)\"");
            var accounts = new HashSet<uint>();
            if (by.Success)
                foreach (var part in by.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (uint.TryParse(part, out var id)) accounts.Add(id);
            map[item.Groups[1].Value] = accounts;
        }

        return map;
    }

    /// <summary>
    /// Steam'in kaydında "kurulu" görünen ama klasörü olmayan öğeler. Klasör elle silinince
    /// Steam bunu fark etmez; abone olunsa da "zaten var" sanıp indirmez. Çözüm: Steam'de
    /// oyunun dosya bütünlüğünü doğrulamak (Atölye öğeleri de denetlenir ve yeniden iner).
    /// </summary>
    public static IReadOnlySet<string> RecordedButMissing(IEnumerable<string> workshopContentDirectories)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var content in workshopContentDirectories)
        {
            var workshop = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(content).TrimEnd(Path.DirectorySeparatorChar)));
            if (workshop is null) continue;
            var acf = Path.Combine(workshop, "appworkshop_" + GameLocator.BannerlordAppId + ".acf");
            try
            {
                if (!File.Exists(acf)) continue;
                foreach (var id in ParseInstalled(File.ReadAllText(acf)))
                    if (!Directory.Exists(Path.Combine(content, id))) missing.Add(id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return missing;
    }

    /// <summary>Kayıttaki <c>WorkshopItemsInstalled</c> bölümünün öğe kimlikleri.</summary>
    public static IReadOnlyList<string> ParseInstalled(string text)
    {
        var start = text.IndexOf("\"WorkshopItemsInstalled\"", StringComparison.Ordinal);
        if (start < 0) return [];
        var end = text.IndexOf("\"WorkshopItemDetails\"", start, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];
        return Regex.Matches(section, "\"(\\d+)\"\\s*\\{").Select(m => m.Groups[1].Value).ToList();
    }

    private static void Merge(Dictionary<string, HashSet<uint>> into, Dictionary<string, HashSet<uint>> from)
    {
        foreach (var (id, accounts) in from)
        {
            if (into.TryGetValue(id, out var existing)) existing.UnionWith(accounts);
            else into[id] = accounts;
        }
    }

    /// <summary>Açık Steam hesabı: Steam çalışıyorsa kayıt defterinden; değilse son giriş yapılan.</summary>
    public static uint? ReadActiveAccount()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (key?.GetValue("ActiveUser") is int active && active != 0) return unchecked((uint)active);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return ReadMostRecentLogin();
    }

    private static uint? ReadMostRecentLogin()
    {
        if (GameLocator.FindSteamRoot() is { } steam)
        {
            var file = Path.Combine(steam, "config", "loginusers.vdf");
            try
            {
                if (!File.Exists(file)) return null;
                foreach (Match user in Regex.Matches(File.ReadAllText(file), "\"(7656\\d{13})\"\\s*\\{([^{}]*)\\}"))
                {
                    if (!Regex.IsMatch(user.Groups[2].Value, "\"MostRecent\"\\s*\"1\"")) continue;
                    if (ulong.TryParse(user.Groups[1].Value, out var id64)) return (uint)(id64 - 76561197960265728UL);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
