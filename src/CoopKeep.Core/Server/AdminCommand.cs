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
        "coop.debug.clan.add_influence",
        "coop.debug.clan.add_renown",
        "coop.debug.clan.info",
        "coop.debug.clan.list",
        "coop.debug.kingdom.list",
        "coop.debug.hero.list",
        "coop.debug.mobile_party.set_troop_state",
        "coop.debug.mobile_party.set_troop_wounded",
        "coop.debug.mobile_party.character_ids",
        "coop.debug.mobile_party.position",
        "coop.debug.mobile_party.move_to_settlement",
        "coop.debug.settlements.owner_state",
        "coop.debug.item_object.data",
        "coopkeep.ping",
        "coopkeep.give_item",
        "coopkeep.take_item",
        "coopkeep.party_items",
        "coopkeep.items",
        "coopkeep.prices",
        "coopkeep.hero_stats",
        "coopkeep.add_skill",
        "coopkeep.add_skill_xp",
        "coopkeep.add_focus",
        "coopkeep.add_attribute",
        "coopkeep.add_unspent",
        "coopkeep.hero_equipment",
        "coopkeep.roster",
        "coopkeep.parties",
        "coopkeep.owners",
        "coopkeep.settlement_stats",
        "coopkeep.item_stats",
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
    public const int MaxTroopXp = 1_000_000;

    /// <summary>Klana nüfuz ekler. Usage: <c>coop.debug.clan.give_influence &lt;clan_id&gt; &lt;amount&gt;</c></summary>
    public static string GiveInfluence(string clanId, int amount) =>
        $"coop.debug.clan.give_influence {RequireId(clanId)} {Math.Clamp(amount, 1, MaxInfluence).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Klanın nüfuzunu artırır ya da <b>azaltır</b> (eksi değer).
    /// </summary>
    /// <remarks>
    /// Usage: <c>coop.debug.clan.add_influence &lt;clan_id&gt; &lt;amount&gt;</c> →
    /// <c>Applied -50 influence to 'Osticos'; clan is now at 415 influence</c>. Eksi değeri
    /// kabul ettiği ve yeni değeri yazdığı için hem "ver" hem "al" bununla yapılır.
    /// </remarks>
    public static string AddInfluence(string clanId, int amount) =>
        $"coop.debug.clan.add_influence {RequireId(clanId)} {Math.Clamp(amount, -MaxInfluence, MaxInfluence).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Klanın ünü, nüfuzu, lideri, krallığı. Usage: <c>coop.debug.clan.info &lt;clan_id&gt;</c></summary>
    public static string ClanInfo(string clanId) => $"coop.debug.clan.info {RequireId(clanId)}";

    public static string HeroList() => "coop.debug.hero.list";
    public static string ClanList() => "coop.debug.clan.list";
    public static string KingdomList() => "coop.debug.kingdom.list";

    /// <summary>Bir partide en fazla bu kadar asker ayarlanabilir.</summary>
    public const int MaxTroopCount = 1000;

    /// <summary>
    /// Partideki bir asker türünün sayısını <b>ayarlar</b> (vermek de almak da bununla).
    /// </summary>
    /// <remarks>
    /// Usage: <c>coop.debug.mobile_party.set_troop_state &lt;party_id&gt; &lt;character_id&gt; &lt;exists&gt; &lt;number&gt; &lt;wounded_count&gt; &lt;xp&gt;</c>
    /// → <c>TROOP_STATE_SET party=… character=… oldNumber=1 … newNumber=25 …</c>.
    /// Sayı 0 olunca tür kadrodan çıkarılır (<c>exists=false</c>). Kahraman satırlarını
    /// kabul etmiyor (<c>Hero roster elements are not supported</c>).
    /// </remarks>
    public static string SetTroopState(string partyId, string troopId, int number, int wounded = 0, int xp = 0)
    {
        number = Math.Clamp(number, 0, MaxTroopCount);
        wounded = Math.Clamp(wounded, 0, number);
        var exists = number > 0 ? "true" : "false";
        return string.Create(CultureInfo.InvariantCulture,
            $"coop.debug.mobile_party.set_troop_state {RequireId(partyId)} {RequireId(troopId)} {exists} {number} {wounded} {Math.Max(0, xp)}");
    }

    /// <summary>Yaralı asker sayısını ayarlar (0 = hepsini iyileştir).</summary>
    public static string SetTroopWounded(string partyId, string troopId, int wounded) =>
        string.Create(CultureInfo.InvariantCulture,
            $"coop.debug.mobile_party.set_troop_wounded {RequireId(partyId)} {RequireId(troopId)} {Math.Max(0, wounded)}");

    /// <summary>Kahramanın partisinin kadrosu. Usage: <c>coop.debug.mobile_party.character_ids &lt;hero_name_or_id&gt;</c></summary>
    /// <remarks>
    /// Bu komut Coop'un kayıt kimliğini (<c>Hero_lord_1_1</c>) tanımıyor, yalnızca oyunun kimliğini
    /// (<c>lord_1_1</c>) kabul ediyor (2026-10-17, test sunucusu). Save'deki oyuncu kimlikleri
    /// kayıt kimliği olduğundan önek atılır; v0.4'te "Asker al" listesi bu yüzden boştu.
    /// </remarks>
    public static string PartyRoster(string heroId) => $"coop.debug.mobile_party.character_ids {GameStringId(RequireId(heroId))}";

    /// <summary>Coop kayıt kimliğinden oyunun kimliği: <c>Hero_lord_1_1</c> → <c>lord_1_1</c>.</summary>
    public static string GameStringId(string registryId) =>
        registryId.StartsWith("Hero_", StringComparison.Ordinal) && registryId.Length > 5 ? registryId[5..] : registryId;

    /// <summary>Partinin konumu (hafif sorgu). Usage: <c>coop.debug.mobile_party.position &lt;party_id&gt;</c></summary>
    public static string PartyPosition(string partyId) => $"coop.debug.mobile_party.position {RequireId(partyId)}";

    /// <summary>
    /// Oyuncu partisini bir yerleşime ışınlar.
    /// </summary>
    /// <remarks>
    /// Usage: <c>coop.debug.mobile_party.move_to_settlement &lt;party_id&gt; &lt;settlement_id&gt; [&lt;instant&gt;]</c>.
    /// Yalnızca oyuncu partileri: lord partisinde <c>Party … is not a player party.</c> döner.
    /// </remarks>
    public static string MoveToSettlement(string partyId, string settlementId) =>
        $"coop.debug.mobile_party.move_to_settlement {RequireId(partyId)} {RequireId(settlementId)} true";

    /// <summary>Yerleşimin güncel sahibi. Usage: <c>coop.debug.settlements.owner_state &lt;settlementNameOrId&gt;</c></summary>
    public static string SettlementOwner(string settlementId) => $"coop.debug.settlements.owner_state {RequireId(settlementId)}";

    // --- CoopKeep sunucu eklentisi (ServerToolsInstaller) ---------------------

    /// <summary>Eklenti yüklü mü? → <c>COOPKEEP_TOOLS v1</c></summary>
    public static string ToolsPing() => "coopkeep.ping";

    /// <summary>Kahramanın partisinin envanterine eşya ekler. → <c>COOPKEEP_ITEM hero=… item=… old=… new=…</c></summary>
    public static string GiveItem(string heroId, string itemId, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.give_item {RequireId(heroId)} {RequireId(itemId)} {Math.Clamp(count, 1, 10_000)}");

    /// <summary>Envanterden eşya alır (en fazla elindeki kadar).</summary>
    public static string TakeItem(string heroId, string itemId, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.take_item {RequireId(heroId)} {RequireId(itemId)} {Math.Clamp(count, 1, 10_000)}");

    /// <summary>Envanter: <c>item_id: sayı</c> satırları, <c>COOPKEEP_ITEMS_END</c>.</summary>
    public static string PartyItems(string heroId) => $"coopkeep.party_items {RequireId(heroId)}";

    /// <summary>Oyundaki bütün eşyalar (<c>ITEM|…</c>).</summary>
    public static string ItemList() => "coopkeep.items";

    /// <summary>Kasaba fiyatları (<c>PRICE|…</c>).</summary>
    public static string ItemPrices() => "coopkeep.prices";

    /// <summary>Seviye, XP, özellikler ve beceriler (<c>HERO|…</c>, <c>ATTR|…</c>, <c>SKILL|…</c>).</summary>
    public static string ToolsHeroStats(string heroId) => $"coopkeep.hero_stats {RequireId(heroId)}";

    /// <summary>Beceri seviyesini değiştirir (eksi: düşürür), 0–330 arasında kalır.</summary>
    public static string ToolsAddSkill(string heroId, string skillId, int delta) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.add_skill {RequireId(heroId)} {RequireSkill(skillId)} {Math.Clamp(delta, -330, 330)}");

    /// <summary>Beceriye XP ekler (oyunun kendi öğrenme hızıyla; yüksek seviyede seviye zor artar).</summary>
    public static string ToolsAddSkillXp(string heroId, string skillId, int xp) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.add_skill_xp {RequireId(heroId)} {RequireSkill(skillId)} {Math.Clamp(xp, 1, 1_000_000)}");

    /// <summary>Beceriye odak puanı (eksi: geri alır).</summary>
    public static string ToolsAddFocus(string heroId, string skillId, int delta) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.add_focus {RequireId(heroId)} {RequireSkill(skillId)} {Math.Clamp(delta, -10, 10)}");

    /// <summary>Özelliğe puan (eksi: geri alır).</summary>
    public static string ToolsAddAttribute(string heroId, string attributeId, int delta) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.add_attribute {RequireId(heroId)} {RequireSkill(attributeId)} {Math.Clamp(delta, -10, 10)}");

    /// <summary>Harcanmamış özellik ya da odak puanı.</summary>
    public static string ToolsAddUnspent(string heroId, bool attribute, int delta) =>
        string.Create(CultureInfo.InvariantCulture, $"coopkeep.add_unspent {RequireId(heroId)} {(attribute ? "attribute" : "focus")} {Math.Clamp(delta, -100, 100)}");

    /// <summary>Savaş ve şehir kuşanımı (<c>EQ|…</c>).</summary>
    public static string ToolsHeroEquipment(string heroId) => $"coopkeep.hero_equipment {RequireId(heroId)}";

    /// <summary>Kahramanın partisinin kadrosu (<c>PARTY|…</c>, <c>TROOP|…</c>).</summary>
    public static string ToolsRoster(string heroId) => $"coopkeep.roster {RequireId(heroId)}";

    /// <summary>Bütün lord ve oyuncu partileri (<c>MP|…</c>).</summary>
    public static string ToolsParties() => "coopkeep.parties";

    /// <summary>Bütün yerleşimlerin sahibi (<c>OWN|…</c>).</summary>
    public static string ToolsOwners() => "coopkeep.owners";

    /// <summary>Kasaba ve kalelerin durumu (<c>SET|…</c>).</summary>
    public static string ToolsSettlementStats() => "coopkeep.settlement_stats";

    /// <summary>Silah, zırh ve at değerleri (<c>STAT|…</c>).</summary>
    public static string ToolsItemStats() => "coopkeep.item_stats";

    private static string RequireSkill(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(char.IsAsciiLetter)
            ? id
            : throw new ArgumentException($"Geçersiz beceri/özellik: '{id}'", nameof(id));

    /// <summary>Eşyanın oyun içi değeri. Usage: <c>coop.debug.item_object.data &lt;item_id&gt;</c> → <c>Value: 10</c></summary>
    public static string ItemData(string itemId) => $"coop.debug.item_object.data {RequireId(itemId)}";

    /// <summary>Klana ün ekler. Usage: <c>coop.debug.clan.add_renown &lt;clan_id&gt; &lt;renown&gt;</c></summary>
    public static string AddRenown(string clanId, int amount) =>
        $"coop.debug.clan.add_renown {RequireId(clanId)} {Math.Clamp(amount, 1, MaxRenown).ToString(CultureInfo.InvariantCulture)}";

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
