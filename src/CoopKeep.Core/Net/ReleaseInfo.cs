using System.Text.Json;

namespace CoopKeep.Core.Net;

/// <summary>GitHub'daki son sürüm: etiketi ve indirilecek exe'nin adresi.</summary>
/// <param name="Tag">Sürüm etiketi (ör. <c>v1.0.1</c>).</param>
/// <param name="ExeUrl">Sürüme eklenmiş <c>CoopKeep.exe</c>'nin indirme adresi; yoksa <c>null</c>.</param>
/// <param name="ExeSize">Exe'nin bayt cinsinden boyutu (indirmeyi doğrulamak için); bilinmiyorsa 0.</param>
public sealed record ReleaseInfo(string Tag, string? ExeUrl, long ExeSize)
{
    public const string ExeName = "CoopKeep.exe";

    /// <summary>GitHub API'nin <c>releases/latest</c> yanıtını okur.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagProp) || tagProp.GetString() is not { Length: > 0 } tag) return null;

        string? url = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase)) continue;
                url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var v) ? v : 0;
                break;
            }
        }

        // Yalnızca GitHub'ın kendi indirme adresine güvenilir.
        if (url is not null && !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = null;
        return new ReleaseInfo(tag, url, size);
    }

    /// <summary>
    /// Kendini değiştirme betiği: CoopKeep kapanınca yeni exe eskisinin yerine konur ve açılır.
    /// Çalışan exe Windows'ta üzerine yazılamadığı için değiştirme kapandıktan sonra yapılır.
    /// </summary>
    public static string UpdaterScript(int processId, string newExe, string currentExe) =>
        "@echo off\r\n" +
        ":wait\r\n" +
        $"tasklist /FI \"PID eq {processId}\" | find \"{processId}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
        $"move /y \"{newExe}\" \"{currentExe}\" >nul\r\n" +
        $"start \"\" \"{currentExe}\"\r\n" +
        "del \"%~f0\"\r\n";
}
