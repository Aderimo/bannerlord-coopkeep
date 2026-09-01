using CoopKeep.Core.Saves;

namespace CoopKeep.Core.Tests.Saves;

/// <summary>
/// Save keşfi ve dosya işlemleri testleri.
/// </summary>
/// <remarks>
/// Eş JSON fixture'ları gerçek şemadan alındı (kullanıcının <c>saveauto1.json</c>
/// dosyasındaki PascalCase alanlar ve gerçek SteamID64 biçimi).
/// </remarks>
public sealed class SaveRepositoryTests : IDisposable
{
    private readonly string _dizin;
    private readonly SaveRepository _depo;

    public SaveRepositoryTests()
    {
        _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-saves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dizin);
        _depo = new SaveRepository(_dizin);
    }

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    private void CiftYaz(string ad, string? json = null)
    {
        File.WriteAllText(Path.Combine(_dizin, ad + ".sav"), "sahte kaydedilmis dunya verisi");
        if (json is not null)
            File.WriteAllText(Path.Combine(_dizin, ad + ".json"), json);
    }

    private const string IkiOyunculuJson = """
        {
          "UniqueGameId": "saveauto1",
          "Players": [
            {
              "ControllerId": "76561199571622150",
              "HeroId": "Hero_Player",
              "MobilePartyId": "MobileParty_Player",
              "ClanId": "Clan_Player",
              "CharacterObjectId": "CharacterObject_Player"
            },
            {
              "ControllerId": "76561199007459190",
              "HeroId": "Hero_Player2863",
              "MobilePartyId": "MobileParty_Player438",
              "ClanId": "Clan_Player12",
              "CharacterObjectId": "CharacterObject_Player2863"
            }
          ],
          "CraftingPlayerData": { "PlayerOpenedPartsDictionary": {} },
          "HeroMeetingData": {}
        }
        """;

    private const string BosOyuncuJson = """{ "UniqueGameId": "faz0test", "Players": [] }""";

    // --- keşif -----------------------------------------------------------

    [Fact]
    public void save_ve_es_json_cifti_birlikte_kesfedilir()
    {
        CiftYaz("saveauto1", IkiOyunculuJson);

        var save = Assert.Single(_depo.Discover());

        Assert.Equal("saveauto1", save.Name);
        Assert.True(save.IsComplete);
        Assert.Equal(2, save.PlayerCount);
    }

    [Fact]
    public void es_json_yoksa_save_eksik_isaretlenir()
    {
        CiftYaz("yalniz");  // json yok

        var save = Assert.Single(_depo.Discover());

        Assert.False(save.IsComplete);
        Assert.Empty(save.Players);
    }

    [Fact]
    public void oyuncularin_SteamID64_kimlikleri_okunur()
    {
        CiftYaz("saveauto1", IkiOyunculuJson);

        var oyuncular = _depo.Find("saveauto1")!.Players;

        Assert.Equal("76561199571622150", oyuncular[0].ControllerId);
        Assert.True(oyuncular[0].HasSteamId);
        Assert.Equal("Hero_Player", oyuncular[0].HeroId);

        Assert.Equal("76561199007459190", oyuncular[1].ControllerId);
        Assert.True(oyuncular[1].HasSteamId);
    }

    [Fact]
    public void TransferSave_bir_kampanya_degildir_listelenmez()
    {
        // Katılan istemcilere gönderilen anlık görüntü; kampanya gibi gösterilmemeli.
        CiftYaz("TransferSave", BosOyuncuJson);
        CiftYaz("gercek", BosOyuncuJson);

        var adlar = _depo.Discover().Select(s => s.Name).ToList();

        Assert.Contains("gercek", adlar);
        Assert.DoesNotContain("TransferSave", adlar);
    }

    [Fact]
    public void coop_kendi_yedekleri_isaretlenir_ve_kampanya_listesinden_cikarilir()
    {
        CiftYaz("saveauto1", BosOyuncuJson);
        CiftYaz("saveauto1.backup1", BosOyuncuJson);
        CiftYaz("saveauto1.backup2", BosOyuncuJson);
        CiftYaz("default_new_game");

        Assert.Equal(4, _depo.Discover().Count);

        var kampanyalar = _depo.DiscoverCampaigns();
        var tekKampanya = Assert.Single(kampanyalar);
        Assert.Equal("saveauto1", tekKampanya.Name);

        Assert.True(_depo.Find("saveauto1.backup1")!.IsCoopBackup);
        Assert.True(_depo.Find("default_new_game")!.IsTemplate);
    }

    [Fact]
    public void bozuk_es_json_tum_listeyi_cokertmez()
    {
        CiftYaz("bozuk", "{ bu gecerli json degil ]]]");
        CiftYaz("saglam", IkiOyunculuJson);

        var hepsi = _depo.Discover();

        Assert.Equal(2, hepsi.Count);
        Assert.Empty(_depo.Find("bozuk")!.Players);       // sessizce boş
        Assert.Equal(2, _depo.Find("saglam")!.PlayerCount); // diğeri etkilenmedi
    }

    // --- dosya işlemleri: çift her zaman birlikte ------------------------

    [Fact]
    public void kopyalamak_her_iki_dosyayi_da_kopyalar()
    {
        CiftYaz("kaynak", IkiOyunculuJson);

        var yeni = _depo.Duplicate("kaynak", "kopya");

        Assert.True(File.Exists(Path.Combine(_dizin, "kopya.sav")));
        Assert.True(File.Exists(Path.Combine(_dizin, "kopya.json")));
        Assert.Equal(2, yeni.PlayerCount);
        Assert.True(File.Exists(Path.Combine(_dizin, "kaynak.sav"))); // kaynak duruyor
    }

    [Fact]
    public void yeniden_adlandirmak_her_iki_dosyayi_da_tasir()
    {
        CiftYaz("eski", IkiOyunculuJson);

        _depo.Rename("eski", "yeni");

        Assert.False(File.Exists(Path.Combine(_dizin, "eski.sav")));
        Assert.False(File.Exists(Path.Combine(_dizin, "eski.json")));
        Assert.True(File.Exists(Path.Combine(_dizin, "yeni.sav")));
        Assert.True(File.Exists(Path.Combine(_dizin, "yeni.json")));
    }

    [Fact]
    public void silmek_her_iki_dosyayi_da_siler()
    {
        CiftYaz("gidecek", IkiOyunculuJson);

        _depo.Delete("gidecek");

        Assert.False(File.Exists(Path.Combine(_dizin, "gidecek.sav")));
        Assert.False(File.Exists(Path.Combine(_dizin, "gidecek.json")));
        Assert.Empty(_depo.Discover());
    }

    [Fact]
    public void var_olan_adin_uzerine_kopyalanmaz()
    {
        CiftYaz("a", BosOyuncuJson);
        CiftYaz("b", BosOyuncuJson);

        Assert.Throws<InvalidOperationException>(() => _depo.Duplicate("a", "b"));
        Assert.Throws<InvalidOperationException>(() => _depo.Rename("a", "b"));
    }

    // --- güvenlik: path traversal ---------------------------------------

    [Theory]
    [InlineData("../disari")]
    [InlineData("..\\disari")]
    [InlineData("alt/klasor")]
    [InlineData("C:\\mutlak")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".gizli")]
    [InlineData("CON")]
    [InlineData("nul")]
    public void tehlikeli_save_adlari_reddedilir(string ad)
    {
        Assert.False(SaveSet.IsValidSaveName(ad));
        Assert.Throws<ArgumentException>(() => _depo.ResolveSavePath(ad));
    }

    [Theory]
    [InlineData("saveauto1")]
    [InlineData("saveauto1.backup1")]
    [InlineData("Benim Kampanyam")]
    [InlineData("yeni_dunya-2")]
    public void gecerli_save_adlari_kabul_edilir(string ad)
    {
        Assert.True(SaveSet.IsValidSaveName(ad));

        var yol = _depo.ResolveSavePath(ad);
        Assert.StartsWith(_dizin, yol);
        Assert.EndsWith(".sav", yol);
    }

    // --- SteamID64 doğrulama --------------------------------------------

    [Theory]
    [InlineData("76561199571622150", true)]
    [InlineData("76561199007459190", true)]
    [InlineData("1234567890123456", false)]   // 16 hane
    [InlineData("765611995716221501", false)] // 18 hane
    [InlineData("7656119957162215a", false)]  // rakam değil
    [InlineData("12345678901234567", false)]  // yanlış önek
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SteamID64_bicimi_dogrulanir(string? deger, bool beklenen)
        => Assert.Equal(beklenen, CoopSessionPlayer.IsSteamId64(deger));

    [Fact]
    public void olmayan_dizin_bos_liste_dondurur()
    {
        var depo = new SaveRepository(Path.Combine(_dizin, "hic-olmayan"));
        Assert.Empty(depo.Discover());
    }
}
