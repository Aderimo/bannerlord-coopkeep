using CoopKeep.Core.Mods;
using Xunit;

namespace CoopKeep.Core.Tests.Mods;

/// <summary>
/// Aynı mod Atölye'de iki sayfada (Mod Configuration Menu v5: resmî 2859238197 v5.12.3 ve bir
/// yeniden yükleme 3747620191 v5.11.99.0, ikisi de Bannerlord.MBOptionScreen).
/// </summary>
public class DuplicateModuleTests
{
    private static InstalledModule Copy(string workshopId, string version, ModuleSource source = ModuleSource.Workshop) => new()
    {
        Id = "Bannerlord.MBOptionScreen",
        Name = "Mod Configuration Menu v5",
        Folder = @"C:\ws\" + workshopId,
        Version = version,
        Source = source,
        WorkshopId = source == ModuleSource.Workshop ? workshopId : null,
    };

    private static readonly InstalledModule Official = Copy("2859238197", "v5.12.3");
    private static readonly InstalledModule Repack = Copy("3747620191", "v5.11.99.0");

    [Fact]
    public void Subscribed_copy_is_the_one_the_game_sees()
    {
        // Açık hesap yalnızca yeniden yüklemeye abone: oyun resmî kopyayı görmez.
        var picked = ModuleScanner.Pick([Official, Repack], id => id == "3747620191");
        Assert.Equal("v5.11.99.0", Assert.Single(picked).Version);
    }

    [Fact]
    public void Game_folder_copy_wins()
    {
        var manual = Copy("manual", "v5.12.3", ModuleSource.GameFolder);
        var picked = ModuleScanner.Pick([Repack, manual], _ => true);
        Assert.Equal(ModuleSource.GameFolder, Assert.Single(picked).Source);
        Assert.Empty(ModuleScanner.Duplicates([Repack, manual], _ => true));
    }

    [Fact]
    public void Two_subscribed_workshop_copies_are_reported()
    {
        var dup = ModuleScanner.Duplicates([Official, Repack], _ => true);
        Assert.Equal(2, dup["Bannerlord.MBOptionScreen"].Count);
    }

    [Fact]
    public void Copy_from_another_account_is_not_a_duplicate()
    {
        Assert.Empty(ModuleScanner.Duplicates([Official, Repack], id => id == "2859238197"));
    }
}
