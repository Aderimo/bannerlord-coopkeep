using CoopKeep.Core.Installation;
using CoopKeep.Core.Mods;
using Xunit;

namespace CoopKeep.Core.Tests.Mods;

/// <summary>
/// Harmony/ButterLib gibi kütüphaneler resmî modüllerden önce yüklenmeli. Eskiden resmî modüller
/// hep başa konuyordu ve ButterLib oyun açılırken "Native'den önce yüklenmeli" uyarısı veriyordu.
/// </summary>
public class LoadOrderEarlyTests
{
    private static readonly string[] Official = ["Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle"];

    private static InstalledModule Mod(string id, string[]? loadAfter = null, string[]? loadBefore = null, string[]? deps = null) => new()
    {
        Id = id,
        Name = id,
        Folder = id,
        LoadAfter = loadAfter ?? [],
        LoadBefore = loadBefore ?? [],
        Dependencies = (deps ?? []).Select(d => new ModuleDependency(d, null, false)).ToList(),
    };

    // Gerçek SubModule.xml'lerdeki gibi: kütüphaneler resmî modüllere "LoadAfterThis" der.
    private static readonly IReadOnlyList<InstalledModule> Installed =
    [
        Mod("Bannerlord.Harmony", loadBefore: Official),
        Mod("Bannerlord.ButterLib", loadAfter: ["Bannerlord.Harmony"], loadBefore: Official, deps: ["Bannerlord.Harmony"]),
        Mod("Bannerlord.UIExtenderEx", loadAfter: ["Bannerlord.Harmony"], loadBefore: Official),
        Mod("Bannerlord.MBOptionScreen", loadAfter: ["Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx"], loadBefore: Official),
        Mod("Bannerlord.Diplomacy", loadAfter: ["Native", "Bannerlord.ButterLib", "Bannerlord.MBOptionScreen"]),
        Mod("both_perks", loadAfter: ["Native"]),
    ];

    [Fact]
    public void Libraries_LoadBeforeOfficialModules()
    {
        var ids = new[] { "Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle", "Bannerlord.Harmony", "Bannerlord.ButterLib",
                          "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen", "Bannerlord.Diplomacy", "both_perks", "Coop" };

        var order = LoadOrder.Sort(ids, Installed, out var conflict).ToList();

        Assert.False(conflict);
        var native = order.IndexOf("Native");
        foreach (var lib in new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen" })
            Assert.True(order.IndexOf(lib) < native, lib + " Native'den önce olmalı");
        Assert.True(order.IndexOf("Bannerlord.Harmony") < order.IndexOf("Bannerlord.ButterLib"));
        Assert.True(order.IndexOf("Bannerlord.ButterLib") < order.IndexOf("Bannerlord.MBOptionScreen"));
        Assert.True(order.IndexOf("CustomBattle") < order.IndexOf("Bannerlord.Diplomacy"), "normal modlar resmî modüllerden sonra");
        Assert.True(order.IndexOf("CustomBattle") < order.IndexOf("both_perks"));
        Assert.Equal("Coop", order[^1]);
    }

    [Fact]
    public void ClientModuleList_UsesTheSameOrder()
    {
        var list = CoopClientLauncher.ModuleList(["Bannerlord.Diplomacy", "Bannerlord.ButterLib", "Bannerlord.Harmony", "Bannerlord.MBOptionScreen", "Bannerlord.UIExtenderEx"],
            "Coop", Installed).ToList();

        Assert.Equal("Bannerlord.Harmony", list[0]);
        Assert.True(list.IndexOf("Bannerlord.ButterLib") < list.IndexOf("Native"));
        Assert.True(list.IndexOf("Native") < list.IndexOf("Bannerlord.Diplomacy"));
        Assert.Equal("Coop", list[^1]);
    }

    [Fact]
    public void WithoutLibraries_OfficialStaysFirst()
    {
        var order = LoadOrder.Sort(["both_perks", "Native", "Sandbox", "Coop"], Installed, out _);

        Assert.Equal(["Native", "Sandbox", "both_perks", "Coop"], order);
    }
}
