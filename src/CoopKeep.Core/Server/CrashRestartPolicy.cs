namespace CoopKeep.Core.Server;

/// <summary>Bir çöküş sonrası ne yapılacağı.</summary>
/// <param name="ShouldRestart">Yeniden başlatılsın mı?</param>
/// <param name="Delay">Başlatmadan önce beklenecek süre.</param>
/// <param name="Reason">Kullanıcıya gösterilecek gerekçe anahtarı.</param>
public readonly record struct CrashDecision(bool ShouldRestart, TimeSpan Delay, CrashDecisionReason Reason);

public enum CrashDecisionReason
{
    /// <summary>Kasıtlı duruş — çöküş değil.</summary>
    NotACrash,

    /// <summary>Bu hata türü yeniden başlatmakla düzelmez (bozuk save, modül uyuşmazlığı).</summary>
    NotRecoverable,

    /// <summary>Yeniden başlatılacak.</summary>
    Restarting,

    /// <summary>Çok sık çöküyor; otomatik yeniden başlatma durduruldu.</summary>
    CrashLoop,

    /// <summary>Otomatik yeniden başlatma kullanıcı tarafından kapatılmış.</summary>
    Disabled,
}

/// <summary>
/// Çöküş sonrası yeniden başlatma politikası: üstel bekleme ve döngü koruması.
/// </summary>
/// <remarks>
/// <para>
/// Bu sınıfın var olma sebebi somut: incelediğimiz benzer araçlarda hiçbir bekleme
/// veya döngü koruması yok, dolayısıyla açılışta çöken bir sunucu sonsuz bir
/// yeniden başlatma döngüsüne giriyor ve makineyi meşgul ediyor.
/// </para>
/// <para>
/// İki koruma katmanı var:
/// <list type="bullet">
///   <item><b>Üstel bekleme:</b> her denemede bekleme süresi çarpanla artar, tavana kadar.</item>
///   <item><b>Döngü koruması:</b> belirli bir zaman penceresinde çok fazla çöküş olursa
///   otomasyon tamamen durur ve karar kullanıcıya bırakılır.</item>
/// </list>
/// </para>
/// <para>
/// Ayrıca <see cref="ServerExit.IsAutoRestartable"/> ile birlikte çalışır: bozuk save
/// (kod 2) ve modül doğrulama hatası (kod 4) hiç denenmez, çünkü bunlar tekrar
/// denemekle düzelmez.
/// </para>
/// </remarks>
public sealed class CrashRestartPolicy(
    TimeSpan? initialDelay = null,
    TimeSpan? maxDelay = null,
    double multiplier = 2.0,
    int maxRestartsPerWindow = 3,
    TimeSpan? window = null)
{
    private readonly TimeSpan _initialDelay = initialDelay ?? TimeSpan.FromSeconds(10);
    private readonly TimeSpan _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
    private readonly double _multiplier = multiplier <= 1 ? 2.0 : multiplier;
    private readonly int _maxRestartsPerWindow = Math.Max(1, maxRestartsPerWindow);
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(10);

    private readonly List<DateTimeOffset> _recentCrashes = [];

    /// <summary>Otomatik yeniden başlatma açık mı?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Pencere içindeki çöküş sayısı (son değerlendirmeye göre).</summary>
    public int RecentCrashCount => _recentCrashes.Count;

    /// <summary>
    /// Çöküş sonrası kararı verir ve iç sayaçları günceller.
    /// </summary>
    public CrashDecision Evaluate(ServerExitInfo exit, DateTimeOffset now)
    {
        // Biz durdurduysak veya temiz çıktıysa bu bir çöküş değil.
        if (!exit.IsCrash)
        {
            _recentCrashes.Clear();
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.NotACrash);
        }

        if (!ServerExit.IsAutoRestartable(exit.Kind))
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.NotRecoverable);

        if (!Enabled)
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.Disabled);

        // Pencere dışındaki eski çöküşleri unut.
        _recentCrashes.RemoveAll(t => now - t > _window);
        _recentCrashes.Add(now);

        if (_recentCrashes.Count > _maxRestartsPerWindow)
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.CrashLoop);

        // Üstel bekleme: 1. çöküşte initialDelay, sonrakilerde çarpanla artar.
        var steps = _recentCrashes.Count - 1;
        var seconds = _initialDelay.TotalSeconds * Math.Pow(_multiplier, steps);
        var delay = TimeSpan.FromSeconds(Math.Min(seconds, _maxDelay.TotalSeconds));

        return new CrashDecision(true, delay, CrashDecisionReason.Restarting);
    }

    /// <summary>
    /// Sayaçları sıfırlar — sunucu bir süre sorunsuz çalıştıysa veya kullanıcı elle
    /// başlattıysa geçmiş çöküşler artık anlamlı değildir.
    /// </summary>
    public void Reset() => _recentCrashes.Clear();
}
