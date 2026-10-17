using System.Text.Json.Nodes;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>Coop'un <c>steam-bans.json</c> biçimi (doc/SteamBans.md).</summary>
public sealed class SteamBanListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coopkeep-bans-" + Guid.NewGuid().ToString("N"));
    private string Dosya => Path.Combine(_dir, SteamBanList.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void dosya_yoksa_kimse_banli_degil()
    {
        var list = SteamBanList.Load(Dosya);
        Assert.Empty(list.Bans);
        Assert.False(list.IsBanned("76561198000000001"));
    }

    [Fact]
    public void ban_coopun_okudugu_bicimde_yazilir()
    {
        var list = SteamBanList.Load(Dosya);
        list.Ban("76561198000000001", "Hileci", "altın hilesi");
        list.Save();

        var root = JsonNode.Parse(File.ReadAllText(Dosya))!;
        Assert.Equal("76561198000000001", (string?)root["steamIds"]![0]);
        Assert.Equal("Hileci", (string?)root["entries"]![0]!["heroName"]);

        var again = SteamBanList.Load(Dosya);
        Assert.True(again.IsBanned("76561198000000001"));
        Assert.Equal("altın hilesi", again.Bans[0].Note);
        Assert.NotNull(again.Bans[0].BannedAt);
    }

    [Fact]
    public void ban_kaldirilir()
    {
        var list = SteamBanList.Load(Dosya);
        list.Ban("76561198000000001", "A");
        list.Ban("76561198000000002", "B");
        Assert.True(list.Unban("76561198000000001"));
        list.Save();

        var again = SteamBanList.Load(Dosya);
        Assert.False(again.IsBanned("76561198000000001"));
        Assert.True(again.IsBanned("76561198000000002"));
    }

    [Fact]
    public void gecersiz_steam_kimligi_banlanamaz()
        => Assert.Throws<ArgumentException>(() => SteamBanList.Load(Dosya).Ban("123", "x"));

    [Fact]
    public void elle_yazilmis_entriessiz_dosya_okunur()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Dosya, """{ "steamIds": ["76561198000000003", "not-an-id"] }""");

        var list = SteamBanList.Load(Dosya);
        Assert.Single(list.Bans);
        Assert.Null(list.Bans[0].Name);
    }

    [Fact]
    public void bozuk_dosya_kimseyi_banlamaz()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Dosya, "{ bozuk");
        Assert.Empty(SteamBanList.Load(Dosya).Bans);
    }

    [Fact]
    public void ban_dosyasi_ortam_degiskeniyle_verilir()
    {
        var options = new ServerLaunchOptions { ExecutablePath = "x.exe", SteamBanFile = Dosya };
        Assert.Equal(Path.GetFullPath(Dosya), options.BuildEnvironment()["COOP_STEAM_BAN_FILE"]);
        Assert.Empty(new ServerLaunchOptions { ExecutablePath = "x.exe" }.BuildEnvironment());
    }
}
