using CoopKeep.Core.Mods;
using Xunit;

namespace CoopKeep.Core.Tests.Mods;

/// <summary>
/// "Sunucuya ekle" ve "Oyuna gir": bir mod eklenince gerektirdikleri de gelir, çıkarılırken
/// ona ihtiyaç duyanlar söylenir.
/// </summary>
public class ModPlannerDependencyTests
{
    private static InstalledModule Mod(string id, params string[] deps) => new()
    {
        Id = id,
        Name = id,
        Folder = id,
        Dependencies = deps.Select(d => new ModuleDependency(d, null, false)).ToList(),
    };

    // Bannerloot gibi: ButterLib (o da Harmony'ye bağlı) ve MCM ister.
    private static readonly IReadOnlyList<InstalledModule> Installed =
    [
        Mod("Bannerlord.Harmony"),
        Mod("Bannerlord.ButterLib", "Bannerlord.Harmony"),
        Mod("Bannerlord.MBOptionScreen", "Bannerlord.ButterLib"),
        Mod("keep_what_you_deserve", "Native", "Bannerlord.ButterLib", "Bannerlord.MBOptionScreen"),
        Mod("Diplomacy"),
    ];

    [Fact]
    public void ForMod_AddsDependenciesTransitively_DependenciesFirst()
    {
        var ids = ModPlanner.ForMod("keep_what_you_deserve", Installed).Mods.Select(m => m.Module.Id).ToList();

        Assert.Equal(4, ids.Count);
        Assert.DoesNotContain("Native", ids); // resmî modül bağımlılık sayılmaz
        Assert.True(ids.IndexOf("Bannerlord.Harmony") < ids.IndexOf("Bannerlord.ButterLib"));
        Assert.True(ids.IndexOf("Bannerlord.ButterLib") < ids.IndexOf("keep_what_you_deserve"));
        Assert.True(ids.IndexOf("Bannerlord.MBOptionScreen") < ids.IndexOf("keep_what_you_deserve"));
    }

    [Fact]
    public void ForMod_ReportsMissingDependency()
    {
        var plan = ModPlanner.ForMod("x", [Mod("x", "NotInstalled")]);

        Assert.Contains(plan.MissingDependencies, d => d.Dependency == "NotInstalled");
    }

    [Fact]
    public void NeededBy_FindsDirectAndIndirectUsers()
    {
        var world = new[] { "keep_what_you_deserve", "Bannerlord.ButterLib", "Bannerlord.Harmony", "Diplomacy" };

        Assert.Equal(["keep_what_you_deserve", "Bannerlord.ButterLib"], ModPlanner.NeededBy("Bannerlord.Harmony", world, Installed));
        Assert.Empty(ModPlanner.NeededBy("Diplomacy", world, Installed));
        Assert.Empty(ModPlanner.NeededBy("keep_what_you_deserve", world, Installed));
    }
}
