namespace CoopKeep.Core.Protocol;

/// <summary>
/// Dedicated server'ın <c>@DS@ {"ev":"state","phase":...}</c> olayında bildirdiği yaşam
/// döngüsü fazı.
/// </summary>
/// <remarks>
/// Değerler gerçek server çıktısından doğrulandı (2026-09-01). Faz sırası:
/// <c>boot</c> → (~26 sn) → <c>loading</c> → (~27 sn) → <c>serving</c>, ve kapanışta
/// <c>stopping</c> → <c>shutdown</c>.
/// <para>
/// Bu enum'un varlık sebebi önemli: hazır olma sinyalini <c>"SERVING"</c> gibi bir metni
/// arayarak değil, protokolün kendi alanından okuyoruz. Upstream günlük commit atıyor;
/// log metinleri değişebilir, protokol alanı çok daha kararlı.
/// </para>
/// </remarks>
public enum ServerPhase
{
    /// <summary>Tanınmayan bir faz adı geldi. Bilinmeyen değerler çökertmez, loglanır.</summary>
    Unknown = 0,

    Starting,
    Boot,
    Loading,

    /// <summary>Server ayakta ve oyuncu bekliyor. Hazır olma sinyali budur.</summary>
    Serving,

    Stopping,
    Shutdown,
}

public static class ServerPhaseExtensions
{
    /// <summary>
    /// Protokoldeki ham faz adını enum'a çevirir. Tanınmayan değer <see cref="ServerPhase.Unknown"/>
    /// döner — asla fırlatmaz, çünkü upstream yeni bir faz ekleyebilir ve bu bizi çökertmemeli.
    /// </summary>
    public static ServerPhase ParsePhase(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "starting" => ServerPhase.Starting,
        "boot" => ServerPhase.Boot,
        "loading" => ServerPhase.Loading,
        "serving" => ServerPhase.Serving,
        "stopping" => ServerPhase.Stopping,
        "shutdown" => ServerPhase.Shutdown,
        _ => ServerPhase.Unknown,
    };

    /// <summary>Server bu fazda oyuncu kabul ediyor mu?</summary>
    public static bool IsAcceptingPlayers(this ServerPhase phase) => phase == ServerPhase.Serving;

    /// <summary>Server bu fazda hâlâ açılıyor mu? (UI'da ilerleme göstermek için)</summary>
    public static bool IsStartingUp(this ServerPhase phase) =>
        phase is ServerPhase.Starting or ServerPhase.Boot or ServerPhase.Loading;
}
