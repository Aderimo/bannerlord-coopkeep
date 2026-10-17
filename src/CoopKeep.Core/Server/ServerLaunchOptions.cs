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

    /// <summary>Sunucu sürecine verilecek ortam değişkenleri.</summary>
    public IReadOnlyDictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(SteamBanFile))
            env["COOP_STEAM_BAN_FILE"] = Path.GetFullPath(SteamBanFile);
        return env;
    }

    public IReadOnlyList<string> BuildArguments()
    {
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
    }
}
