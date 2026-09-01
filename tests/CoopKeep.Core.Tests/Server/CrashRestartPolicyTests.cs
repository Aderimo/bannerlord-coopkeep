using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>
/// Çöküş sonrası yeniden başlatma politikası testleri.
/// </summary>
public class CrashRestartPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static ServerExitInfo Cokme(int kod = 3) =>
        new(kod, ServerExit.Classify(kod), WasExpected: false);

    [Fact]
    public void biz_durdurduysak_cokme_sayilmaz()
    {
        var politika = new CrashRestartPolicy();

        var karar = politika.Evaluate(new ServerExitInfo(0, ServerExitKind.Clean, WasExpected: true), T0);

        Assert.False(karar.ShouldRestart);
        Assert.Equal(CrashDecisionReason.NotACrash, karar.Reason);
    }

    [Fact]
    public void gercek_cokme_yeniden_baslatilir()
    {
        var politika = new CrashRestartPolicy();

        var karar = politika.Evaluate(Cokme(), T0);

        Assert.True(karar.ShouldRestart);
        Assert.Equal(CrashDecisionReason.Restarting, karar.Reason);
        Assert.True(karar.Delay > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(2)]  // save yüklenemedi
    [InlineData(4)]  // modül doğrulama hatası
    public void duzelmeyecek_hatalar_yeniden_baslatilmaz(int kod)
    {
        // Bu ikisi tekrar denemekle düzelmez; yeniden başlatmak sonsuz döngü olur.
        var politika = new CrashRestartPolicy();

        var karar = politika.Evaluate(Cokme(kod), T0);

        Assert.False(karar.ShouldRestart);
        Assert.Equal(CrashDecisionReason.NotRecoverable, karar.Reason);
    }

    [Fact]
    public void bekleme_suresi_her_cokmede_artar()
    {
        var politika = new CrashRestartPolicy(
            initialDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromMinutes(5),
            multiplier: 2,
            maxRestartsPerWindow: 10);

        var birinci = politika.Evaluate(Cokme(), T0);
        var ikinci = politika.Evaluate(Cokme(), T0.AddSeconds(30));
        var ucuncu = politika.Evaluate(Cokme(), T0.AddSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(10), birinci.Delay);
        Assert.Equal(TimeSpan.FromSeconds(20), ikinci.Delay);
        Assert.Equal(TimeSpan.FromSeconds(40), ucuncu.Delay);
    }

    [Fact]
    public void bekleme_suresi_tavani_asmaz()
    {
        var politika = new CrashRestartPolicy(
            initialDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromSeconds(25),
            multiplier: 10,
            maxRestartsPerWindow: 10);

        politika.Evaluate(Cokme(), T0);
        var ikinci = politika.Evaluate(Cokme(), T0.AddSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(25), ikinci.Delay);
    }

    [Fact]
    public void ust_uste_cokmede_otomasyon_durur()
    {
        // Açılışta çöken bir sunucu sonsuz döngüye girmemeli.
        var politika = new CrashRestartPolicy(
            maxRestartsPerWindow: 3,
            window: TimeSpan.FromMinutes(10));

        Assert.True(politika.Evaluate(Cokme(), T0).ShouldRestart);
        Assert.True(politika.Evaluate(Cokme(), T0.AddSeconds(10)).ShouldRestart);
        Assert.True(politika.Evaluate(Cokme(), T0.AddSeconds(20)).ShouldRestart);

        var dorduncu = politika.Evaluate(Cokme(), T0.AddSeconds(30));

        Assert.False(dorduncu.ShouldRestart);
        Assert.Equal(CrashDecisionReason.CrashLoop, dorduncu.Reason);
    }

    [Fact]
    public void pencere_disindaki_eski_cokmeler_unutulur()
    {
        var politika = new CrashRestartPolicy(
            maxRestartsPerWindow: 2,
            window: TimeSpan.FromMinutes(10));

        politika.Evaluate(Cokme(), T0);
        politika.Evaluate(Cokme(), T0.AddSeconds(5));

        // Bir saat sonra: eski çöküşler artık anlamlı değil, sayaç sıfırlanmalı
        var sonraki = politika.Evaluate(Cokme(), T0.AddHours(1));

        Assert.True(sonraki.ShouldRestart);
        Assert.Equal(TimeSpan.FromSeconds(10), sonraki.Delay); // ilk denemeye dönmüş
    }

    [Fact]
    public void temiz_cikis_sayaci_sifirlar()
    {
        var politika = new CrashRestartPolicy(maxRestartsPerWindow: 2);

        politika.Evaluate(Cokme(), T0);
        politika.Evaluate(Cokme(), T0.AddSeconds(5));

        // Kullanıcı durdurdu: geçmiş çöküşler artık geçersiz
        politika.Evaluate(new ServerExitInfo(0, ServerExitKind.Clean, WasExpected: true), T0.AddSeconds(10));

        var sonraki = politika.Evaluate(Cokme(), T0.AddSeconds(20));
        Assert.True(sonraki.ShouldRestart);
    }

    [Fact]
    public void kapaliyken_yeniden_baslatilmaz()
    {
        var politika = new CrashRestartPolicy { Enabled = false };

        var karar = politika.Evaluate(Cokme(), T0);

        Assert.False(karar.ShouldRestart);
        Assert.Equal(CrashDecisionReason.Disabled, karar.Reason);
    }

    [Fact]
    public void Reset_sayaci_temizler()
    {
        var politika = new CrashRestartPolicy(maxRestartsPerWindow: 1);

        politika.Evaluate(Cokme(), T0);
        Assert.False(politika.Evaluate(Cokme(), T0.AddSeconds(5)).ShouldRestart);

        politika.Reset();

        Assert.True(politika.Evaluate(Cokme(), T0.AddSeconds(10)).ShouldRestart);
    }
}
