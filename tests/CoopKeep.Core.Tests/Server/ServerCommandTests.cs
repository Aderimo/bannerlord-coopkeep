using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>
/// Komut allow-list'i ve argüman temizliği testleri.
/// </summary>
/// <remarks>
/// Bu bir güvenlik sınırı: server, 7 builtin komutun yanında 594 oyun konsol komutunu
/// da stdin'den kabul ediyor (<c>campaign.add_gold_to_hero</c> dahil). Kullanıcı
/// girdisinin doğrudan stdin'e akmaması gerekiyor.
/// </remarks>
public class ServerCommandTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("players")]
    [InlineData("save")]
    [InlineData("stop")]
    [InlineData("help")]
    [InlineData("say merhaba")]
    [InlineData("kick 3")]
    public void izin_verilen_komutlar_kabul_edilir(string komut)
        => Assert.True(ServerCommand.IsAllowed(komut));

    [Theory]
    [InlineData("campaign.add_gold_to_hero 100000")]  // hile komutu
    [InlineData("campaign.kill_hero Aderimo")]
    [InlineData("coop.delete_player")]
    [InlineData("ui.toggle_ui")]
    [InlineData("mission.killAgent")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void oyun_konsol_komutlari_ve_bos_girdi_reddedilir(string? komut)
        => Assert.False(ServerCommand.IsAllowed(komut));

    [Theory]
    [InlineData("say merhaba\nkick 0")]
    [InlineData("say merhaba\r\nstop")]
    [InlineData("status\ncampaign.add_gold_to_hero 99999")]
    public void satir_sonu_iceren_komutlar_reddedilir(string komut)
    {
        // stdin satır tabanlı: gömülü bir satır sonu tek komutu ikiye böler.
        // Klasik komut enjeksiyonu vektörü.
        Assert.False(ServerCommand.IsAllowed(komut));
    }

    // --- say ------------------------------------------------------------

    [Fact]
    public void duyuru_tek_satira_indirgenir()
    {
        var komut = ServerCommand.Say("Server 30 saniye\niçinde\r\nyeniden başlatılacak");

        Assert.StartsWith("say ", komut);
        Assert.DoesNotContain('\n', komut);
        Assert.DoesNotContain('\r', komut);
        Assert.True(ServerCommand.IsAllowed(komut));
    }

    [Fact]
    public void asiri_uzun_duyuru_kirpilir()
    {
        var komut = ServerCommand.Say(new string('a', 500));

        Assert.True(komut.Length <= ServerCommand.MaxBroadcastLength + "say ".Length);
        Assert.True(ServerCommand.IsAllowed(komut));
    }

    [Fact]
    public void normal_duyuru_bozulmadan_gecer()
    {
        Assert.Equal("say Sunucu 5 dakika icinde yeniden baslatilacak",
            ServerCommand.Say("Sunucu 5 dakika icinde yeniden baslatilacak"));
    }

    // --- kick -----------------------------------------------------------

    [Fact]
    public void kick_hem_id_hem_isim_kabul_eder()
    {
        // Server'ın kendi yardım metni: "kick <id|name>" (2026-09-01'de doğrulandı)
        Assert.Equal("kick 3", ServerCommand.Kick("3"));
        Assert.Equal("kick Aderimo", ServerCommand.Kick("Aderimo"));
    }

    [Fact]
    public void kick_argumanindaki_enjeksiyon_denemesi_etkisiz_kalir()
    {
        var komut = ServerCommand.Kick("Aderimo\ncampaign.add_gold_to_hero 99999");

        Assert.DoesNotContain('\n', komut);
        Assert.StartsWith("kick ", komut);
        Assert.True(ServerCommand.IsAllowed(komut));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r")]
    public void bos_kick_hedefi_reddedilir(string hedef)
        => Assert.Throws<ArgumentException>(() => ServerCommand.Kick(hedef));

    // --- temizlik --------------------------------------------------------

    [Fact]
    public void kontrol_karakterleri_silinmez_bosluga_cevrilir()
    {
        // Silmek "ki\nck" gibi bir girdiyi geçerli bir kelimeye dönüştürebilirdi.
        Assert.Equal("ki ck", ServerCommand.SanitizeArgument("ki\nck", 64));
    }

    [Fact]
    public void ardisik_bosluklar_tek_bosluga_indirgenir()
        => Assert.Equal("a b c", ServerCommand.SanitizeArgument("  a    b\t\tc  ", 64));
}

public class ServerExitTests
{
    [Theory]
    [InlineData(0, ServerExitKind.Clean)]
    [InlineData(2, ServerExitKind.LoadFailure)]
    [InlineData(3, ServerExitKind.FatalWhileServing)]
    [InlineData(4, ServerExitKind.ModuleVerificationFailed)]
    [InlineData(-1, ServerExitKind.Unknown)]
    [InlineData(137, ServerExitKind.Unknown)]
    public void cikis_kodlari_dogru_siniflandirilir(int kod, ServerExitKind beklenen)
        => Assert.Equal(beklenen, ServerExit.Classify(kod));

    [Fact]
    public void bozuk_save_ve_modul_hatasi_otomatik_yeniden_baslatilmaz()
    {
        // En kritik crash politikası kararı: bu ikisi tekrar denemekle düzelmez,
        // yeniden başlatmak sonsuz döngü yaratır.
        Assert.False(ServerExit.IsAutoRestartable(ServerExitKind.LoadFailure));
        Assert.False(ServerExit.IsAutoRestartable(ServerExitKind.ModuleVerificationFailed));
    }

    [Fact]
    public void gercek_cokme_yeniden_baslatilir_temiz_cikis_baslatilmaz()
    {
        Assert.True(ServerExit.IsAutoRestartable(ServerExitKind.FatalWhileServing));
        Assert.True(ServerExit.IsAutoRestartable(ServerExitKind.Unknown));
        Assert.False(ServerExit.IsAutoRestartable(ServerExitKind.Clean));
    }

    [Fact]
    public void beklenen_kapanis_cokme_sayilmaz()
    {
        var beklenen = new ServerExitInfo(0, ServerExitKind.Clean, WasExpected: true);
        var cokme = new ServerExitInfo(3, ServerExitKind.FatalWhileServing, WasExpected: false);
        var bizDurdurduk = new ServerExitInfo(3, ServerExitKind.FatalWhileServing, WasExpected: true);

        Assert.False(beklenen.IsCrash);
        Assert.True(cokme.IsCrash);
        Assert.False(bizDurdurduk.IsCrash); // stop gönderdiysek crash alarmı verme
    }
}

public class ServerLaunchOptionsTests
{
    [Fact]
    public void argumanlar_dogru_sirayla_uretilir()
    {
        var opt = new ServerLaunchOptions
        {
            ExecutablePath = @"C:\x\BannerlordCoopServer.exe",
            DataDirectory = @"C:\veri\test",
        };

        Assert.Equal(new[] { "--no-tui", "--data-dir", @"C:\veri\test" }, opt.BuildArguments());
    }

    [Fact]
    public void data_dizini_verilmezse_arguman_eklenmez()
    {
        var opt = new ServerLaunchOptions { ExecutablePath = @"C:\x\s.exe" };

        Assert.Equal(new[] { "--no-tui" }, opt.BuildArguments());
    }

    [Fact]
    public void bosluk_iceren_yol_ayri_arguman_olarak_gecer()
    {
        // ArgumentList kullandığımız için elle tırnaklama yapmıyoruz;
        // yol tek bir argüman olarak kalmalı.
        var opt = new ServerLaunchOptions
        {
            ExecutablePath = @"C:\x\s.exe",
            DataDirectory = @"C:\Program Files (x86)\bir yer\veri",
        };

        var args = opt.BuildArguments();
        Assert.Equal(@"C:\Program Files (x86)\bir yer\veri", args[^1]);
    }

    [Fact]
    public void olmayan_exe_dogrulamada_yakalanir()
    {
        var opt = new ServerLaunchOptions { ExecutablePath = @"C:\yok\boyle\bir\s.exe" };

        Assert.Throws<FileNotFoundException>(opt.Validate);
    }
}
