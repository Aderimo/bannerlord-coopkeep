using System.Diagnostics;

namespace CoopKeep.Core.Installation;

/// <summary>
/// Oyunu Coop moduyla (ve dünyanın modlarıyla) açar: oyunun kendi başlatıcısını atlar.
/// </summary>
/// <remarks>
/// <para>
/// Coop, bağlanırken oyuncunun modlarını sunucununkilerle iki yönde birebir karşılaştırır
/// (eksik, fazla ve sürümü farklı mod reddedilir). Oyunu doğru modlarla tek tıkla açmak bu
/// yüzden önemli: oyuncu başlatıcıda tek tek mod açıp kapatmak zorunda kalmaz.
/// </para>
/// <para>
/// DLC'ler (War Sails) ve diğer co-op modları Coop ile birlikte yüklenmez; Coop bunları
/// reddediyor.
/// </para>
/// </remarks>
public static class CoopClientLauncher
{
    /// <summary>Oyunun zorunlu resmî modülleri, yükleme sırasıyla (Coop bunlara bağımlı).</summary>
    public static readonly string[] BaseModules = ["Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle"];

    /// <summary>Coop ile birlikte yüklenmeyenler: DLC'ler, çok oyunculu modül, diğer co-op modları.</summary>
    public static readonly IReadOnlySet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NavalDLC", "Multiplayer", "CoopKeep", "DedicatedServer.Windows",
    };

    /// <summary>
    /// Yüklenecek modüller: resmî modüller, dünyanın modları, en sonda Coop. Bilgisayardaki modlar
    /// verilirse yükleme sırası onların isteklerine göre kurulur (Harmony, ButterLib gibi
    /// kütüphaneler resmî modüllerden önce).
    /// </summary>
    public static IReadOnlyList<string> ModuleList(IEnumerable<string> worldMods, string coopModuleId = "Coop",
        IReadOnlyList<Mods.InstalledModule>? installed = null)
    {
        var list = BaseList(worldMods, coopModuleId);
        return installed is null ? list : Mods.LoadOrder.Sort(list, installed, out _);
    }

    private static List<string> BaseList(IEnumerable<string> worldMods, string coopModuleId)
    {
        var list = new List<string>(BaseModules);
        foreach (var id in worldMods)
        {
            if (Excluded.Contains(id) || id.StartsWith("Coop", StringComparison.OrdinalIgnoreCase)) continue;
            if (list.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(id);
        }

        list.Add(coopModuleId);
        return list;
    }

    public static IReadOnlyList<string> Arguments(IReadOnlyList<string> modules) =>
        ["/singleplayer", "_MODULES_*" + string.Join("*", modules) + "*_MODULES_"];

    /// <summary>Oyunu açar. Oyun zaten açıksa açmaz (iki oyun aynı Steam hesabıyla çakışır).</summary>
    public static Process Start(BannerlordInstallation game, IReadOnlyList<string> arguments)
    {
        if (ModuleCompatibility.IsGameRunning(null)) throw new InvalidOperationException("The game is already running.");

        var exe = Path.Combine(game.ClientBinDirectory, "Bannerlord.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Bannerlord.exe was not found.", exe);

        var psi = new ProcessStartInfo { FileName = exe, WorkingDirectory = game.ClientBinDirectory, UseShellExecute = false };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("The game could not be started.");
    }
}
