using CoopKeep.App.ViewModels;
using CoopKeep.Core.Mods;

namespace CoopKeep.App.Tests;

/// <summary>Modlar sayfasındaki satır ve Ayarlar'daki tema seçeneği modelleri.</summary>
public class RowModelTests
{
    private static InstalledModule Module(string id, ModuleSource source = ModuleSource.Workshop, bool official = false) => new()
    {
        Id = id,
        Name = id,
        Folder = @"C:\mods\" + id,
        Version = "v1.0.0",
        Source = source,
        IsOfficial = official,
    };

    [Fact]
    public void Game_base_modules_are_locked_and_user_mods_move()
    {
        var native = new ManagedMod(Module("Native", ModuleSource.GameFolder, official: true), "Native", true, () => { });
        var mod = new ManagedMod(Module("BetterTime"), "BetterTime", false, () => { });

        Assert.False(native.CanToggle);
        Assert.False(native.CanMove);
        Assert.True(mod.CanToggle);
        Assert.True(mod.CanMove);
        Assert.True(mod.IsUserMod);
    }

    [Fact]
    public void Coop_can_be_turned_on_but_not_off()
    {
        var off = new ManagedMod(Module("Coop"), "Coop", false, () => { });
        var on = new ManagedMod(Module("Coop"), "Coop", true, () => { });

        Assert.True(off.CanToggle);
        Assert.False(on.CanToggle);
        Assert.False(on.IsUserMod);
    }

    [Fact]
    public void Rejected_mod_can_only_be_turned_off()
    {
        var on = new ManagedMod(Module("Bad"), "Bad", true, () => { }) { Role = CoopModRole.Incompatible };
        var off = new ManagedMod(Module("Bad"), "Bad", false, () => { }) { Role = CoopModRole.Incompatible };

        Assert.True(on.CanToggle);
        Assert.False(off.CanToggle);
        Assert.True(on.RoleIsIncompatible);
    }

    [Fact]
    public void Not_subscribed_counts_as_a_problem()
    {
        var mod = new ManagedMod(Module("X"), "X", true, () => { });
        Assert.False(mod.HasProblem);
        mod.NotSubscribed = true;
        Assert.True(mod.HasProblem);
    }

    [Fact]
    public void Only_hand_installed_mods_can_be_removed()
    {
        Assert.True(new ManagedMod(Module("Local", ModuleSource.GameFolder), "Local", true, () => { }).CanRemove);
        Assert.False(new ManagedMod(Module("Ws"), "Ws", true, () => { }).CanRemove);
    }

    [Fact]
    public void Toggling_a_row_reports_the_change_and_dims_when_off()
    {
        var changes = 0;
        var mod = new ManagedMod(Module("X"), "X", true, () => changes++);

        mod.IsSelected = false;

        Assert.Equal(1, changes);
        Assert.True(mod.RowOpacity < 1);
    }

    [Fact]
    public void Theme_option_reports_only_when_selected()
    {
        var picked = new List<string>();
        var option = new ThemeOption("Night", "Gece", o => picked.Add(o.Key));

        option.IsSelected = true;
        option.IsSelected = false;

        Assert.Equal(["Night"], picked);
    }
}
