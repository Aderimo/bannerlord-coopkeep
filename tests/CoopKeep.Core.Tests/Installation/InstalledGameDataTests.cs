using CoopKeep.Core.Installation;
using CoopKeep.Core.Map;

namespace CoopKeep.Core.Tests.Installation;

/// <summary>
/// Oyunun kendi veri dosyalarını okuyan kodlar. Oyun kurulu değilse (CI) testler
/// hiçbir şey yapmadan geçer: CoopKeep oyun dosyalarını repoda taşımaz.
/// </summary>
public class InstalledGameDataTests
{
    private static readonly string Modules =
        @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules";

    private static bool Installed => Directory.Exists(Modules);

    [Fact]
    public void navmesh_okunur()
    {
        if (!Installed) return;

        var faces = NavMeshReader.Read(NavMeshReader.PathIn(Modules));
        Assert.NotNull(faces);
        Assert.True(faces.Count > 10_000);
        Assert.All(faces.Take(100), f => Assert.InRange(f.Points.Count, 3, 16));
        Assert.All(faces.Take(100), f => Assert.InRange(f.Points[0].X, 0, 1000));
    }

    [Fact]
    public void askerler_sinif_ve_terfiyle_okunur()
    {
        if (!Installed) return;

        var troops = GameCatalogs.ReadTroops(Path.Combine(Modules, "SandBoxCore", "ModuleData", "spnpccharacters.xml"));
        var recruit = troops.Single(t => t.Id == "imperial_recruit");
        Assert.Equal("Infantry", recruit.Class);
        Assert.NotEmpty(recruit.UpgradeTargets);
        Assert.Contains(troops, t => t.Class == "HorseArcher");
    }

    [Fact]
    public void klanlar_krallik_ve_yerlesim_sahipleri_okunur()
    {
        if (!Installed) return;

        var clans = GameCatalogs.ReadClans(Path.Combine(Modules, "SandBox", "ModuleData", "spclans.xml"));
        Assert.Contains(clans, c => c is { Id: "clan_empire_north_1", Name: "Osticos", KingdomId: "empire", LeaderId: "lord_1_1" });

        var kingdoms = GameCatalogs.ReadKingdoms(Path.Combine(Modules, "SandBox", "ModuleData", "spkingdoms.xml"));
        Assert.Contains(kingdoms, k => k is { Id: "empire", RulerId: "lord_1_1" });

        var settlements = GameCatalogs.ReadSettlements(Path.Combine(Modules, "SandBox", "ModuleData", "settlements.xml"));
        Assert.Equal("clan_empire_north_3", settlements.Single(s => s.Id == "castle_EN1").OwnerClanId);
        Assert.Equal("castle_EN1", settlements.Single(s => s.Id == "castle_village_EN1_1").BoundTo);
    }

    [Fact]
    public void esyalar_okunur()
    {
        if (!Installed) return;

        var items = GameCatalogs.ReadItems(Modules);
        Assert.True(items.Count > 300);
        Assert.Contains(items, i => i.Type == "Horse");
        Assert.DoesNotContain(items, i => i.Name.Contains("{", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("FF39223F", 0xFF39223Fu)]
    [InlineData("0xff793191", 0xFF793191u)]
    [InlineData(null, 0xFF808080u)]
    public void renk_okunur(string? text, uint expected) => Assert.Equal(expected, GameCatalogs.ParseColor(text));
}
