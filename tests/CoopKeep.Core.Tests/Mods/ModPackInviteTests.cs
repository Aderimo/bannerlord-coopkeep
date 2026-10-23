using CoopKeep.Core.Mods;
using Xunit;

namespace CoopKeep.Core.Tests.Mods;

/// <summary>Tek dosyalık davet: katılma bilgisi paketle birlikte taşınır; eski paketler bozulmaz.</summary>
public class ModPackInviteTests
{
    [Fact]
    public void Invite_RoundTrips()
    {
        var pack = new ModPack
        {
            Name = "yeni dünyam daveti",
            Mods = [new ModPackEntry("Bannerlord.Diplomacy", "Diplomacy", "v1.5.4", ModPackSource.Workshop, "2881380744")],
            Invite = new ModPackInvite("Host", "192.168.1.20:4200", "gizli", "yeni dünyam", "v0.1.6"),
        };

        var back = ModPackFile.Deserialize(ModPackFile.Serialize(pack));

        Assert.Equal(pack.Invite, back.Invite);
        Assert.Contains("192.168.1.20:4200", ModPackFile.Instructions(back, turkish: true));
    }

    [Fact]
    public void OldPackWithoutInvite_StillReads()
    {
        var back = ModPackFile.Deserialize("""{ "Format": 1, "Name": "eski", "Mods": [] }""");

        Assert.Null(back.Invite);
        Assert.Equal("eski", back.Name);
    }
}
