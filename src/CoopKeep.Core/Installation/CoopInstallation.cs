namespace CoopKeep.Core.Installation;

/// <summary>
/// Diskte bulunmuş bir Bannerlord Coop kurulumu.
/// </summary>
public sealed record CoopInstallation
{
    /// <summary>Workshop öğesinin kök dizini.</summary>
    public required string ModRoot { get; init; }

    public required string DedicatedServerExe { get; init; }

    /// <summary>Coop modülünün kimliği (<c>Coop</c>; deneme sürümünde <c>CoopNightly</c>).</summary>
    public string ModuleId { get; init; } = "Coop";

    /// <summary>Steam Workshop öğe kimliği. Sürüm takibi için kullanılır.</summary>
    public string? WorkshopItemId { get; init; }

    /// <summary><c>SubModule.xml</c>'den okunan mod sürümü (örn. <c>v0.1.4</c>).</summary>
    public string? ModVersion { get; init; }

    /// <summary><c>SubModule.xml</c>'in bağımlı olduğu oyun sürümü (örn. <c>v1.4.8</c>).</summary>
    public string? RequiredGameVersion { get; init; }

    /// <summary>Sunucunun lisans sözleşmesi.</summary>
    public string EulaPath => Path.Combine(Path.GetDirectoryName(DedicatedServerExe)!, "EULA.md");

    public string LicensePath => Path.Combine(Path.GetDirectoryName(DedicatedServerExe)!, "LICENSE.txt");

    /// <summary>
    /// EULA + LICENSE metinlerinin parmak izi. Mod güncellenip sözleşme değişirse
    /// kullanıcıdan yeniden onay istemek için.
    /// </summary>
    public string? EulaFingerprint()
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = File.ReadAllBytes(EulaPath).Concat(File.Exists(LicensePath) ? File.ReadAllBytes(LicensePath) : []).ToArray();
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Veri klasöründe sunucunun kendi kaydettiği bir EULA onayı var mı?
    /// </summary>
    /// <remarks>
    /// Sunucu onayı <c>eula.txt</c>'ye <c>eula=true</c> olarak yazıyor. Kullanıcı daha önce
    /// sunucuyu kendisi açıp onayladıysa CoopKeep tekrar sormaz.
    /// </remarks>
    public static bool HasAcceptedEula(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "eula.txt");
        try
        {
            return File.Exists(path) && File.ReadLines(path)
                .Any(l => l.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Server'ın <c>--data-dir</c> verilmediğinde kullandığı dizin.</summary>
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer");

    /// <summary>Oyun ayarlarının bulunduğu, oyuncu-host'lu oturumlarla ortak dizin.</summary>
    public static string DefaultModConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "CoopData", "mod-config.json");
}

/// <summary>
/// Kurulu Bannerlord oyunu.
/// </summary>
public sealed record BannerlordInstallation
{
    public required string InstallDirectory { get; init; }

    /// <summary>
    /// <c>Modules\Native\SubModule.xml</c>'den okunan sürüm (örn. <c>v1.4.8</c>).
    /// </summary>
    /// <remarks>
    /// <b>Tuzak:</b> Kurulum kökündeki <c>package_info.txt</c> dosyasında da bir sürüm
    /// alanı var (<c>Environment: PC@v1.3.4</c>) ama bu <b>bayat</b> — TaleWorlds onu
    /// güncellemiyor. Sürüm kontrolü mutlaka <c>Modules\Native\SubModule.xml</c>'den
    /// yapılmalı, aksi hâlde uyumlu bir kurulum yanlışlıkla uyumsuz raporlanır.
    /// </remarks>
    public string? GameVersion { get; init; }

    /// <summary>Epic Games kurulumu mu (Steam Atölyesi yok; modlar zip ile gelir)?</summary>
    public bool IsEpic { get; init; }

    public string ModulesDirectory => Path.Combine(InstallDirectory, "Modules");

    public string ClientBinDirectory => Path.Combine(InstallDirectory, "bin", "Win64_Shipping_Client");

    /// <summary>Oyunun kendi save dizini (tek oyunculu kampanyalar).</summary>
    public static string SinglePlayerSavesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "Game Saves");
}
