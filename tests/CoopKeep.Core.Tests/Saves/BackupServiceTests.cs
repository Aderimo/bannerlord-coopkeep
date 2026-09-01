using System.IO.Compression;
using CoopKeep.Core.Saves;

namespace CoopKeep.Core.Tests.Saves;

/// <summary>
/// Yedekleme, saklama kuralları ve geri yükleme testleri.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _dizin;
    private readonly BackupService _yedek;

    public BackupServiceTests()
    {
        _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dizin);
        _yedek = new BackupService(_dizin);
    }

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    private void DunyaYaz(string ad, string savIcerik = "dunya", string jsonIcerik = "{\"Players\":[]}")
    {
        File.WriteAllText(Path.Combine(_dizin, ad + ".sav"), savIcerik);
        File.WriteAllText(Path.Combine(_dizin, ad + ".json"), jsonIcerik);
    }

    // --- yedek alma ------------------------------------------------------

    [Fact]
    public void yedek_her_iki_dosyayi_da_icerir()
    {
        DunyaYaz("Calradia");

        var kayit = _yedek.Create("Calradia");

        Assert.True(File.Exists(kayit.FilePath));

        using var zip = ZipFile.OpenRead(kayit.FilePath);
        Assert.NotNull(zip.GetEntry("Calradia.sav"));
        Assert.NotNull(zip.GetEntry("Calradia.json"));
    }

    [Fact]
    public void es_json_eksikse_yedek_alinmaz()
    {
        // Yarım bir yedek geri yüklendiğinde oyuncu eşlemeleri kaybolur;
        // en baştan almamak doğrusu.
        File.WriteAllText(Path.Combine(_dizin, "Yarim.sav"), "dunya");

        var hata = Assert.Throws<InvalidOperationException>(() => _yedek.Create("Yarim"));
        Assert.Contains("eş JSON", hata.Message);
    }

    [Fact]
    public void olmayan_dunyanin_yedegi_alinamaz()
        => Assert.Throws<FileNotFoundException>(() => _yedek.Create("YokBoyleBirSey"));

    [Fact]
    public void ayni_saniyede_alinan_ikinci_yedek_ustune_yazmaz()
    {
        DunyaYaz("Calradia");

        var bir = _yedek.Create("Calradia");
        var iki = _yedek.Create("Calradia");

        Assert.NotEqual(bir.FilePath, iki.FilePath);
        Assert.Equal(2, _yedek.List("Calradia").Count);
    }

    [Fact]
    public void yedekler_yeniden_eskiye_siralanir()
    {
        DunyaYaz("Calradia");

        _yedek.Create("Calradia");
        Thread.Sleep(1100); // dosya zaman damgası saniye çözünürlüklü olabilir
        var yeni = _yedek.Create("Calradia");

        Assert.Equal(yeni.FilePath, _yedek.List("Calradia")[0].FilePath);
    }

    [Fact]
    public void yedek_klasoru_save_listesine_karismaz()
    {
        DunyaYaz("Calradia");
        _yedek.Create("Calradia");

        // Yedek klasörü alt çizgiyle başlıyor ve zip içeriyor; .sav taraması onu görmemeli.
        var depo = new SaveRepository(_dizin);
        Assert.Single(depo.DiscoverCampaigns());
    }

    // --- saklama kuralları -----------------------------------------------

    [Fact]
    public void sayi_sinirini_asan_eski_yedekler_silinir()
    {
        var yedek = new BackupService(_dizin, new BackupPolicy(MaxCount: 3, MaxAgeDays: 0));
        DunyaYaz("Calradia");

        for (var i = 0; i < 5; i++) yedek.Create("Calradia");

        Assert.Equal(3, yedek.List("Calradia").Count);
    }

    [Fact]
    public void yas_sinirini_asan_yedekler_silinir()
    {
        var yedek = new BackupService(_dizin, new BackupPolicy(MaxCount: 0, MaxAgeDays: 7));
        DunyaYaz("Calradia");

        var eski = yedek.Create("Calradia");
        var yeni = yedek.Create("Calradia");

        // Bir yedeği yapay olarak eskit
        File.SetLastWriteTimeUtc(eski.FilePath, DateTime.UtcNow.AddDays(-30));

        Assert.Equal(1, yedek.Prune("Calradia"));

        var kalan = Assert.Single(yedek.List("Calradia"));
        Assert.Equal(yeni.FilePath, kalan.FilePath);
    }

    [Fact]
    public void sayi_ve_yas_kurallari_birlikte_uygulanir()
    {
        // Yalnızca sayıya bakmak, yoğun bir oturumun bir haftalık geçmişi silmesine
        // yol açar; yalnızca yaşa bakmak diski büyütür. İkisi birden gerekli.
        var yedek = new BackupService(_dizin, new BackupPolicy(MaxCount: 5, MaxAgeDays: 7));
        DunyaYaz("Calradia");

        for (var i = 0; i < 4; i++) yedek.Create("Calradia");

        foreach (var b in yedek.List("Calradia").Take(2))
            File.SetLastWriteTimeUtc(b.FilePath, DateTime.UtcNow.AddDays(-10));

        yedek.Prune("Calradia");

        Assert.Equal(2, yedek.List("Calradia").Count); // sayı sınırı aşılmadı, yaş sınırı 2'yi aldı
    }

    // --- geri yükleme ----------------------------------------------------

    [Fact]
    public void geri_yukleme_dunyayi_eski_haline_dondurur()
    {
        DunyaYaz("Calradia", "ILK_HAL", "{\"Players\":[1]}");
        var kayit = _yedek.Create("Calradia");

        DunyaYaz("Calradia", "BOZULMUS", "{\"Players\":[]}");
        Assert.Equal("BOZULMUS", File.ReadAllText(Path.Combine(_dizin, "Calradia.sav")));

        _yedek.Restore(kayit);

        Assert.Equal("ILK_HAL", File.ReadAllText(Path.Combine(_dizin, "Calradia.sav")));
        Assert.Equal("{\"Players\":[1]}", File.ReadAllText(Path.Combine(_dizin, "Calradia.json")));
    }

    [Fact]
    public void geri_yuklemeden_once_mevcut_halin_yedegi_alinir()
    {
        // Yanlış yedeği seçen kullanıcının kurtuluş yolu.
        DunyaYaz("Calradia", "ILK");
        var kayit = _yedek.Create("Calradia");

        DunyaYaz("Calradia", "SONRAKI");
        _yedek.Restore(kayit);

        // Geri yükleme sırasında "SONRAKI" hâli de yedeklenmiş olmalı
        Assert.Equal(2, _yedek.List("Calradia").Count);
    }

    [Fact]
    public void eksik_icerikli_yedek_reddedilir_ve_dunya_bozulmaz()
    {
        DunyaYaz("Calradia", "SAGLAM_HAL");

        // Yalnızca .sav içeren sahte bir yedek üret
        var klasor = _yedek.FolderFor("Calradia");
        Directory.CreateDirectory(klasor);
        var bozuk = Path.Combine(klasor, "Calradia_bozuk.zip");
        using (var zip = ZipFile.Open(bozuk, ZipArchiveMode.Create))
            zip.CreateEntry("Calradia.sav");

        var kayit = new BackupEntry(bozuk, "Calradia", DateTime.UtcNow, 0);

        Assert.Throws<InvalidDataException>(() => _yedek.Restore(kayit));

        // En önemlisi: mevcut dünya bozulmadı
        Assert.Equal("SAGLAM_HAL", File.ReadAllText(Path.Combine(_dizin, "Calradia.sav")));
    }

    [Fact]
    public void olmayan_yedek_dosyasi_geri_yuklenemez()
    {
        var kayit = new BackupEntry(Path.Combine(_dizin, "yok.zip"), "Calradia", DateTime.UtcNow, 0);
        Assert.Throws<FileNotFoundException>(() => _yedek.Restore(kayit));
    }

    [Theory]
    [InlineData("../disari")]
    [InlineData("alt/klasor")]
    [InlineData("")]
    public void tehlikeli_adlar_yedek_klasorunde_de_reddedilir(string ad)
        => Assert.Throws<ArgumentException>(() => _yedek.FolderFor(ad));

    [Fact]
    public void hic_yedegi_olmayan_dunya_bos_liste_dondurur()
        => Assert.Empty(_yedek.List("HicYedeklenmemis"));
}
