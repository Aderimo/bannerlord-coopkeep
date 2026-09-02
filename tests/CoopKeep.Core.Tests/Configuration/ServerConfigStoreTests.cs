using CoopKeep.Core.Configuration;

namespace CoopKeep.Core.Tests.Configuration;

/// <summary>
/// <c>server-config.json</c> okuma/yazma testleri.
/// </summary>
/// <remarks>
/// Şifrenin sunucuya hiç ulaşmadığı bir hata bildirimi üzerine yazıldı: kullanıcı
/// şifre koydu ama sunucu <c>pass : none</c> ile açıldı. Bu testler yazma yolunun
/// gerçekten çalıştığını sabitler.
/// </remarks>
public sealed class ServerConfigStoreTests : IDisposable
{
    private readonly string _dizin;
    private readonly string _yol;
    private readonly ServerConfigStore _depo;

    public ServerConfigStoreTests()
    {
        _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dizin);
        _yol = Path.Combine(_dizin, "server-config.json");
        _depo = new ServerConfigStore(_yol);
    }

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    // --- yeni dosya oluşturma --------------------------------------------

    [Fact]
    public void yapilandirma_yoksa_olusturulur_ve_okunabilir()
    {
        Assert.False(_depo.Exists);

        _depo.EnsureSaveName("Calradia");

        Assert.True(_depo.Exists);
        var cfg = _depo.Read();
        Assert.Equal("Calradia", cfg.SaveName);
        Assert.Equal(ServerConfig.DefaultPort, cfg.Port);
        Assert.True(cfg.HasExplicitPort);
        Assert.False(cfg.HasPassword);
    }

    // --- şifre yazma: bildirilen hatanın testi ---------------------------

    [Fact]
    public void sifre_yazilir_ve_geri_okunur()
    {
        _depo.EnsureSaveName("Calradia");

        _depo.Update(w => w.Password("gizli123"));

        var cfg = _depo.Read();
        Assert.Equal("gizli123", cfg.Password);
        Assert.True(cfg.HasPassword);

        // Diskteki dosyada da gerçekten olmalı — sunucu bu dosyayı okuyor.
        Assert.Contains("gizli123", File.ReadAllText(_yol));
    }

    [Fact]
    public void sifre_yazdiktan_sonra_save_degistirmek_sifreyi_silmez()
    {
        // "Başlat" akışı save adını yeniden yazıyor; şifreyi ezmemeli.
        _depo.EnsureSaveName("Calradia");
        _depo.Update(w => w.Password("gizli123"));

        _depo.EnsureSaveName("BaskaDunya");

        var cfg = _depo.Read();
        Assert.Equal("BaskaDunya", cfg.SaveName);
        Assert.Equal("gizli123", cfg.Password);
    }

    [Fact]
    public void turkce_karakterli_sifre_dogru_saklanir()
    {
        _depo.EnsureSaveName("Calradia");

        _depo.Update(w => w.Password("şifreÇĞİÖÜ"));

        Assert.Equal("şifreÇĞİÖÜ", _depo.Read().Password);
    }

    [Fact]
    public void turkce_karakterli_sunucu_adi_dogru_saklanir()
    {
        _depo.EnsureSaveName("CENGİZLE OYNADIĞIMIZ DÜNYA");

        Assert.Equal("CENGİZLE OYNADIĞIMIZ DÜNYA", _depo.Read().SaveName);
    }

    [Fact]
    public void sifre_temizlenebilir()
    {
        _depo.EnsureSaveName("Calradia");
        _depo.Update(w => w.Password("gizli"));

        _depo.Update(w => w.Password(""));

        Assert.False(_depo.Read().HasPassword);
    }

    [Fact]
    public void cok_uzun_sifre_reddedilir()
    {
        _depo.EnsureSaveName("Calradia");

        Assert.Throws<ArgumentException>(
            () => _depo.Update(w => w.Password(new string('a', 129))));
    }

    // --- diğer alanlar ----------------------------------------------------

    [Fact]
    public void tum_ayarlar_tek_islemde_yazilabilir()
    {
        _depo.EnsureSaveName("Calradia");

        _depo.Update(w => w
            .Password("abc")
            .Port(4300)
            .AutosaveMinutes(15)
            .Steam(false));

        var cfg = _depo.Read();
        Assert.Equal("abc", cfg.Password);
        Assert.Equal(4300, cfg.Port);
        Assert.Equal(15, cfg.AutosaveMinutes);
        Assert.False(cfg.Steam);
        Assert.Equal("Calradia", cfg.SaveName); // dokunulmadı
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void gecersiz_port_reddedilir(int port)
    {
        _depo.EnsureSaveName("Calradia");
        Assert.Throws<ArgumentOutOfRangeException>(() => _depo.Update(w => w.Port(port)));
    }

    [Fact]
    public void otomatik_kayit_sifir_olabilir_negatif_olamaz()
    {
        _depo.EnsureSaveName("Calradia");

        _depo.Update(w => w.AutosaveMinutes(0));
        Assert.False(_depo.Read().AutosaveEnabled);

        Assert.Throws<ArgumentOutOfRangeException>(() => _depo.Update(w => w.AutosaveMinutes(-5)));
    }

    [Fact]
    public void elle_yazilmis_yorumlu_dosyadaki_ayarlar_korunur()
    {
        // Kullanıcı dosyayı elle düzenlemiş olabilir; yorumları ve bilinmeyen
        // anahtarları koruduğumuzu burada da sabitliyoruz.
        File.WriteAllText(_yol, """
            {
              // kendi notum
              "saveName": "Elle",
              "password": "elleSifre",
              "gelecektekiAyar": 42,
            }
            """);

        _depo.Update(w => w.Port(4250));

        var metin = File.ReadAllText(_yol);
        Assert.Contains("// kendi notum", metin);
        Assert.Contains("gelecektekiAyar", metin);
        Assert.Equal("elleSifre", _depo.Read().Password);
        Assert.Equal(4250, _depo.Read().Port);
    }
}
