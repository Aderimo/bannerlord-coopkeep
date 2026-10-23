using CoopKeep.Core.Mods;
using Xunit;

namespace CoopKeep.Core.Tests.Mods;

/// <summary>
/// Paketten gelen bir klasör Coop'un, oyunun ya da Atölye'de zaten kurulu bir modun yerine
/// geçmesin: oyun Modules klasöründekini Atölye'dekine tercih ediyor.
/// </summary>
public sealed class InstallGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ck-guard-" + Guid.NewGuid().ToString("N"));

    private string Module(string id)
    {
        var folder = Path.Combine(_dir, "paket", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
            $"<Module><Name value=\"{id}\"/><Id value=\"{id}\"/><Version value=\"v1.0.0\"/></Module>");
        return folder;
    }

    [Fact]
    public void FakeCoopAndWorkshopDuplicates_AreNotInstalled()
    {
        Module("Coop");
        Module("Bannerlord.Harmony");
        Module("MyMod");
        var modules = Path.Combine(_dir, "oyun", "Modules");
        var skipped = new List<string>();

        var installed = ModArchiveInstaller.InstallFolder(Path.Combine(_dir, "paket"), modules, Path.Combine(_dir, "yedek"),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Bannerlord.Harmony"] = "v1.0.0" }, skipped);

        Assert.Equal(["MyMod"], installed.Select(m => m.Id));
        Assert.Equal(["Bannerlord.Harmony", "Coop"], skipped.Order());
        Assert.False(Directory.Exists(Path.Combine(modules, "Coop")));
    }

    [Fact]
    public void Other_version_of_a_workshop_mod_is_installed_by_hand()
    {
        // Atölye'de v5.11.99.0 var; kullanıcı arkadaşının sürümünü (v1.0.0) elle indirdi.
        Module("Bannerlord.MBOptionScreen");
        var modules = Path.Combine(_dir, "oyun", "Modules");
        var skipped = new List<string>();

        var installed = ModArchiveInstaller.InstallFolder(Path.Combine(_dir, "paket"), modules, Path.Combine(_dir, "yedek"),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Bannerlord.MBOptionScreen"] = "v5.11.99.0" }, skipped);

        Assert.Equal(["Bannerlord.MBOptionScreen"], installed.Select(m => m.Id));
        Assert.Empty(skipped);
    }

    [Theory]
    [InlineData("Coop", true)]
    [InlineData("coopnightly", true)]
    [InlineData("Native", true)]
    [InlineData("CoopKeep", true)]
    [InlineData("Bannerlord.Diplomacy", false)]
    public void IsProtected(string id, bool expected) => Assert.Equal(expected, ModArchiveInstaller.IsProtected(id));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
