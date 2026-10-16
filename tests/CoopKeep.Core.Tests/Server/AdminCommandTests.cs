using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>
/// Yönetici komutları testleri.
/// </summary>
/// <remarks>
/// Söz dizimleri ve yanıtlar Coop v0.1.6 sunucusundan okundu (2026-10-16), bkz.
/// <c>Fixtures/admin-responses.txt</c>. v0.1.0'ın kullandığı <c>coop.debug.hero.SetGold</c>
/// artık yok — eski testler o komutu sabitlediği için hatayı yakalayamamıştı.
/// </remarks>
public class AdminCommandTests
{
    private static readonly string[] Fikstur =
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "admin-responses.txt"));

    /// <summary>Fikstürde bir komutun yanıt satırları.</summary>
    private static IReadOnlyList<string> Yanit(string komut)
    {
        var i = Array.FindIndex(Fikstur, l => l == "[DedicatedServer] > " + komut);
        Assert.True(i >= 0, "fikstürde yok: " + komut);
        return Fikstur.Skip(i + 1).TakeWhile(l => !l.StartsWith("[DedicatedServer] > ")).ToList();
    }

    [Fact]
    public void altin_kimlikle_ve_set_gold_state_ile_ayarlanir()
        => Assert.Equal("coop.debug.hero.set_gold_state main_hero 5000", AdminCommand.SetGold("main_hero", 5000));

    [Fact]
    public void eski_SetGold_komutu_artik_uretilmiyor()
    {
        Assert.DoesNotContain("SetGold", AdminCommand.SetGold("main_hero", 1));
        Assert.False(AdminCommand.IsAllowed("coop.debug.hero.SetGold Aderimo 100"));
    }

    [Fact]
    public void set_hitpoints_kimlik_alir()
        => Assert.Equal("coop.debug.hero.set_hitpoints main_hero 100", AdminCommand.Heal("main_hero"));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-50, 1)]
    [InlineData(500, 100)]
    [InlineData(50, 50)]
    public void can_puani_guvenli_araliga_kisitlanir(int istenen, int beklenen)
        => Assert.EndsWith(" " + beklenen, AdminCommand.SetHitPoints("main_hero", istenen));

    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(999_999_999, AdminCommand.MaxGold)]
    [InlineData(1000, 1000)]
    public void altin_guvenli_araliga_kisitlanir(int istenen, int beklenen)
        => Assert.EndsWith(" " + beklenen, AdminCommand.SetGold("main_hero", istenen));

    [Theory]
    [InlineData(5, 18)]
    [InlineData(300, 90)]
    [InlineData(35, 35)]
    public void yas_guvenli_araliga_kisitlanir(int istenen, int beklenen)
        => Assert.EndsWith(" " + beklenen, AdminCommand.SetAge("main_hero", istenen));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    [InlineData("Ayşe Kılıç")]         // boşluklu: bu bir isim, kimlik değil
    [InlineData("main_hero\"x")]
    public void gecersiz_kimlik_reddedilir(string hedef)
    {
        Assert.Throws<ArgumentException>(() => AdminCommand.SetGold(hedef, 100));
        Assert.Throws<ArgumentException>(() => AdminCommand.SetHitPoints(hedef, 50));
    }

    [Fact]
    public void satir_sonu_enjeksiyonu_kimligi_bozar_ve_reddedilir()
        => Assert.Throws<ArgumentException>(() => AdminCommand.SetGold("main_hero\ncampaign.kill_hero X", 100));

    // --- allow-list ------------------------------------------------------

    [Theory]
    [InlineData("coop.debug.hero.set_gold_state main_hero 100")]
    [InlineData("coop.debug.hero.set_hitpoints main_hero 100")]
    [InlineData("coop.debug.hero.info main_hero")]
    [InlineData("coop.debug.campaign_options.list")]
    [InlineData("coop.server.shutdown 60")]
    public void izinli_yonetici_komutlari_kabul_edilir(string komut)
        => Assert.True(AdminCommand.IsAllowed(komut));

    [Theory]
    [InlineData("campaign.add_gold_to_hero 99999")]
    [InlineData("campaign.kill_hero Aderimo")]
    [InlineData("coop.debug.hero.kill_player main_hero battle")]
    [InlineData("ui.toggle_ui")]
    [InlineData("coop.debug.hero.set_gold_state main_hero 100\ncampaign.kill_hero X")]
    public void izinsiz_komutlar_reddedilir(string komut)
        => Assert.False(AdminCommand.IsAllowed(komut));

    // --- gerçek sunucu yanıtları -----------------------------------------

    [Fact]
    public void altin_yaniti_basarili_ve_degerler_okunur()
    {
        var yanit = Yanit("coop.debug.hero.set_gold_state main_hero 12345");

        Assert.Equal(AdminOutcome.Success, AdminResponseParser.Classify(yanit));
        Assert.Equal((7000, 12345), AdminResponseParser.ParseGoldSet(yanit));
    }

    [Theory]
    [InlineData("coop.debug.hero.set_hitpoints main_hero 60", AdminOutcome.Success)]
    [InlineData("coop.debug.hero.set_age main_hero 30", AdminOutcome.Success)]
    [InlineData("coop.debug.hero.set_hitpoints NoSuchHero 50", AdminOutcome.NotFound)]
    [InlineData("coop.debug.hero.set_gold NoSuchHero 10", AdminOutcome.NotFound)]
    [InlineData("coop.debug.hero.SetGold", AdminOutcome.UnknownCommand)]
    [InlineData("campaign.add_gold_to_hero 100", AdminOutcome.CheatsDisabled)]
    public void yanitlar_dogru_siniflandirilir(string komut, AdminOutcome beklenen)
        => Assert.Equal(beklenen, AdminResponseParser.Classify(Yanit(komut)));

    [Fact]
    public void yanitsiz_komut_basarili_sayilmaz()
        => Assert.Equal(AdminOutcome.NoResponse, AdminResponseParser.Classify(["[Coop] alakasiz bir log satiri"]));

    [Fact]
    public void kahraman_bilgisi_okunur()
    {
        // coop.debug.hero.info main_hero çıktısından kesit (set_age ve set_gold sonrası).
        string[] satirlar =
        [
            "<Weight>k__BackingField = 0.5",
            "_characterObject = Pacarios",
            "_firstName = Pacarios",
            "_name = Pacarios",
            "_health = 60",
            "_defaultAge = 30",
            "_birthDay = Summer 1, 1054",
            "_gold = 2222",
            "_heroDeveloper = TaleWorlds.CampaignSystem.CharacterDevelopment.HeroDeveloper",
        ];

        var bilgi = AdminResponseParser.ParseHeroInfo(satirlar);

        Assert.Equal(new HeroInfo("Pacarios", 2222, 60, 30), bilgi);
        Assert.True(AdminResponseParser.IsTerminal(satirlar[^1]));
    }

    [Fact]
    public void ayar_listesi_okunur()
    {
        string[] satirlar =
        [
            "PlayerReceivedDamageDifficulty: Realistic",
            "BattleDeath: Realistic",
            "[Coop] Mission state: dropped 0",
            "ClientsCanUseCheats: True",
        ];

        var liste = AdminResponseParser.ParseKeyValueList(satirlar);

        Assert.Equal(3, liste.Count);
        Assert.Equal("Realistic", liste["BattleDeath"]);
        Assert.Equal("True", liste["ClientsCanUseCheats"]);
    }
}
