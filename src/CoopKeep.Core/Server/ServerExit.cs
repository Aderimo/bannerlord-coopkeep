namespace CoopKeep.Core.Server;

/// <summary>
/// Server process'inin çıkış kodunun ne anlama geldiği.
/// </summary>
/// <remarks>
/// Kodlar <c>release-info.txt</c>'te belgeli ve Faz 0'da doğrulandı (temiz <c>stop</c> → 0).
/// </remarks>
public enum ServerExitKind
{
    /// <summary>0 — temiz duruş. Dünya kaydedildi.</summary>
    Clean,

    /// <summary>
    /// 2 — save yüklenemedi veya zaman aşımına uğradı.
    /// </summary>
    LoadFailure,

    /// <summary>3 — server çalışırken ölümcül hata.</summary>
    FatalWhileServing,

    /// <summary>
    /// 4 — Coop modülü doğrulaması başarısız. Modül değiştirilmiş veya güncellenmiş.
    /// </summary>
    ModuleVerificationFailed,

    /// <summary>Belgelenmemiş bir kod.</summary>
    Unknown,
}

public static class ServerExit
{
    public static ServerExitKind Classify(int exitCode) => exitCode switch
    {
        0 => ServerExitKind.Clean,
        2 => ServerExitKind.LoadFailure,
        3 => ServerExitKind.FatalWhileServing,
        4 => ServerExitKind.ModuleVerificationFailed,
        _ => ServerExitKind.Unknown,
    };

    /// <summary>
    /// Bu çıkış türü otomatik yeniden başlatmaya uygun mu?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Kritik karar:</b> <see cref="ServerExitKind.LoadFailure"/> ve
    /// <see cref="ServerExitKind.ModuleVerificationFailed"/> için <c>false</c> döner.
    /// Bu ikisi tekrar denemekle düzelmez:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     Kod 2 save bozulmuş olabileceğini gösterir; yeniden başlatmak bozuk save'i
    ///     tekrar tekrar yüklemeye çalışmak olur. Doğrusu kullanıcıya yedekten dönmeyi önermek.
    ///   </item>
    ///   <item>
    ///     Kod 4 modülün değiştiğini gösterir (genelde mod güncellemesi). Yeniden başlatmak
    ///     sonsuz döngü yaratır; doğrusu kurulum doğrulamasına yönlendirmek.
    ///   </item>
    /// </list>
    /// </remarks>
    public static bool IsAutoRestartable(ServerExitKind kind) => kind switch
    {
        ServerExitKind.Clean => false,                     // kasıtlı duruş
        ServerExitKind.LoadFailure => false,               // save şüpheli
        ServerExitKind.ModuleVerificationFailed => false,  // kurulum sorunu
        ServerExitKind.FatalWhileServing => true,
        ServerExitKind.Unknown => true,
        _ => false,
    };

    /// <summary>Kullanıcıya gösterilecek, ne yapılacağını söyleyen açıklama.</summary>
    public static string Describe(ServerExitKind kind) => kind switch
    {
        ServerExitKind.Clean =>
            "Server temiz şekilde kapandı ve dünya kaydedildi.",
        ServerExitKind.LoadFailure =>
            "Kampanya yüklenemedi. Save dosyası bozulmuş olabilir — otomatik yeniden başlatma yapılmadı. Son yedeği geri yüklemeyi deneyin.",
        ServerExitKind.FatalWhileServing =>
            "Server çalışırken beklenmedik şekilde çöktü.",
        ServerExitKind.ModuleVerificationFailed =>
            "Coop modülü doğrulaması başarısız oldu. Mod güncellenmiş veya dosyaları değişmiş olabilir; tüm oyuncuların aynı sürümde olması gerekir.",
        _ =>
            "Server bilinmeyen bir kodla kapandı.",
    };
}
