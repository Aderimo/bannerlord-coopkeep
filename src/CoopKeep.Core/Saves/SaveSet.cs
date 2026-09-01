namespace CoopKeep.Core.Saves;

/// <summary>
/// Bir kampanya save'i: <c>&lt;ad&gt;.sav</c> ve eşi <c>&lt;ad&gt;.json</c> birlikte.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu tipin var olma sebebi bir güvenlik kuralıdır:</b> iki dosya asla birbirinden
/// ayrılmamalı. Yalnızca <c>.sav</c> kopyalayan bir kod yolunun var olmaması için
/// tüm save işlemleri bu tip üzerinden yapılır. Coop'un kendi dokümantasyonu da
/// bunu şöyle söylüyor: "a save moves or deletes as one folder's
/// <c>&lt;name&gt;.sav</c> + <c>&lt;name&gt;.json</c> pair".
/// </para>
/// </remarks>
public sealed record SaveSet
{
    public const string SaveExtension = ".sav";
    public const string CompanionExtension = ".json";

    /// <summary>Uzantısız save adı — <c>server-config.json</c>'daki <c>saveName</c> ile aynı.</summary>
    public required string Name { get; init; }

    public required string SavePath { get; init; }

    /// <summary>Eş JSON dosyasının yolu. Dosya diskte olmayabilir (bkz. <see cref="IsComplete"/>).</summary>
    public required string CompanionPath { get; init; }

    public long SizeBytes { get; init; }

    public DateTime LastModifiedUtc { get; init; }

    /// <summary>
    /// Kullanıcıya gösterilecek yerel saat.
    /// </summary>
    /// <remarks>
    /// Arayüzde doğrudan <see cref="LastModifiedUtc"/> bağlamak, kullanıcıya saat dilimi
    /// farkı kadar yanlış bir zaman gösterir.
    /// </remarks>
    public DateTime LastModifiedLocal => LastModifiedUtc.ToLocalTime();

    /// <summary>
    /// Coop'un kendi yedek kuşaklarından biri mi? (<c>&lt;ad&gt;.backup1</c> / <c>.backup2</c>)
    /// </summary>
    /// <remarks>
    /// Bunlar server tarafından üretiliyor ve bizim yönetimimizde değil. Ayrıca upstream
    /// #3341'e göre F10 ile durdurulunca üçü de aynı içeriğe düşüyor — yani güvenilmezler.
    /// UI'da ayrı gösterilmeli, "kampanya" gibi sunulmamalı.
    /// </remarks>
    public bool IsCoopBackup { get; init; }

    /// <summary>Coop'un yeni dünya kurarken şablon olarak kullandığı dosya.</summary>
    public bool IsTemplate => Name.Equals("default_new_game", StringComparison.OrdinalIgnoreCase);

    /// <summary>Çift tam mı? Eksik eşli bir save kurtarılamaz sayılmalı.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Save'e kayıtlı oyuncular (eş JSON'dan). Okunamadıysa boş.</summary>
    public IReadOnlyList<CoopSessionPlayer> Players { get; init; } = Array.Empty<CoopSessionPlayer>();

    public int PlayerCount => Players.Count;

    /// <summary>Bu isim bir dosya adı olarak güvenli mi? (path traversal koruması)</summary>
    public static bool IsValidSaveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > 64) return false;

        // Yol ayırıcı veya sürücü belirteci içeren hiçbir ad kabul edilmez.
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':')) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (name.StartsWith('.') || name.EndsWith('.')) return false;
        if (name.Trim() != name) return false;

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

        // Windows'ta ayrılmış cihaz adları
        var stem = name.Split('.')[0];
        string[] reserved = ["CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) return false;

        return true;
    }
}
