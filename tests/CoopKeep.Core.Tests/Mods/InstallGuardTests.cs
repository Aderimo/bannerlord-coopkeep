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
            new HashSet<string>(["Bannerlord.Harmony"], StringComparer.OrdinalIgnoreCase), skipped);

        Assert.Equal(["MyMod"], installed.Select(m => m.Id));
        Assert.Equal(["Bannerlord.Harmony", "Coop"], skipped.Order());
        Assert.False(Directory.Exists(Path.Combine(modules, "Coop")));
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
