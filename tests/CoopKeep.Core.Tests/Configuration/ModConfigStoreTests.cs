using CoopKeep.Core.Configuration;

namespace CoopKeep.Core.Tests.Configuration;

/// <summary>
/// <c>mod-config.json</c> — dünya ayarları — okuma/yazma testleri.
/// </summary>
/// <remarks>
/// Fikstür, Coop v0.1.6 ile gelen <c>mod-config.default.json</c> ile aynı yapıda ve aynı
/// varsayılanlarda, CoopKeep için yazılmış bir dosya: yorumlu ve iç içe. Yazma işleminin yorumları ve bilinmeyen anahtarları koruduğunu
/// bu yapı üzerinde doğruluyoruz.
/// </remarks>
public sealed class ModConfigStoreTests : IDisposable
{
    private static readonly string Sablon = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mod-config.default.json");

    private readonly string _dizin;
    private readonly ModConfigStore _depo;

    public ModConfigStoreTests()
    {
        _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-mod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dizin);
        _depo = new ModConfigStore(Path.Combine(_dizin, "mod-config.json"));
    }

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    [Fact]
    public void dosya_yoksa_sablondan_olusturulur()
    {
        _depo.EnsureExists(Sablon);

        Assert.True(_depo.Exists);
        Assert.Equal(File.ReadAllText(Sablon), File.ReadAllText(_depo.Path));
    }

    [Fact]
    public void sablonun_degerleri_modun_varsayilanlariyla_ayni()
    {
        _depo.EnsureExists(Sablon);

        var okunan = _depo.Read();

        foreach (var d in ModSettingsCatalog.All)
            Assert.True(Equals(d.Default, okunan[d.Id]), $"{d.Id}: beklenen {d.Default}, okunan {okunan[d.Id]}");
    }

    [Fact]
    public void zorluk_ve_secenekler_yazilir_ve_geri_okunur()
    {
        _depo.EnsureExists(Sablon);

        var degerler = _depo.Read();
        ModSettingsCatalog.ApplyPreset(degerler, "Realistic");
        degerler["modOptions.battleSize"] = 600;
        degerler["modOptions.clientsCanUseCheats"] = true;
        degerler["modOptions.maximumLootersMultiplier"] = 1.5;
        degerler["modOptions.goldFoodInfluenceChangeInBattles"] = "OneDayMax";

        _depo.Write(degerler);
        var okunan = _depo.Read();

        Assert.Equal("Realistic", okunan["difficulty.playerReceivedDamage"]);
        Assert.Equal("Realistic", okunan["difficulty.battleDeath"]);
        Assert.Equal(600, okunan["modOptions.battleSize"]);
        Assert.Equal(true, okunan["modOptions.clientsCanUseCheats"]);
        Assert.Equal(1.5, okunan["modOptions.maximumLootersMultiplier"]);
        Assert.Equal("OneDayMax", okunan["modOptions.goldFoodInfluenceChangeInBattles"]);
        Assert.Equal("Realistic", ModSettingsCatalog.DetectPreset(okunan));
    }

    [Fact]
    public void yazma_yorumlari_ve_tanimadigimiz_anahtarlari_korur()
    {
        _depo.EnsureExists(Sablon);

        var degerler = _depo.Read();
        degerler["modOptions.battleSize"] = 500;
        _depo.Write(degerler);

        var metin = File.ReadAllText(_depo.Path);
        Assert.Contains("// Player damage.", metin);
        Assert.Contains("\"mapFullVolumeDistance\": 5", metin);
        Assert.Contains("\"smithingStaminaRecoveryMultiplier\": 0.1", metin);
        Assert.Contains("\"battleSize\": 500", metin);

        // Değişmeyen satırlar birebir aynı kalmalı.
        var once = File.ReadAllLines(Sablon);
        var sonra = File.ReadAllLines(_depo.Path);
        Assert.Equal(once.Length, sonra.Length);
        Assert.Single(once.Zip(sonra).Where(p => p.First != p.Second));
    }

    [Fact]
    public void ayni_anahtar_baska_bolumde_olsa_bile_dogru_bolume_yazilir()
    {
        File.WriteAllText(_depo.Path, """
            {
              "voice": { "battleSize": 1 },
              "modOptions": {
                "battleSize": 1000
              }
            }
            """);

        _depo.Write(new Dictionary<string, object> { ["modOptions.battleSize"] = 300 });

        var metin = File.ReadAllText(_depo.Path);
        Assert.Contains("\"voice\": { \"battleSize\": 1 }", metin);
        Assert.Contains("\"battleSize\": 300", metin);
    }

    [Fact]
    public void bolumde_olmayan_anahtar_bolumun_icine_eklenir()
    {
        File.WriteAllText(_depo.Path, """
            {
              "difficulty": {
                "playerReceivedDamage": "Easy"
              },
              "modOptions": {
                "battleSize": 1000
              }
            }
            """);

        _depo.Write(new Dictionary<string, object> { ["modOptions.clientsCanUseCheats"] = true });

        var okunan = _depo.Read();
        Assert.Equal(true, okunan["modOptions.clientsCanUseCheats"]);
        Assert.Equal("Easy", okunan["difficulty.playerReceivedDamage"]);

        var editor = JsoncEditor.Load(_depo.Path);
        Assert.Equal("true", editor.GetRawValue("modOptions", "clientsCanUseCheats"));
        Assert.Null(editor.GetRawValue("difficulty", "clientsCanUseCheats"));
    }

    [Fact]
    public void gecersiz_degerler_sinirlanir()
    {
        _depo.EnsureExists(Sablon);

        _depo.Write(new Dictionary<string, object>
        {
            ["modOptions.battleSize"] = 50_000,
            ["difficulty.battleDeath"] = "Imkansiz",
        });

        var okunan = _depo.Read();
        Assert.Equal(1000, okunan["modOptions.battleSize"]);
        Assert.Equal("VeryEasy", okunan["difficulty.battleDeath"]);
    }

    [Fact]
    public void dunya_profili_kaydedilir_kopyalanir_ve_tasinir()
    {
        var profiller = new WorldProfileStore(Path.Combine(_dizin, "worlds"));
        var degerler = ModSettingsCatalog.Defaults();
        ModSettingsCatalog.ApplyPreset(degerler, "Easy");

        profiller.Save("Calradia", degerler);
        profiller.Duplicate("Calradia", "Kopya");
        profiller.Rename("Calradia", "Yeni");

        Assert.False(profiller.Exists("Calradia"));
        Assert.Equal("Easy", profiller.Load("Yeni")!["difficulty.combatAIDifficulty"]);
        Assert.Equal("Easy", profiller.Load("Kopya")!["difficulty.combatAIDifficulty"]);

        profiller.Delete("Kopya");
        Assert.Null(profiller.Load("Kopya"));
    }
}
