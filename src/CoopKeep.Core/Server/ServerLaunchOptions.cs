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

    public IReadOnlyList<string> BuildArguments()
    {
        var args = new List<string>();

        if (NoTui) args.Add("--no-tui");
        if (Trace) args.Add("--trace");

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
