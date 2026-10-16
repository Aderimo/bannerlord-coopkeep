using System.Globalization;

namespace CoopKeep.Core.Server;

/// <summary>
/// Sunucu sahibinin bir oyuncuya uygulayabileceği yönetici işlemleri.
/// </summary>
/// <remarks>
/// <para>
/// Söz dizimleri <b>tahmin edilmedi</b>; 2026-10-16'da Coop v0.1.6 sunucusuna argümansız
/// gönderilerek kullanım metinleri doğrudan sunucudan okundu ve her komut gerçek bir
/// dünyada denenip sonucu <c>coop.debug.hero.info</c> ile doğrulandı:
/// </para>
/// <code>
/// Usage: coop.debug.hero.set_gold_state &lt;hero_id&gt; &lt;gold&gt;       → HERO_GOLD_SET hero=… oldGold=… newGold=…
/// Usage: coop.debug.hero.set_hitpoints &lt;hero_id&gt; &lt;hit_points&gt;   → Hero HitPoints changed to: 60
/// Usage: coop.debug.hero.set_age &lt;hero_name_or_id&gt; &lt;age&gt;         → Set age to 30 for 1 hero(es) matching '…'
/// Usage: coop.debug.hero.info &lt;hero_id&gt;                           → _name = … / _gold = … / _health = …
/// </code>
/// <para>
/// <b>v0.1.0'daki hata:</b> altın komutu <c>coop.debug.hero.SetGold</c> idi. Coop bu komutu
/// kaldırdı (<c>no game console command 'coop.debug.hero.SetGold'</c>) ve yerine isim alan
/// <c>set_gold</c> ile kimlik alan <c>set_gold_state</c> geldi. Ayrıca eski kod hedef olarak
/// oyuncunun Steam adını gönderiyordu; oysa komut karakterin oyun içi adını istiyordu.
/// Artık <b>kimlik</b> alan <c>set_gold_state</c> kullanılıyor: aynı adlı iki karakter
/// olsa bile yanlış kişiye uygulanamaz.
/// </para>
/// <para>
/// Vanilla hile komutları (<c>campaign.add_gold_to_hero</c> vb.) sunucuda
/// <c>Cheat mode is disabled!</c> ile reddediliyor ve hile modunu açacak bir konsol komutu
/// yok. Oyuncuların kendi hile menüleri ise <c>mod-config.json</c>'daki
/// <c>clientsCanUseCheats</c> ile açılıyor (dünya ayarlarında).
/// </para>
/// </remarks>
public static class AdminCommand
{
    /// <summary>
    /// Yönetici işlemleri için izin verilen komut önekleri.
    /// </summary>
    /// <remarks>
    /// Sunucu her oyun konsol komutunu kabul ediyor. Yalnızca bunlara izin veriyoruz;
    /// geri kalanı arayüzden erişilebilir olmamalı.
    /// </remarks>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "coop.debug.hero.set_gold_state",
        "coop.debug.hero.set_hitpoints",
        "coop.debug.hero.set_age",
        "coop.debug.hero.info",
        "coop.debug.clan.give_influence",
        "coop.debug.clan.add_renown",
        "coop.debug.hero_developer.add_skill_xp",
        "coop.debug.hero_developer.add_attribute_points",
        "coop.debug.hero_developer.add_focus_points",
        "coop.debug.mobile_party.add_troops",
        "coop.debug.mobile_party.add_troop_xp",
        "coop.debug.mobileparty.info",
        "coop.debug.players.list",
        "coop.debug.campaign_options.list",
        "coop.debug.mod_config.list",
        "coop.server.shutdown",
    };

    /// <summary>"Tam can" için kullanılan değer.</summary>
    /// <remarks>
    /// <c>hero.info</c> azami canı vermiyor. Mevcut can bundan yüksekse (perklerle 100'ü
    /// geçebiliyor) iyileştirme <b>uygulanmaz</b> — "tam can" düğmesi canı düşürmemeli.
    /// </remarks>
    public const int FullHitPoints = 100;

    /// <summary>Altın için üst sınır — kazara girilen uçuk değerleri engeller.</summary>
    public const int MaxGold = 10_000_000;

    public const int MinAge = 18;
    public const int MaxAge = 90;

    /// <summary>Kahramanın altınını <b>belirtilen değere ayarlar</b>.</summary>
    /// <param name="heroId">Kayıtlı kahraman kimliği (örn. <c>main_hero</c>).</param>
    public static string SetGold(string heroId, int gold) =>
        $"coop.debug.hero.set_gold_state {RequireId(heroId)} {Math.Clamp(gold, 0, MaxGold).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Kahramanın can puanını ayarlar.</summary>
    /// <param name="hitPoints">
    /// 1 ile 100 arası. <b>Sıfıra izin verilmiyor:</b> öldürmek için Coop'un ayrı bir
    /// komutu var (<c>kill_player</c>); canı sıfırlamanın ne yapacağı belirsiz.
    /// </param>
    public static string SetHitPoints(string heroId, int hitPoints) =>
        $"coop.debug.hero.set_hitpoints {RequireId(heroId)} {Math.Clamp(hitPoints, 1, FullHitPoints).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Kahramanı tam cana getirir.</summary>
    public static string Heal(string heroId) => SetHitPoints(heroId, FullHitPoints);

    /// <summary>Kahramanın yaşını ayarlar.</summary>
    public static string SetAge(string heroId, int age) =>
        $"coop.debug.hero.set_age {RequireId(heroId)} {Math.Clamp(age, MinAge, MaxAge).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Tek bir kahramanın ayrıntılarını sorgular (ad, altın, can, yaş).</summary>
    public static string HeroInfo(string heroId) => $"coop.debug.hero.info {RequireId(heroId)}";

    public const int MaxInfluence = 10_000;
    public const int MaxRenown = 10_000;
    public const int MaxSkillXp = 100_000;
    public const int MaxPoints = 20;
    public const int MaxTroopXp = 1_000_000;

    /// <summary>Oyunun yetenek kimlikleri (<c>add_skill_xp</c> için).</summary>
    public static readonly IReadOnlyList<string> Skills =
    [
        "OneHanded", "TwoHanded", "Polearm", "Bow", "Crossbow", "Throwing",
        "Riding", "Athletics", "Crafting", "Scouting", "Tactics", "Roguery",
        "Charm", "Leadership", "Trade", "Steward", "Medicine", "Engineering",
    ];

    /// <summary>Klana nüfuz ekler. Usage: <c>coop.debug.clan.give_influence &lt;clan_id&gt; &lt;amount&gt;</c></summary>
    public static string GiveInfluence(string clanId, int amount) =>
        $"coop.debug.clan.give_influence {RequireId(clanId)} {Math.Clamp(amount, 1, MaxInfluence).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Klana ün ekler. Usage: <c>coop.debug.clan.add_renown &lt;clan_id&gt; &lt;renown&gt;</c></summary>
    public static string AddRenown(string clanId, int amount) =>
        $"coop.debug.clan.add_renown {RequireId(clanId)} {Math.Clamp(amount, 1, MaxRenown).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Yetenek deneyimi. Usage: <c>add_skill_xp &lt;hero_name_or_id&gt; &lt;skill_name&gt; &lt;xp_amount&gt;</c></summary>
    public static string AddSkillXp(string heroId, string skill, int xp)
    {
        if (!Skills.Contains(skill))
            throw new ArgumentException($"Bilinmeyen yetenek: '{skill}'", nameof(skill));

        return $"coop.debug.hero_developer.add_skill_xp {RequireId(heroId)} {skill} {Math.Clamp(xp, 1, MaxSkillXp).ToString(CultureInfo.InvariantCulture)}";
    }

    public static string AddAttributePoints(string heroId, int points) =>
        $"coop.debug.hero_developer.add_attribute_points {RequireId(heroId)} {Math.Clamp(points, 1, MaxPoints).ToString(CultureInfo.InvariantCulture)}";

    public static string AddFocusPoints(string heroId, int points) =>
        $"coop.debug.hero_developer.add_focus_points {RequireId(heroId)} {Math.Clamp(points, 1, MaxPoints).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Kahramanın partisine Coop'un sabit asker grubunu ekler.
    /// </summary>
    /// <remarks>
    /// Usage: <c>coop.debug.mobile_party.add_troops &lt;hero_name&gt;</c>. Tür ve sayı seçilemiyor:
    /// komut Coop'un hata ayıklama aracı ve sabit bir acemi grubu ekliyor. Partisi olmayan
    /// kahramanda <c>No hero named "…" with a party found.</c> döner. İsim aldığı için
    /// boşluklu adlar tırnakla gönderilir.
    /// </remarks>
    public static string AddTroops(string heroName) =>
        $"coop.debug.mobile_party.add_troops {QuoteName(heroName)}";

    /// <summary>Partideki askerlere deneyim (terfi için). Usage: <c>add_troop_xp &lt;hero_name&gt; &lt;xp_amount&gt;</c></summary>
    public static string AddTroopXp(string heroName, int xp) =>
        $"coop.debug.mobile_party.add_troop_xp {QuoteName(heroName)} {Math.Clamp(xp, 1, MaxTroopXp).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Partinin konumu, hedefi, morali. Usage: <c>coop.debug.mobileparty.info &lt;partyStringId&gt;</c></summary>
    public static string PartyInfo(string partyId) => $"coop.debug.mobileparty.info {RequireId(partyId)}";

    /// <summary>İsim alan komutlar için: boşluk varsa çift tırnak; tırnak karakterine izin yok.</summary>
    private static string QuoteName(string? name)
    {
        var clean = ServerCommand.SanitizeArgument(name, 64);
        if (clean.Length == 0)
            throw new ArgumentException("Kahraman adı boş olamaz.", nameof(name));
        if (clean.Contains('"'))
            throw new ArgumentException($"Geçersiz kahraman adı: '{clean}'", nameof(name));

        return clean.Contains(' ') ? "\"" + clean + "\"" : clean;
    }

    /// <summary>Bağlı oyuncuların kahraman eşlemesini sorgular.</summary>
    public static string PlayersList() => "coop.debug.players.list";

    /// <summary>Sunucuda o an geçerli zorluk ayarları.</summary>
    public static string CampaignOptionsList() => "coop.debug.campaign_options.list";

    /// <summary>Sunucuda o an geçerli Coop ayarları.</summary>
    public static string ModConfigList() => "coop.debug.mod_config.list";

    /// <summary>
    /// Oyunculara süre tanıyarak güvenli kapatma: yeni girişleri kapatır, süre bitince
    /// herkesi düşürür ve kaydeder.
    /// </summary>
    public static string ScheduleShutdown(int seconds) =>
        "coop.server.shutdown " + Math.Clamp(seconds, 5, 3600).ToString(CultureInfo.InvariantCulture);

    public static string CancelShutdown() => "coop.server.shutdown cancel";

    /// <summary>Komut satırının yönetici allow-list'inden geçip geçmediği.</summary>
    public static bool IsAllowed(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        if (commandLine.Any(c => c is '\r' or '\n')) return false;

        var verb = commandLine.Split(' ', 2)[0];
        return Allowed.Contains(verb);
    }

    /// <summary>
    /// Kahraman kimliğini doğrular. Kimlikler boşluk ve tırnak içermez; içeriyorsa bu
    /// bir kimlik değil, büyük olasılıkla bir isimdir — sessizce yanlış komut üretmeyelim.
    /// </summary>
    private static string RequireId(string? heroId)
    {
        var id = ServerCommand.SanitizeArgument(heroId, 64);
        if (id.Length == 0)
            throw new ArgumentException("Kahraman kimliği boş olamaz.", nameof(heroId));
        if (id.Any(c => c is ' ' or '"' or '\''))
            throw new ArgumentException($"Geçersiz kahraman kimliği: '{id}'", nameof(heroId));

        return id;
    }
}

/// <summary>
/// Yöneticinin uyguladığı bir işlemin kaydı.
/// </summary>
/// <remarks>
/// Bu işlemler oyunun dengesini değiştirebiliyor. Sessizce yapılmaları, birlikte
/// oynayan insanlar arasında güven sorunu yaratır — bu yüzden her biri kaydediliyor
/// ve arayüzde görünür kılınıyor.
/// </remarks>
/// <param name="At">İşlemin zamanı.</param>
/// <param name="PlayerName">Hedef oyuncunun adı.</param>
/// <param name="Description">Ne yapıldığı (yerelleştirilmiş metin).</param>
public readonly record struct AdminAuditEntry(DateTimeOffset At, string PlayerName, string Description)
{
    public string TimeText => At.ToLocalTime().ToString("HH:mm:ss");
}
