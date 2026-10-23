using CoopKeep.Core.Coop;

namespace CoopKeep.Core.Tests.Coop;

public class BlockedFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ck-blocked-" + Guid.NewGuid().ToString("N"));

    public BlockedFilesTests() => Directory.CreateDirectory(Path.Combine(_dir, "bin"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void internetten_indirildi_isareti_bulunur_ve_kaldirilir()
    {
        if (!OperatingSystem.IsWindows()) return; // işaret NTFS'e özgü

        var blocked = Path.Combine(_dir, "bin", "Mod.dll");
        var clean = Path.Combine(_dir, "bin", "Other.dll");
        File.WriteAllText(blocked, "x");
        File.WriteAllText(clean, "x");
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "x");
        File.WriteAllText(blocked + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var found = BlockedFiles.Find([_dir, Path.Combine(_dir, "yok")]);
        Assert.Equal([blocked], found);

        Assert.Equal(1, BlockedFiles.Unblock(found));
        Assert.False(BlockedFiles.IsBlocked(blocked));
        Assert.True(File.Exists(blocked));
        Assert.Empty(BlockedFiles.Find([_dir]));
    }
}
