namespace CoopKeep.Core.Server;

/// <summary>
/// Sunucu sahibinin bir oyuncuya uygulayabileceği yönetici işlemleri.
/// </summary>
/// <remarks>
/// <para>
/// Bu komutlar sunucunun oyun konsoluna gider. Söz dizimleri <b>tahmin edilmedi</b>;
/// 2026-09-02'de çalışan sunucuya argümansız gönderilerek kullanım metinleri
/// doğrudan sunucudan okundu:
/// </para>
/// <code>
/// Usage: coop.debug.hero.SetGold &lt;heroName&gt; &lt;gold&gt;
/// Usage: coop.debug.hero.set_hitpoints &lt;heroId&gt; &lt;hitPoints&gt;
/// Usage: coop.debug.hero.info &lt;heroId&gt;
/// </code>
/// <para>
/// <b>Dikkat: SetGold isim, set_hitpoints kimlik alıyor.</b> İkisini karıştırmak
/// sessizce işe yaramayan bir komut üretir.
/// </para>
/// <para>
/// Vanilla hile komutları (<c>campaign.add_gold_to_hero</c>, <c>campaign.kill_hero</c>,
/// <c>campaign.heal_player_party</c>) sunucu tarafından <c>"Cheat mode is disabled!"</c>
/// ile reddediliyor — bu yüzden yalnızca Coop'un kendi komutları kullanılıyor.
/// </para>
/// </remarks>
public static class AdminCommand
{
    /// <summary>
    /// Yönetici işlemleri için izin verilen komut önekleri.
    /// </summary>
    /// <remarks>
    /// Sunucu 594 oyun konsol komutunun tamamını kabul ediyor. Yalnızca bu üçüne
    /// izin veriyoruz; geri kalanı arayüzden erişilebilir olmamalı.
    /// </remarks>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "coop.debug.hero.SetGold",
        "coop.debug.hero.set_hitpoints",
        "coop.debug.hero.info",
        "coop.debug.hero.list",
        "coop.debug.players.list",
    };

    /// <summary>Bannerlord'da bir kahramanın normal azami can puanı.</summary>
    public const int FullHitPoints = 100;

    /// <summary>Altın için üst sınır — kazara girilen uçuk değerleri engeller.</summary>
    public const int MaxGold = 10_000_000;

    /// <summary>
    /// Bir kahramanın altınını <b>belirtilen değere ayarlar</b>.
    /// </summary>
    /// <remarks>
    /// Komut ekleme değil <i>atama</i> yapıyor. "1000 ekle" gibi bir işlem için
    /// çağıranın önce mevcut altını okuması gerekir.
    /// </remarks>
    /// <param name="heroName">Kahramanın <b>adı</b> (kimliği değil).</param>
    public static string SetGold(string heroName, int gold)
    {
        var name = ServerCommand.SanitizeArgument(heroName, 64);
        if (name.Length == 0)
            throw new ArgumentException("Kahraman adı boş olamaz.", nameof(heroName));

        var amount = Math.Clamp(gold, 0, MaxGold);
        return $"coop.debug.hero.SetGold {name} {amount}";
    }

    /// <summary>
    /// Bir kahramanın can puanını ayarlar.
    /// </summary>
    /// <param name="heroId">Kahramanın <b>kimliği</b> (adı değil), örn. <c>Hero_Player</c>.</param>
    /// <param name="hitPoints">
    /// 1 ile 100 arası. <b>Sıfıra izin verilmiyor:</b> sıfır canın ne yapacağı
    /// (yaralama mı, kalıcı ölüm mü) doğrulanmadı ve başkasının karakterini geri
    /// dönüşü olmadan bozabilir.
    /// </param>
    public static string SetHitPoints(string heroId, int hitPoints)
    {
        var id = ServerCommand.SanitizeArgument(heroId, 64);
        if (id.Length == 0)
            throw new ArgumentException("Kahraman kimliği boş olamaz.", nameof(heroId));

        var hp = Math.Clamp(hitPoints, 1, FullHitPoints);
        return $"coop.debug.hero.set_hitpoints {id} {hp}";
    }

    /// <summary>Kahramanı tam cana getirir.</summary>
    public static string Heal(string heroId) => SetHitPoints(heroId, FullHitPoints);

    /// <summary>Tek bir kahramanın ayrıntılarını sorgular (mevcut altın dahil).</summary>
    public static string HeroInfo(string heroId)
    {
        var id = ServerCommand.SanitizeArgument(heroId, 64);
        if (id.Length == 0)
            throw new ArgumentException("Kahraman kimliği boş olamaz.", nameof(heroId));

        return $"coop.debug.hero.info {id}";
    }

    /// <summary>Bağlı oyuncuların kahraman eşlemesini sorgular.</summary>
    public static string PlayersList() => "coop.debug.players.list";

    /// <summary>Komut satırının yönetici allow-list'inden geçip geçmediği.</summary>
    public static bool IsAllowed(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        if (commandLine.Any(c => c is '\r' or '\n')) return false;

        var verb = commandLine.Split(' ', 2)[0];
        return Allowed.Contains(verb);
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
