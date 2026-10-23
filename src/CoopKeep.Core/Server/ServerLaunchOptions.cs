namespace CoopKeep.Core.Server;

/// <summary>
/// <c>BannerlordCoopServer.exe</c>'nin nasıl başlatılacağı.
/// </summary>
/// <remarks>
/// Argümanlar binary'den kurtarılan <c>--help</c> metniyle ve 2026-09-01 canlı
/// koşularıyla doğrulandı.
/// </remarks>
public sealed class ServerLaunchOptions
{
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Server'ın sahip olduğu her şeyin bulunduğu dizin: <c>server-config.json</c>,
    /// <c>Game Saves\</c>, <c>logs\</c>.
    /// </summary>
    /// <remarks>
    /// Boş bırakılırsa server varsayılanı kullanır:
    /// <c>Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer</c>.
    /// <b>Testlerde her zaman izole bir dizin verin</b> — kullanıcının gerçek kampanyası
    /// test verisi değildir.
    /// </remarks>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// Çalışma dizini. Server, gömülü motorunu kendi klasörüne göre bulduğu için
    /// exe'nin bulunduğu dizin olmalı.
    /// </summary>
    public string WorkingDirectory =>
        Path.GetDirectoryName(Path.GetFullPath(ExecutablePath))
        ?? throw new InvalidOperationException("Çalışma dizini belirlenemedi.");

    /// <summary>
    /// TUI panellerini kapatır. stdio redirect edildiğinde zaten otomatik devreye giriyor;
    /// niyeti açık kılmak için yine de veriyoruz.
    /// </summary>
    public bool NoTui { get; init; } = true;

    /// <summary>
    /// MonoMod ve crash-dump tanılamasını açar. Yalnızca hata raporu toplarken.
    /// </summary>
    public bool Trace { get; init; }

    /// <summary>
    /// Kullanıcı sunucunun EULA'sını CoopKeep'te okuyup kabul etti mi?
    /// </summary>
    /// <remarks>
    /// Sunucu ilk açılışta EULA onayı istiyor ve onayı veri klasöründeki <c>eula.txt</c>'ye
    /// yazıyor. CoopKeep bunu <b>kullanıcı adına sessizce</b> kabul etmez: metni gösterir,
    /// kullanıcı onaylarsa <c>--accept-eula</c> ile başlatır.
    /// </remarks>
    public bool AcceptEula { get; init; }

    /// <summary>
    /// Coop'un ban listesinin yolu (<c>COOP_STEAM_BAN_FILE</c>). Boşsa sunucu kendi
    /// varsayılanını kullanır.
    /// </summary>
    /// <remarks>
    /// <c>COOP_DATA_DIR</c> yerine bu değişken kullanılıyor: o değişken
    /// <c>mod-config.json</c>'ın yerini de değiştirirdi. Yol mutlak olmalı ve tırnak
    /// içermemeli (Coop belgesi).
    /// </remarks>
    public string? SteamBanFile { get; init; }

    // ------------------------------------------------------------------
    // Modlu açılış: motor doğrudan başlatılır
    // ------------------------------------------------------------------

    /// <summary>
    /// Motora verilecek tam modül listesi (resmî modüller + modlar + Coop). Doluysa
    /// sarmalayıcı (<c>BannerlordCoopServer.exe</c>) yerine motor doğrudan başlatılır.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sarmalayıcı listeyi sabit kodluyor (<c>LaunchPlan.BuildStartInfo</c>, Coop v0.1.6):
    /// <c>_MODULES_*Native*SandBoxCore*Sandbox*Coop*DedicatedServer.Windows*_MODULES_</c>; dışarıdan
    /// liste verme seçeneği yok. Bu yüzden modlu açılışta motoru sarmalayıcının kurduğu ortamın
    /// aynısıyla biz başlatıyoruz (2026-10-18'de test sunucusunda ölçüldü: SERVING, <c>@DS@</c>,
    /// stdin komutları ve eklenti çalışıyor).
    /// </para>
    /// <para>
    /// EULA ve Coop'un SHA-256 doğrulaması <b>motorun içinde</b> de yapılıyor ("EULA accepted locally",
    /// "Coop module verified against release pins"); bu yol hiçbir kontrolü atlatmaz.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? EngineModules { get; init; }

    /// <summary>Motorun iç portu ve bölgesi; sarmalayıcının varsayılanları.</summary>
    public int EnginePort { get; init; } = 7210;
    public string EngineRegion { get; init; } = "EU";

    public bool IsDirect => EngineModules is { Count: > 0 };

    /// <summary>Sunucu kurulumunun kökü (<c>DedicatedServer</c>).</summary>
    public string ServerRoot => WorkingDirectory;

    public string EngineDirectory => Path.Combine(ServerRoot, "engine");

    public string EngineModulesDirectory => Path.Combine(EngineDirectory, "Modules");

    private string EngineBin => Path.Combine(EngineDirectory, "bin", "Win64_Shipping_Server");

    private string BundledDotnet => Path.Combine(EngineDirectory, "dotnet", "dotnet.exe");

    /// <summary>Çalıştırılacak dosya: sarmalayıcı ya da gömülü .NET.</summary>
    public string FileName => IsDirect ? BundledDotnet : ExecutablePath;

    /// <summary>Sürecin çalışma klasörü: motor kendi <c>bin</c> klasöründe çalışır.</summary>
    public string ProcessWorkingDirectory => IsDirect ? EngineBin : WorkingDirectory;

    /// <summary>Veri klasörü; verilmemişse sunucunun varsayılanı.</summary>
    public string EffectiveDataDirectory => Path.GetFullPath(
        string.IsNullOrWhiteSpace(DataDirectory) ? Installation.CoopInstallation.DefaultDataDirectory : DataDirectory);

    /// <summary>Motorun <c>_MODULES_</c> argümanı.</summary>
    public static string ModuleArgument(IEnumerable<string> modules) =>
        "_MODULES_" + string.Concat(modules.Select(m => "*" + m)) + "*_MODULES_";

    /// <summary>
    /// Sarmalayıcının açılıştan önce yaptığı hazırlığın aynısı (<c>LaunchPlan.EnsureDataDir</c>):
    /// veri klasörü, boş dünya kaydı (<c>default_new_game.sav</c>) ve <c>mod-config.json</c> yoksa kopyalanır.
    /// </summary>
    public void PrepareDirectDataDirectory()
    {
        var data = EffectiveDataDirectory;
        var deployment = Path.Combine(ServerRoot, "server-data");
        Directory.CreateDirectory(data);

        if (string.Equals(data.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(deployment).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return;

        CopyIfAbsent(Path.Combine(deployment, "Game Saves", "default_new_game.sav"),
            Path.Combine(data, "Game Saves", "default_new_game.sav"));

        // Coop'un kuralı (BannerlordUserData.ResolveModConfigDir): veri klasöründe varsa orası,
        // yoksa CoopData klasörü.
        var modConfigDir = File.Exists(Path.Combine(data, "mod-config.json"))
            ? data
            : Path.GetDirectoryName(Installation.CoopInstallation.DefaultModConfigPath) ?? data;
        CopyIfAbsent(Path.Combine(deployment, "mod-config.json"), Path.Combine(modConfigDir, "mod-config.json"));
    }

    private static void CopyIfAbsent(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    /// <summary>Sunucu sürecine verilecek ortam değişkenleri.</summary>
    public IReadOnlyDictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(SteamBanFile))
            env["COOP_STEAM_BAN_FILE"] = Path.GetFullPath(SteamBanFile);

        if (IsDirect)
        {
            // Sarmalayıcının LaunchPlan.ApplyEnvironment'ı ile aynı.
            env["BANNERLORD_USER_DIR"] = EffectiveDataDirectory;
            env["DOTNET_ROOT"] = Path.Combine(EngineDirectory, "dotnet");
            env["DOTNET_MULTILEVEL_LOOKUP"] = "0";

            // --accept-eula'nın karşılığı; motorun kendi EULA denetimi bunu tanıyor.
            // Yalnızca kullanıcı sözleşmeyi CoopKeep'te okuyup onayladıysa verilir.
            if (AcceptEula) env["DS_ACCEPT_EULA"] = "true";
        }

        return env;
    }

    public IReadOnlyList<string> BuildArguments()
    {
        if (IsDirect)
        {
            return
            [
                "TaleWorlds.Starter.DotNetCore.dll",
                ModuleArgument(EngineModules!),
                "/dedicatedcustomserver",
                EnginePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                EngineRegion,
                "0",
            ];
        }

        var args = new List<string>();

        if (NoTui) args.Add("--no-tui");
        if (Trace) args.Add("--trace");
        if (AcceptEula) args.Add("--accept-eula");

        if (!string.IsNullOrWhiteSpace(DataDirectory))
        {
            args.Add("--data-dir");
            args.Add(DataDirectory);
        }

        return args;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
            throw new InvalidOperationException("Server exe yolu belirtilmemiş.");

        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("Server exe bulunamadı.", ExecutablePath);

        if (!IsDirect) return;

        if (!File.Exists(BundledDotnet))
            throw new FileNotFoundException("Sunucunun gömülü .NET'i bulunamadı.", BundledDotnet);
        if (!File.Exists(Path.Combine(EngineBin, "TaleWorlds.Starter.DotNetCore.dll")))
            throw new FileNotFoundException("Sunucu motoru bulunamadı.", EngineBin);

        // Kimlikler argümanın içine yıldızla ayrılarak girer: yıldız, boşluk ya da tırnak olamaz.
        foreach (var id in EngineModules!)
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['*', ' ', '"', '/']) >= 0)
                throw new InvalidOperationException($"Geçersiz modül kimliği: '{id}'");
    }
}
