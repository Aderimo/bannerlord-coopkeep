using System.Diagnostics;

namespace CoopKeep.Core.Installation;

/// <summary>
/// Windows güvenlik duvarında oyunun gelen bağlantılara izin kuralı.
/// </summary>
/// <remarks>
/// Oyun ilk kez sunucu açtığında Windows izin soruyor; pencere oyunun arkasında kalırsa
/// fark edilmiyor ve arkadaşlar "sunucuya ulaşılamadı" alıyor (geri bildirim). Kuralı
/// önceden eklemek bunu önler. Ekleme yönetici izni ister; Windows'un kendi onay penceresi
/// çıkar.
/// </remarks>
public static class GameFirewall
{
    public const string RuleName = "CoopKeep (Bannerlord)";

    /// <summary>Penceresiz sunucunun (sunucu motorunun) kuralı.</summary>
    public const string EngineRuleName = "CoopKeep (server engine)";

    public static bool HasRule() => HasRule(RuleName);

    public static bool HasRule(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{name}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Kuralı ekler (yönetici onayıyla). Kullanıcı onay penceresini reddederse <c>false</c>.
    /// </summary>
    public static bool AddRule(string gameExe) => AddRules([(RuleName, gameExe)]);

    /// <summary>Birden çok kural, tek yönetici onayıyla.</summary>
    public static bool AddRules(IReadOnlyList<(string Name, string Program)> rules)
    {
        var args = "/c " + string.Join(" & ", rules.Select(r =>
            $"netsh advfirewall firewall add rule name=\"{r.Name}\" dir=in action=allow program=\"{r.Program}\" protocol=UDP enable=yes"));
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // onay penceresi reddedildi
        }
    }
}
