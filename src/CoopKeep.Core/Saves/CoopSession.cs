using System.Text.Json.Serialization;

namespace CoopKeep.Core.Saves;

/// <summary>
/// Bir kampanya save'inin yanındaki <c>.json</c> eşinin içeriği.
/// </summary>
/// <remarks>
/// <para>
/// Coop, her <c>&lt;ad&gt;.sav</c> dosyasının yanına bir <c>&lt;ad&gt;.json</c> yazar.
/// Bu dosya oyuncu→kahraman eşlemelerini ve alt sistem verilerini taşır. İkisi
/// <b>ayrılmaz bir çifttir</b> — biri diğeri olmadan anlamsızdır.
/// </para>
/// <para>
/// Bizim için kritik olan kısmı <see cref="Players"/>: server hiç çalışmıyorken bile
/// bir kampanyanın oyuncu listesini okuyabiliyoruz. Save Manager'daki
/// "Kampanya | Oyuncular | Son değişiklik" tablosu doğrudan buradan doluyor.
/// </para>
/// </remarks>
public sealed class CoopSession
{
    /// <summary>Genelde save'in adıyla aynı.</summary>
    public string? UniqueGameId { get; init; }

    public IReadOnlyList<CoopSessionPlayer> Players { get; init; } = Array.Empty<CoopSessionPlayer>();
}

/// <summary>
/// Save'e kayıtlı tek bir oyuncu.
/// </summary>
public sealed class CoopSessionPlayer
{
    /// <summary>
    /// Oyuncunun kalıcı kimliği — pratikte <b>SteamID64</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bu, dışarıdan erişebildiğimiz <b>tek kalıcı oyuncu kimliği</b>. Canlı
    /// <c>@DS@ players</c> olayı yalnızca <c>id</c> / <c>name</c> / <c>addr</c> veriyor;
    /// Steam ID protokole yazılmıyor. Save dosyası ise yazıyor.
    /// </para>
    /// <para>
    /// ⚠️ Yine de <b>doğrulanmış bir kimlik değil</b>: istemci bunu kendisi beyan ediyor
    /// ve Steam game server kimlik doğrulaması yapmadan (<c>eServerModeNoAuthentication</c>)
    /// çalışıyor. Ban gerekçesi olarak kullanılabilir, ama kesin koruma değildir.
    /// </para>
    /// </remarks>
    public string? ControllerId { get; init; }

    public string? HeroId { get; init; }
    public string? MobilePartyId { get; init; }
    public string? ClanId { get; init; }
    public string? CharacterObjectId { get; init; }

    /// <summary><see cref="ControllerId"/> geçerli bir SteamID64 biçiminde mi?</summary>
    [JsonIgnore]
    public bool HasSteamId => IsSteamId64(ControllerId);

    /// <summary>
    /// SteamID64 biçim kontrolü: 17 hane, tamamı rakam, bireysel hesap aralığında
    /// (<c>7656119…</c> öneki).
    /// </summary>
    public static bool IsSteamId64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length != 17) return false;

        foreach (var c in value)
            if (c is < '0' or > '9') return false;

        return value.StartsWith("7656119", StringComparison.Ordinal);
    }
}
