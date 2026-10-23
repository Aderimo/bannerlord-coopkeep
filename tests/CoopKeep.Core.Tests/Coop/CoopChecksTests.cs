using CoopKeep.Core.Coop;
using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Tests.Coop;

public class CoopChecksTests
{
    private static SetupFacts Ready() => new()
    {
        GameFound = true,
        GameVersion = "v1.4.8",
        CoopFound = true,
        CoopVersion = "v0.1.6",
        CoopRequiredGameVersion = "v1.4.8",
        ServerPackageFound = true,
        FirewallOk = true,
    };

    private static IEnumerable<string> Ids(IEnumerable<CheckResult> r) => r.Select(x => x.Id);

    [Fact]
    public void her_sey_hazirsa_yalnizca_basarili_satirlar()
    {
        var r = CoopChecks.Setup(Ready());
        Assert.Equal(["gameFound", "coopFound"], Ids(r));
        Assert.All(r, x => Assert.Equal(CheckSeverity.Ok, x.Severity));
    }

    [Fact]
    public void oyun_yoksa_baska_denetim_yapilmaz()
    {
        var r = CoopChecks.Setup(Ready() with { GameFound = false });
        Assert.Equal(["gameMissing"], Ids(r));
        Assert.Equal(CheckFix.OpenGameStore, r[0].Fix);
    }

    [Fact]
    public void coop_yoksa_atolye_onerilir()
    {
        var r = CoopChecks.Setup(Ready() with { CoopFound = false });
        Assert.Equal(["gameFound", "coopMissing"], Ids(r));
        Assert.Equal(CheckFix.OpenCoopWorkshop, r[1].Fix);
    }

    [Fact]
    public void surum_derleme_numarasi_olmadan_karsilastirilir()
    {
        Assert.DoesNotContain("versionMismatch", Ids(CoopChecks.Setup(Ready() with { GameVersion = "v1.4.8.118999" })));

        var r = CoopChecks.Setup(Ready() with { GameVersion = "v1.4.7" });
        var mismatch = Assert.Single(r, x => x.Id == "versionMismatch");
        Assert.Equal(CheckSeverity.Error, mismatch.Severity);
        Assert.Equal(["v1.4.7", "v1.4.8"], mismatch.Args);
    }

    [Fact]
    public void dlc_engelli_dosya_ve_guvenlik_duvari_duzeltmeleriyle_gelir()
    {
        var r = CoopChecks.Setup(Ready() with
        {
            ServerPackageFound = false,
            EnabledBlockedModules = ["NavalDLC"],
            BlockedFiles = 3,
            FirewallOk = false,
        });

        Assert.Equal(CheckFix.OpenCoopWorkshop, r.Single(x => x.Id == "serverMissing").Fix);
        Assert.Equal(CheckFix.DisableDlc, r.Single(x => x.Id == "dlcEnabled").Fix);
        Assert.Equal(["3"], r.Single(x => x.Id == "filesBlocked").Args);
        Assert.Equal(CheckSeverity.Warning, r.Single(x => x.Id == "firewallMissing").Severity);

        // Güvenlik duvarı bilinmiyorsa uyarılmaz.
        Assert.DoesNotContain("firewallMissing", Ids(CoopChecks.Setup(Ready() with { FirewallOk = null })));
    }

    private static PreflightFacts Clean() => new()
    {
        WorldName = "Calradia",
        WorldSaveExists = true,
        SteamLobbyEnabled = true,
        SteamRunning = true,
        Port = 4200,
    };

    [Fact]
    public void sorunsuz_acilista_bir_sey_soylenmez()
    {
        Assert.Empty(CoopChecks.Preflight(Clean()));
    }

    [Fact]
    public void kayit_yoksa_sessizce_yeni_dunya_acilmasin_diye_uyarilir()
    {
        var r = CoopChecks.Preflight(Clean() with { WorldSaveExists = false });
        var s = Assert.Single(r);
        Assert.Equal(("saveMissing", CheckSeverity.Warning), (s.Id, s.Severity));
        Assert.Equal(["Calradia"], s.Args);
    }

    [Fact]
    public void steam_kapaliysa_uyarilir_ama_lobi_kapaliysa_uyarilmaz()
    {
        Assert.Equal(["steamNotRunning"], Ids(CoopChecks.Preflight(Clean() with { SteamRunning = false })));
        Assert.Empty(CoopChecks.Preflight(Clean() with { SteamRunning = false, SteamLobbyEnabled = false }));
    }

    [Fact]
    public void eksik_dunya_modu_hata_sayilir()
    {
        var r = CoopChecks.Preflight(Clean() with { WorldMods = ["Calradia Expanded", "Harmony"], MissingWorldMods = ["Calradia Expanded"] });
        Assert.True(CoopChecks.HasErrors(r));
        Assert.Contains(r, x => x.Id == "moddedWorld" && x.Severity == CheckSeverity.Info);
        // Düzeltme düğmesi eksikleri getirir (yalnızca Modlar sayfasını açmaz).
        Assert.Contains(r, x => x.Id == "modsMissing" && x.Fix == CheckFix.GetMissingMods);
    }

    [Fact]
    public void ortam_degiskeni_tirnakli_ya_da_goreliyse_uyarilir()
    {
        var env = new Dictionary<string, string>
        {
            ["COOP_DATA_DIR"] = "\"C:\\Coop Data\"",
            ["BANNERLORD_USER_DIR"] = "relative\\dir",
            ["COOP_STEAM_BAN_FILE"] = @"C:\bans\steam-bans.json",
        };

        var r = CoopChecks.Preflight(Clean() with { Environment = env });
        Assert.Equal(2, r.Count(x => x.Id == "envInvalid" && x.Severity == CheckSeverity.Warning));
        Assert.Single(r, x => x.Id == "envSet");
    }

    [Fact]
    public void onedrive_yolu_taninir()
    {
        Assert.True(CoopChecks.IsOneDrivePath(@"C:\Users\a\OneDrive\Documents"));
        Assert.True(CoopChecks.IsOneDrivePath(@"C:\Users\a\OneDrive - Okul\Belgeler"));
        Assert.False(CoopChecks.IsOneDrivePath(@"C:\Users\a\Documents"));
    }

    [Fact]
    public void oyun_coop_ile_dogru_modul_listesiyle_acilir()
    {
        var modules = CoopClientLauncher.ModuleList(["Bannerlord.Harmony", "NavalDLC", "Sandbox", "CoopKeep", "CoopNightly", "Calradia Expanded"]);
        Assert.Equal(["Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle", "Bannerlord.Harmony", "Calradia Expanded", "Coop"], modules);

        Assert.Equal(["/singleplayer", "_MODULES_*Native*Coop*_MODULES_"], CoopClientLauncher.Arguments(["Native", "Coop"]));
    }
}
