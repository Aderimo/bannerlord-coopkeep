using System.Text;
using CoopKeep.Core.Saves;

namespace CoopKeep.Core.Tests.Saves;

/// <summary>
/// Save başlığı okuma. Biçim Bannerlord v1.4.8 save'lerinden alındı:
/// başta düz JSON, ardından sıkıştırılmış ikili veri.
/// </summary>
public sealed class SaveMetadataTests : IDisposable
{
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-meta-" + Guid.NewGuid().ToString("N"));

    public SaveMetadataTests() => Directory.CreateDirectory(_dizin);

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    private static byte[] Baslik(string modules, string extra = "") =>
        Encoding.UTF8.GetBytes(
            "~\u0001\u0000\u0000{\"List\":{\"Modules\":\"" + modules + "\",\"Module_Native\":\"v1.4.8.119303\"," +
            "\"ApplicationVersion\":\"v1.4.8.119303\",\"MainHeroLevel\":\"12\",\"MainHeroGold\":\"4500\"," +
            "\"CharacterName\":\"Ayşe \\\"Kılıç\\\" {Han}\",\"DayLong\":\"55.36988\",\"IronmanMode\":\"0\"" + extra + "}}" +
            "$\u0000ÿþ{garbage}}}");

    private string Yaz(byte[] icerik)
    {
        var yol = Path.Combine(_dizin, Guid.NewGuid().ToString("N") + ".sav");
        File.WriteAllBytes(yol, icerik.Concat(new byte[5000]).ToArray());
        return yol;
    }

    [Fact]
    public void temiz_save_okunur_ve_uyumlu_sayilir()
    {
        var meta = SaveMetadata.TryRead(Yaz(Baslik("Native;SandBoxCore;CustomBattle;Sandbox;StoryMode")));

        Assert.NotNull(meta);
        Assert.Equal(5, meta!.Modules.Count);
        Assert.Equal("Ayşe \"Kılıç\" {Han}", meta.CharacterName);
        Assert.Equal(12, meta.HeroLevel);
        Assert.Equal(4500, meta.Gold);
        Assert.Equal(55, (int)meta.Day!.Value);
        Assert.Equal("v1.4.8.119303", meta.GameVersion);
        Assert.True(meta.IsServerCompatible);
        Assert.Empty(meta.ThirdPartyModules);
    }

    [Fact]
    public void ucuncu_parti_modlu_save_uyumsuz_sayilir()
    {
        // Gerçek çöküşe yol açan save'in mod listesinden bir kesit.
        var meta = SaveMetadata.TryRead(Yaz(Baslik(
            "Bannerlord.Harmony;Native;SandBoxCore;BirthAndDeath;Sandbox;StoryMode;Bannerlord.Diplomacy;DismembermentPlus")));

        Assert.NotNull(meta);
        Assert.False(meta!.IsServerCompatible);
        Assert.Equal(["Bannerlord.Harmony", "Bannerlord.Diplomacy", "DismembermentPlus"], meta.ThirdPartyModules);
        Assert.Equal(["BirthAndDeath"], meta.UnsupportedOfficialModules);
    }

    [Fact]
    public void baslik_yoksa_null_doner()
    {
        Assert.Null(SaveMetadata.TryRead(Yaz(Encoding.UTF8.GetBytes("bu bir save degil"))));
        Assert.Null(SaveMetadata.TryRead(Path.Combine(_dizin, "yok.sav")));
    }

    [Fact]
    public void yarim_baslik_null_doner()
    {
        var yarim = Encoding.UTF8.GetBytes("{\"List\":{\"Modules\":\"Native;San");
        Assert.Null(SaveMetadata.TryParse(yarim));
    }
}
