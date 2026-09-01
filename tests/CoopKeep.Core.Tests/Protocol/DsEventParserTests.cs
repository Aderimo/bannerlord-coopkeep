using CoopKeep.Core.Protocol;

namespace CoopKeep.Core.Tests.Protocol;

/// <summary>
/// Ayrıştırıcı testleri.
/// </summary>
/// <remarks>
/// Buradaki satırların TAMAMI gerçek dedicated server çıktısından alındı
/// (2026-09-01 Faz 0 doğrulama koşuları ve kullanıcının 30.08 tarihli oturum logu).
/// Uydurma fixture kullanılmıyor — protokol kapalı kaynak bir binary'den geliyor,
/// dolayısıyla tek güvenilir referans gerçek çıktı.
/// </remarks>
public class DsEventParserTests
{
    // --- state olayı -----------------------------------------------------

    [Theory]
    [InlineData("boot", ServerPhase.Boot)]
    [InlineData("loading", ServerPhase.Loading)]
    [InlineData("serving", ServerPhase.Serving)]
    [InlineData("stopping", ServerPhase.Stopping)]
    [InlineData("shutdown", ServerPhase.Shutdown)]
    [InlineData("starting", ServerPhase.Starting)]
    public void state_olayindaki_faz_dogru_cozulur(string ham, ServerPhase beklenen)
    {
        var satir = $$"""@DS@{"ev":"state","phase":"{{ham}}","save":"faz0test","pw":false}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var state = Assert.IsType<DsEvent.State>(olay);
        Assert.Equal(beklenen, state.Phase);
        Assert.Equal(ham, state.RawPhase);
        Assert.Equal("faz0test", state.SaveName);
        Assert.False(state.PasswordRequired);
    }

    [Fact]
    public void sifre_gerektiren_server_pw_true_bildirir()
    {
        var satir = """@DS@{"ev":"state","phase":"serving","save":"saveauto1","pw":true}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var state = Assert.IsType<DsEvent.State>(olay);
        Assert.True(state.PasswordRequired);
        Assert.Equal("saveauto1", state.SaveName);
    }

    [Fact]
    public void taninmayan_faz_cokertmez_Unknown_dondurur()
    {
        // Upstream ileride yeni bir faz ekleyebilir; bu bizi kırmamalı.
        var satir = """@DS@{"ev":"state","phase":"hibernating","save":"x","pw":false}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var state = Assert.IsType<DsEvent.State>(olay);
        Assert.Equal(ServerPhase.Unknown, state.Phase);
        Assert.Equal("hibernating", state.RawPhase); // ham değer korunur ki loglayabilelim
    }

    // --- players olayı ---------------------------------------------------

    [Fact]
    public void bos_oyuncu_listesi_ayristirilir()
    {
        Assert.True(DsEventParser.TryParse("""@DS@{"ev":"players","list":[]}""", out var olay));

        var players = Assert.IsType<DsEvent.Players>(olay);
        Assert.Empty(players.List);
    }

    [Fact]
    public void gercek_oturumdaki_iki_oyuncu_dogru_okunur()
    {
        // Kullanıcının 30.08 tarihli gerçek oturumundan.
        var satir = """@DS@{"ev":"players","list":[{"id":0,"name":"Aderimo","state":"on map","addr":"127.0.0.1:52010"},{"id":1,"name":"CengizHan","state":"loading","addr":"127.0.0.1:56016"}]}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var players = Assert.IsType<DsEvent.Players>(olay);
        Assert.Equal(2, players.List.Count);

        Assert.Equal(0, players.List[0].Id);
        Assert.Equal("Aderimo", players.List[0].Name);
        Assert.True(players.List[0].IsOnMap);

        Assert.Equal("CengizHan", players.List[1].Name);
        Assert.Equal("loading", players.List[1].State);
        Assert.False(players.List[1].IsOnMap);
    }

    [Fact]
    public void eksik_alanlar_guvenli_varsayilanlara_duser()
    {
        var satir = """@DS@{"ev":"players","list":[{"id":3}]}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var oyuncu = Assert.Single(Assert.IsType<DsEvent.Players>(olay).List);
        Assert.Equal(3, oyuncu.Id);
        Assert.Equal(ConnectedPlayer.JoiningPlaceholder, oyuncu.Name);
        Assert.Equal("unknown", oyuncu.State);
        Assert.Null(oyuncu.Address);
    }

    // --- commands olayı --------------------------------------------------

    [Fact]
    public void builtin_komut_seti_okunur()
    {
        // Gerçek server'ın açılışta ilan ettiği set (game listesi kısaltıldı).
        var satir = """@DS@{"ev":"commands","builtin":["status","players","save","say","kick","stop","help"],"game":["ai.formation_speed_adjustment_enabled","coop.delete_player"]}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var cmds = Assert.IsType<DsEvent.Commands>(olay);
        Assert.Equal(7, cmds.Builtin.Count);
        Assert.Contains("kick", cmds.Builtin);
        Assert.Contains("stop", cmds.Builtin);
        Assert.Equal(2, cmds.Game.Count);
    }

    // --- satır biçimi dayanıklılığı --------------------------------------

    [Fact]
    public void log_dosyasindaki_zaman_damgali_bicim_de_ayristirilir()
    {
        // stdout ham gelir, log dosyasında ise başında zaman damgası olur.
        // Ayrıştırıcı ikisini de kabul etmeli.
        var satir = """21:24:25.353  @DS@{"ev":"state","phase":"serving","save":"faz0test","pw":false}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));
        Assert.Equal(ServerPhase.Serving, Assert.IsType<DsEvent.State>(olay).Phase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[DedicatedServer] SERVING - coop server up, waiting for clients")]
    [InlineData("21:24:49.097  [DedicatedServer] pulse: time=Summer 1, 1084 players=0")]
    [InlineData("ERROR: 0Harmony.dll: Could not load file or assembly")]
    public void olay_olmayan_satirlar_sessizce_reddedilir(string? satir)
    {
        Assert.False(DsEventParser.TryParse(satir, out var olay));
        Assert.Null(olay);
    }

    [Theory]
    [InlineData("@DS@")]                                   // gövde yok
    [InlineData("@DS@{")]                                  // yarım JSON (satır sınırında kesilmiş)
    [InlineData("""@DS@{"ev":"players","list":[{"id":0,""")] // yarım dizi
    [InlineData("@DS@not-json")]
    [InlineData("""@DS@{"phase":"serving"}""")]            // ev alanı yok
    [InlineData("""@DS@["ev","state"]""")]                 // nesne değil
    public void bozuk_veya_yarim_json_cokertmez(string satir)
    {
        // En kritik dayanıklılık kuralı: canlı stdout'tan yarım satır gelebilir.
        var exception = Record.Exception(() => DsEventParser.TryParse(satir, out _));

        Assert.Null(exception);
        Assert.False(DsEventParser.TryParse(satir, out _));
    }

    [Fact]
    public void taninmayan_olay_tipi_Unknown_olarak_saklanir()
    {
        var satir = """@DS@{"ev":"metrics","cpu":42}""";

        Assert.True(DsEventParser.TryParse(satir, out var olay));

        var unknown = Assert.IsType<DsEvent.Unknown>(olay);
        Assert.Equal("metrics", unknown.EventName);
        Assert.Contains("cpu", unknown.RawJson);
    }
}
