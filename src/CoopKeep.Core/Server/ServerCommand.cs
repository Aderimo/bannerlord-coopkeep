namespace CoopKeep.Core.Server;

/// <summary>
/// Dedicated server'a stdin üzerinden gönderilebilecek komutlar ve doğrulamaları.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu sınıf bir güvenlik sınırıdır.</b> Server açılışta komut setini ilan ediyor:
/// 7 builtin komut <i>ve 594 oyun konsol komutu</i>. İkinci grup
/// <c>campaign.add_gold_to_hero</c>, <c>campaign.give_settlement_to_player</c>,
/// <c>campaign.kill_hero</c> gibi doğrudan hile niteliğinde komutlar içeriyor ve
/// server'ın kendi yardım metni bunları açıkça kabul ettiğini söylüyor:
/// <c>&lt;a.b.c&gt; [args]  any game console command</c>.
/// </para>
/// <para>
/// Dolayısıyla kullanıcı girdisinin doğrudan stdin'e akmasına izin verilemez.
/// Yönetim işlemleri yalnızca bu allow-list üzerinden geçer.
/// </para>
/// </remarks>
public static class ServerCommand
{
    /// <summary>Yönetim için izin verilen builtin komutlar.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "status", "players", "save", "say", "kick", "stop", "help",
    };

    /// <summary>
    /// Server'ın <c>say</c> ile yayınladığı mesaj için üst sınır. Protokolde ilan edilmiş
    /// bir sınır yok; tek satıra sığdırmak ve aşırı uzunluğu engellemek için kendi
    /// muhafazakâr sınırımızı koyuyoruz.
    /// </summary>
    public const int MaxBroadcastLength = 200;

    public static string Status() => "status";
    public static string Players() => "players";
    public static string Save() => "save";
    public static string Stop() => "stop";
    public static string Help() => "help";

    /// <summary>
    /// Tüm oyunculara duyuru gönderir.
    /// </summary>
    /// <remarks>
    /// Restart geri sayımı ("Server 30 saniye içinde yeniden başlatılacak") bu komutla
    /// yapılır — Coop'ta başka bir broadcast kanalı yok.
    /// </remarks>
    public static string Say(string message) => "say " + SanitizeArgument(message, MaxBroadcastLength);

    /// <summary>
    /// Bir oyuncuyu sunucudan atar.
    /// </summary>
    /// <param name="idOrName">
    /// Peer id'si veya oyuncu adı. Server'ın kendi yardım metni:
    /// <c>kick &lt;id|name&gt;  disconnect a player ('players' shows ids)</c> —
    /// yani her ikisi de kabul ediliyor (2026-09-01'de doğrulandı).
    /// </param>
    public static string Kick(string idOrName)
    {
        var arg = SanitizeArgument(idOrName, 64);
        if (arg.Length == 0)
            throw new ArgumentException("Kick hedefi boş olamaz.", nameof(idOrName));

        return "kick " + arg;
    }

    /// <summary>
    /// Komut satırının stdin'e yazılmaya uygun olduğunu doğrular.
    /// </summary>
    /// <remarks>
    /// stdin satır tabanlı olduğu için gömülü bir satır sonu, tek bir komutu iki komuta
    /// böler — klasik komut enjeksiyonu. Bu yüzden hem burada hem
    /// <see cref="SanitizeArgument"/> içinde satır sonları eleniyor.
    /// </remarks>
    public static bool IsAllowed(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        if (commandLine.Any(c => c is '\r' or '\n')) return false;

        var verb = commandLine.Split(' ', 2)[0];
        return Allowed.Contains(verb);
    }

    /// <summary>
    /// Bir argümanı tek satırlık, kontrol karakteri içermeyen güvenli bir metne indirger.
    /// </summary>
    /// <remarks>
    /// Kontrol karakterleri silinmez, <b>boşluğa çevrilir</b>: silmek "ki<c>\n</c>ck" gibi
    /// bir girdiyi geçerli bir kelimeye dönüştürebilirdi.
    /// </remarks>
    public static string SanitizeArgument(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var buffer = new char[value.Length];
        var n = 0;
        foreach (var c in value)
            buffer[n++] = char.IsControl(c) ? ' ' : c;

        var cleaned = new string(buffer, 0, n).Trim();

        // Ardışık boşlukları tek boşluğa indir
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd() : cleaned;
    }
}
