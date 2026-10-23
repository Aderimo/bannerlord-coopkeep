using CoopKeep.Core.Net;
using Xunit;

namespace CoopKeep.Core.Tests.Net;

/// <summary>Program içi güncelleme: GitHub yanıtından exe adresi ve kendini değiştirme betiği.</summary>
public class ReleaseInfoTests
{
    [Fact]
    public void Parse_FindsExeAsset()
    {
        const string json = """
            {
              "tag_name": "v1.0.1",
              "assets": [
                { "name": "CoopKeep-src.zip", "browser_download_url": "https://github.com/a/b/releases/download/v1.0.1/src.zip", "size": 10 },
                { "name": "CoopKeep.exe", "browser_download_url": "https://github.com/a/b/releases/download/v1.0.1/CoopKeep.exe", "size": 46000000 }
              ]
            }
            """;

        var info = ReleaseInfo.Parse(json);

        Assert.NotNull(info);
        Assert.Equal("v1.0.1", info.Tag);
        Assert.Equal("https://github.com/a/b/releases/download/v1.0.1/CoopKeep.exe", info.ExeUrl);
        Assert.Equal(46000000, info.ExeSize);
    }

    [Fact]
    public void Parse_IgnoresNonGitHubDownloads()
    {
        const string json = """{ "tag_name": "v2.0.0", "assets": [ { "name": "CoopKeep.exe", "browser_download_url": "https://example.com/CoopKeep.exe" } ] }""";

        Assert.Null(ReleaseInfo.Parse(json)!.ExeUrl);
    }

    [Fact]
    public void Parse_NoAssets_StillReturnsTag()
    {
        var info = ReleaseInfo.Parse("""{ "tag_name": "v1.1.0" }""");

        Assert.Equal("v1.1.0", info!.Tag);
        Assert.Null(info.ExeUrl);
    }

    [Fact]
    public void UpdaterScript_EscapesPercentInPaths()
    {
        var script = ReleaseInfo.UpdaterScript(1, @"C:\100%\CoopKeep.new.exe", @"C:\100%\CoopKeep (1).exe");

        Assert.Contains(@"move /y ""C:\100%%\CoopKeep.new.exe"" ""C:\100%%\CoopKeep (1).exe""", script);
    }

    [Fact]
    public void PackTempRoot_IsOnlyOurTempFolder()
    {
        Assert.Equal(@"C:\t\coopkeep-pack-abc", CoopKeep.Core.Mods.ModPackFile.TempRootOf(@"C:\t\coopkeep-pack-abc\Modules"));
        Assert.Null(CoopKeep.Core.Mods.ModPackFile.TempRootOf(@"C:\Users\x\Modules"));
        Assert.Null(CoopKeep.Core.Mods.ModPackFile.TempRootOf(null));
    }

    [Fact]
    public void UpdaterScript_WaitsMovesAndRestarts()
    {
        var script = ReleaseInfo.UpdaterScript(1234, @"C:\x\CoopKeep.new.exe", @"C:\x\CoopKeep.exe");

        Assert.Contains("PID eq 1234", script);
        Assert.Contains(@"move /y ""C:\x\CoopKeep.new.exe"" ""C:\x\CoopKeep.exe""", script);
        Assert.Contains(@"start """" ""C:\x\CoopKeep.exe""", script);
    }
}
