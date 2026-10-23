using CoopKeep.Core.Installation;
using CoopKeep.Core.Mods;

namespace CoopKeep.Core.Tests.Mods;

public class CoopModRolesTests
{
    private static InstalledModule Mod(string id, bool code = false, bool clientBins = false) => new()
    {
        Id = id,
        Name = id,
        Folder = @"C:\Mods\" + id,
        HasCode = code,
        HasClientBinaries = clientBins,
    };

    [Fact]
    public void coopun_reddettigi_ve_toplulugun_bozuk_dedigi_uyumsuz_sayilir()
    {
        Assert.Equal(CoopModRole.Incompatible, CoopModRoles.Classify(Mod("NavalDLC")));
        Assert.Equal(CoopModRole.Incompatible, CoopModRoles.Classify(Mod("CoopKeep")));
        Assert.Equal(CoopModRole.Incompatible, CoopModRoles.Classify(Mod("Bir Mod"), CommunityStatus.Broken));
    }

    [Fact]
    public void kutuphane_veri_ve_kodlu_mod_ayrilir()
    {
        Assert.Equal(CoopModRole.Library, CoopModRoles.Classify(Mod("Bannerlord.Harmony", code: true, clientBins: true)));
        Assert.Equal(CoopModRole.Library, CoopModRoles.Classify(Mod("Arayuz", code: true, clientBins: true), CommunityStatus.ClientOnly));
        Assert.Equal(CoopModRole.Data, CoopModRoles.Classify(Mod("Esyalar")));
    }

    [Fact]
    public void baska_oyun_surumu_icin_yapilan_mod_taninir()
    {
        Assert.True(CoopModRoles.IsForOtherGameVersion("v1.3.4", "v1.4.8"));
        Assert.True(CoopModRoles.IsForOtherGameVersion("e1.5.2", "v1.4.8"));
        Assert.False(CoopModRoles.IsForOtherGameVersion("v1.4.7", "v1.4.8"));
        Assert.False(CoopModRoles.IsForOtherGameVersion(null, "v1.4.8"));
        Assert.False(CoopModRoles.IsForOtherGameVersion("bilinmiyor", "v1.4.8"));
    }

    [Fact]
    public void atolye_aciklamasindan_ilk_anlamli_satir_alinir()
    {
        var text = "[h1]Başlık[/h1]\n\n[b]Bu mod savaş alanına yeni birlikler ekler ve dengeyi korur.[/b]\n[url=x]bağlantı[/url]";
        Assert.Equal("Bu mod savaş alanına yeni birlikler ekler ve dengeyi korur.", CoopModRoles.ShortDescription(text));
        Assert.Equal("", CoopModRoles.ShortDescription(null));
        Assert.EndsWith("…", CoopModRoles.ShortDescription(new string('a', 300)));
    }
}
