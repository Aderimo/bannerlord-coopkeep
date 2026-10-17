using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Saves;

namespace CoopKeep.Core.Tests.Installation;

/// <summary>Topluluk mod listesi, konsol açıklamaları ve yedek listesi.</summary>
public sealed class ModCompatDatabaseTests : IDisposable
{
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-compat-" + Guid.NewGuid().ToString("N"));

    public ModCompatDatabaseTests() => Directory.CreateDirectory(_dizin);

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    [Fact]
    public void liste_okunur_ve_kimlik_esnek_eslesir()
    {
        var db = ModCompatDatabase.Parse("""
            {
              // yorumlara izin var
              "mods": [
                { "id": "Bannerlord.Harmony", "status": "Works", "coop": "v0.1.6" },
                { "id": "DismembermentPlus", "status": "Broken", "note": "savaşta çöküyor" },
              ]
            }
            """)!;

        Assert.Equal(2, db.Count);
        Assert.Equal(CommunityStatus.Works, db.Find("bannerlord harmony")!.Status);
        Assert.Equal(CommunityStatus.Broken, db.Find("Dismemberment_Plus")!.Status);
        Assert.Null(db.Find("Yok"));
    }

    [Fact]
    public void bozuk_liste_null_ve_onbellek_yoksa_bos()
    {
        Assert.Null(ModCompatDatabase.Parse("{ bozuk"));
        Assert.Equal(0, ModCompatDatabase.LoadCached(Path.Combine(_dizin, "yok.json")).Count);
    }

    [Theory]
    [InlineData("[06:31:05.964] Messagebox [ERROR] message: Cannot load: 0Harmony.dll", "dllNoise", false)]
    [InlineData("[DedicatedServer] EULA acceptance required; server will not start (exit 6).", "eula", true)]
    [InlineData("[Coop] Save \"imported1\" module mismatch: \"Telepathy\" (ModuleRemovedFromGame). Forcing load anyway.", "moduleMismatch", true)]
    [InlineData("ERR [BannerlordCoopServer] the server exited unexpectedly (code -1073741819).", "accessViolation", true)]
    [InlineData("[DedicatedServer] shutdown save: world saved to 'probe1.sav' (1703 ms)", "saved", false)]
    public void bilinen_satirlar_taninir(string satir, string anahtar, bool sorun)
    {
        Assert.Equal(anahtar, KnownLogLines.Match(satir)?.Key);
        Assert.Equal(sorun, KnownLogLines.IsProblem(satir));
    }

    [Fact]
    public void tum_dunyalarin_yedekleri_nedenleriyle_listelenir()
    {
        var saves = Path.Combine(_dizin, "Game Saves");
        Directory.CreateDirectory(saves);
        foreach (var ad in new[] { "Calradia", "Vlandia" })
        {
            File.WriteAllText(Path.Combine(saves, ad + ".sav"), "sav");
            File.WriteAllText(Path.Combine(saves, ad + ".json"), "{}");
        }

        var servis = new BackupService(saves);
        servis.Create("Calradia", BackupReason.Manual);
        servis.Create("Vlandia", BackupReason.Autosave);
        servis.Create("Vlandia");

        var hepsi = servis.ListAll();

        Assert.Equal(3, hepsi.Count);
        Assert.Contains(hepsi, b => b.SaveName == "Calradia" && b.Reason == BackupReason.Manual);
        Assert.Contains(hepsi, b => b.SaveName == "Vlandia" && b.Reason == BackupReason.Autosave);
        Assert.Contains(hepsi, b => b.SaveName == "Vlandia" && b.Reason == BackupReason.Unknown);
    }
}

public class ModCompatLibraryTests
{
    [Fact]
    public void reponun_mod_listesi_okunur_ve_yeni_alanlar_dolar()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "compat", "mods.json");
        if (!File.Exists(path)) return;

        var db = ModCompatDatabase.Parse(File.ReadAllText(path));
        Assert.NotNull(db);
        Assert.True(db.Count > 300);
        Assert.Equal(CommunityStatus.Broken, db.Find("NavalDLC")!.Status);
        Assert.Contains(db.All, e => e.Status == CommunityStatus.Unknown && !string.IsNullOrEmpty(e.Name));
        Assert.Contains(db.All, e => e.Url is { } u && u.StartsWith("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void workshop_ve_nexus_adresi_uretilir()
    {
        var db = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "A", "name": "Mod A", "status": "Unknown", "workshop": "2859188632", "works": 2, "broken": 1 },
              { "id": "B", "status": "Works", "nexus": 2006 }
            ] }
            """)!;

        var a = db.Find("A")!;
        Assert.Equal("Mod A", a.DisplayName);
        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632", a.Url);
        Assert.Equal((2, 1), (a.Works, a.Broken));
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/2006", db.Find("B")!.Url);
        Assert.Equal("B", db.Find("B")!.DisplayName);
    }
}
