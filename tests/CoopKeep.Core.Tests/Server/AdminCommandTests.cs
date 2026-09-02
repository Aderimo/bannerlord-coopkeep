using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>
/// Yönetici komutları testleri.
/// </summary>
/// <remarks>
/// Söz dizimleri çalışan sunucudan okundu (2026-09-02):
/// <c>SetGold &lt;heroName&gt; &lt;gold&gt;</c>, <c>set_hitpoints &lt;heroId&gt; &lt;hitPoints&gt;</c>.
/// </remarks>
public class AdminCommandTests
{
    [Fact]
    public void SetGold_isim_alir_ve_dogru_bicimde_uretilir()
        => Assert.Equal("coop.debug.hero.SetGold Aderimo 5000", AdminCommand.SetGold("Aderimo", 5000));

    [Fact]
    public void set_hitpoints_kimlik_alir()
    {
        // SetGold isim, set_hitpoints KİMLİK alıyor. Karıştırmak sessizce
        // işe yaramayan bir komut üretirdi.
        Assert.Equal("coop.debug.hero.set_hitpoints Hero_Player 100",
            AdminCommand.SetHitPoints("Hero_Player", 100));
    }

    [Fact]
    public void iyilestirme_tam_cana_getirir()
        => Assert.Equal("coop.debug.hero.set_hitpoints Hero_Player 100", AdminCommand.Heal("Hero_Player"));

    [Theory]
    [InlineData(0, 1)]        // sıfır cana izin yok: ölüm anlamı doğrulanmadı
    [InlineData(-50, 1)]
    [InlineData(500, 100)]    // azami can 100
    [InlineData(50, 50)]
    public void can_puani_guvenli_araliga_kisitlanir(int istenen, int beklenen)
        => Assert.EndsWith(" " + beklenen, AdminCommand.SetHitPoints("Hero_X", istenen));

    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(999_999_999, AdminCommand.MaxGold)]
    [InlineData(1000, 1000)]
    public void altin_guvenli_araliga_kisitlanir(int istenen, int beklenen)
        => Assert.EndsWith(" " + beklenen, AdminCommand.SetGold("Aderimo", istenen));

    [Fact]
    public void arguman_enjeksiyonu_etkisiz_kalir()
    {
        var komut = AdminCommand.SetGold("Aderimo\ncampaign.add_gold_to_hero 99999", 100);

        Assert.DoesNotContain('\n', komut);
        Assert.True(AdminCommand.IsAllowed(komut));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void bos_hedef_reddedilir(string hedef)
    {
        Assert.Throws<ArgumentException>(() => AdminCommand.SetGold(hedef, 100));
        Assert.Throws<ArgumentException>(() => AdminCommand.SetHitPoints(hedef, 50));
    }

    // --- allow-list ------------------------------------------------------

    [Theory]
    [InlineData("coop.debug.hero.SetGold Aderimo 100")]
    [InlineData("coop.debug.hero.set_hitpoints Hero_Player 100")]
    [InlineData("coop.debug.players.list")]
    public void izinli_yonetici_komutlari_kabul_edilir(string komut)
        => Assert.True(AdminCommand.IsAllowed(komut));

    [Theory]
    [InlineData("campaign.add_gold_to_hero 99999")]   // sunucu zaten reddediyor ama biz de göndermeyiz
    [InlineData("campaign.kill_hero Aderimo")]
    [InlineData("coop.debug.mapevent.kill_own_team")]
    [InlineData("ui.toggle_ui")]
    [InlineData("coop.debug.hero.SetGold Aderimo 100\ncampaign.kill_hero X")]
    public void izinsiz_komutlar_reddedilir(string komut)
        => Assert.False(AdminCommand.IsAllowed(komut));

    [Fact]
    public void yonetici_allow_listesi_dar_tutuluyor()
    {
        // Sunucu 594 oyun komutunu kabul ediyor; biz yalnızca bir avuç tanesine
        // izin veriyoruz. Bu sayı büyürse bilinçli bir karar olmalı.
        Assert.Equal(5, AdminCommand.Allowed.Count);
    }
}
