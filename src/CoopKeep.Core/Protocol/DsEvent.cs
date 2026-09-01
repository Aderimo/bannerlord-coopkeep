namespace CoopKeep.Core.Protocol;

/// <summary>
/// Dedicated server'ın stdout'a bastığı <c>@DS@</c> önekli makine-okunur olaylar.
/// </summary>
/// <remarks>
/// Bu, server'ı dışarıdan yönetmenin birincil veri kanalı. Coop modülünün DLL'leri
/// SHA-256 ile pinlendiği için mod'a kod ekleyemiyoruz; elimizdeki tek yapılandırılmış
/// arayüz bu protokol.
/// </remarks>
public abstract record DsEvent
{
    private DsEvent() { }

    /// <summary><c>{"ev":"state","phase":"serving","save":"...","pw":false}</c></summary>
    public sealed record State(
        ServerPhase Phase,
        string RawPhase,
        string? SaveName,
        bool PasswordRequired) : DsEvent;

    /// <summary>
    /// <c>{"ev":"players","list":[{"id":0,"name":"...","state":"on map","addr":"..."}]}</c>
    /// <para>
    /// Server her değişimde tam listeyi yeniden yayınlıyor — delta değil, snapshot.
    /// </para>
    /// </summary>
    public sealed record Players(IReadOnlyList<ConnectedPlayer> List) : DsEvent;

    /// <summary>
    /// <c>{"ev":"commands","builtin":[...],"game":[...]}</c> — açılışta bir kez gelir.
    /// <para>
    /// <c>builtin</c>: status, players, save, say, kick, stop, help (7 adet).
    /// <c>game</c>: 594 motor konsol komutu. Bunların çoğu hile niteliğinde
    /// (<c>campaign.add_gold_to_hero</c> gibi), bu yüzden gönderdiğimiz komutlar
    /// allow-list'ten geçmek zorunda.
    /// </para>
    /// </summary>
    public sealed record Commands(
        IReadOnlyList<string> Builtin,
        IReadOnlyList<string> Game) : DsEvent;

    /// <summary>
    /// Tanınmayan bir <c>ev</c> değeri. Upstream yeni olay tipi eklerse bizi çökertmemeli;
    /// loglanır ve yok sayılır.
    /// </summary>
    public sealed record Unknown(string EventName, string RawJson) : DsEvent;
}

/// <summary>
/// <c>@DS@ players</c> olayındaki tek bir oyuncu.
/// </summary>
/// <param name="Id">
/// Peer indeksi. <b>Kalıcı değil</b> — her oturumda yeniden atanır, kimlik olarak kullanılamaz.
/// </param>
/// <param name="Name">Oyuncu adı. Dışarıdan erişebildiğimiz tek yarı-kalıcı tanımlayıcı.</param>
/// <param name="State">
/// Bağlantı durumu: <c>handshake</c> → <c>creating character</c> → <c>loading</c> → <c>on map</c>.
/// </param>
/// <param name="Address">
/// <c>ip:port</c>. <b>Kimlik olarak kullanılamaz:</b> Steam relay üzerinden bağlanan
/// herkes <c>127.0.0.1</c> görünüyor.
/// </param>
public sealed record ConnectedPlayer(
    int Id,
    string Name,
    string State,
    string? Address)
{
    /// <summary>Server bu alanı boş bıraktığında kullandığı yer tutucu.</summary>
    public const string JoiningPlaceholder = "(joining)";

    /// <summary>Oyuncu haritaya girmiş ve oynuyor mu?</summary>
    public bool IsOnMap => string.Equals(State, "on map", StringComparison.OrdinalIgnoreCase);
}
