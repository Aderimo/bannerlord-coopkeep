using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Tests.Installation;

public class EpicLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ck-epic-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void epic_bildiriminden_oyun_ve_elle_kurulan_coop_bulunur()
    {
        var game = Path.Combine(_root, "Epic", "MountAndBlade2");
        Directory.CreateDirectory(Path.Combine(game, "bin"));
        Directory.CreateDirectory(Path.Combine(game, "Modules", "Coop"));
        File.WriteAllText(Path.Combine(game, "Modules", "Coop", "SubModule.xml"),
            "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/><DependedModules><DependedModule Id=\"Native\" DependentVersion=\"v1.4.8\"/></DependedModules></Module>");

        var manifests = Path.Combine(_root, "Manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "baska.item"), "{\"DisplayName\":\"Fortnite\",\"InstallLocation\":\"C:/x\"}");
        File.WriteAllText(Path.Combine(manifests, "bozuk.item"), "{bozuk");
        File.WriteAllText(Path.Combine(manifests, "bl.item"),
            System.Text.Json.JsonSerializer.Serialize(new { DisplayName = "Mount & Blade II: Bannerlord", InstallLocation = game }));

        Assert.Equal(game, GameLocator.FindEpicGame(manifests));
        Assert.Null(GameLocator.FindEpicGame(Path.Combine(_root, "yok")));

        // Steam Atölyesi'nde Coop yoksa oyunun Modules klasöründeki elle kurulum bulunur (sunucu paketi olmadan).
        var coop = GameLocator.FindCoop(game);
        if (GameLocator.FindCoopInstallations().Count == 0)
        {
            Assert.NotNull(coop);
            Assert.Equal(("Coop", "v0.1.6", "v1.4.8"), (coop!.ModuleId, coop.ModVersion, coop.RequiredGameVersion));
            Assert.False(File.Exists(coop.DedicatedServerExe));
        }
    }
}
