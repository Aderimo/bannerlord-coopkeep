using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>
/// CoopKeep sunucu eklentisi: eşya ver/al, eşya listesi, kasaba fiyatları, kurulum.
/// </summary>
/// <remarks>Yanıtlar Coop v0.1.6 sunucusunun test kopyasında alındı (<c>Fixtures/server-tools.txt</c>).</remarks>
public sealed class ServerToolsTests : IDisposable
{
    private static readonly string[] Fikstur =
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "server-tools.txt"));

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coopkeep-tools-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static IReadOnlyList<string> Yanit(string komut)
    {
        var i = Array.FindIndex(Fikstur, l => l == "[DedicatedServer] > " + komut);
        Assert.True(i >= 0, "fikstürde yok: " + komut);
        return Fikstur.Skip(i + 1).TakeWhile(l => !l.StartsWith("[DedicatedServer] > ")).ToList();
    }

    [Fact]
    public void esya_verilir_ve_dogrulanir()
    {
        Assert.Equal("coopkeep.give_item lord_1_1 grain 5", AdminCommand.GiveItem("lord_1_1", "grain", 5));

        var yanit = Yanit("coopkeep.give_item lord_1_1 grain 5");
        Assert.Equal(AdminOutcome.Success, AdminResponseParser.Classify(yanit));
        Assert.Equal((41, 46), ServerItemCatalog.ParseItemChange(yanit));
    }

    [Fact]
    public void esya_alinir()
        => Assert.Equal((46, 43), ServerItemCatalog.ParseItemChange(Yanit("coopkeep.take_item lord_1_1 grain 3")));

    [Fact]
    public void bilinmeyen_esya_ve_kahraman_bulunamadi_sayilir()
    {
        Assert.Equal(AdminOutcome.NotFound, AdminResponseParser.Classify(Yanit("coopkeep.give_item lord_1_1 no_such_item 1")));
        Assert.Equal(AdminOutcome.NotFound, AdminResponseParser.Classify(Yanit("coopkeep.give_item no_such_hero grain 1")));
    }

    [Fact]
    public void eklenti_yoksa_bilinmeyen_komut()
        => Assert.Equal(AdminOutcome.UnknownCommand,
            AdminResponseParser.Classify(["[DedicatedServer] no game console command 'coopkeep.give_item'"]));

    [Fact]
    public void envanter_okunur()
    {
        var items = ServerItemCatalog.ParsePartyItems(Yanit("coopkeep.party_items lord_1_1"));
        Assert.Equal(41, items["grain"]);
        Assert.Equal(17, items["empire_horse"]);
    }

    [Fact]
    public void esya_listesi_ve_gruplar()
    {
        var items = ServerItemCatalog.ParseItems(Yanit("coopkeep.items"));
        Assert.True(items.Count > 15);

        var grain = items.Single(i => i.Id == "grain");
        Assert.Equal(ItemGroup.Food, grain.Group);
        Assert.Equal(10, grain.Value);

        Assert.Equal(ItemGroup.Axe, items.Single(i => i.Id == "aserai_2haxe_3_t5").Group);
        Assert.Equal(ItemGroup.Crossbow, items.Single(i => i.Id == "crossbow_g").Group);
        Assert.Equal(ItemGroup.Ammo, items.Single(i => i.Id == "bolt_a").Group);

        var bracers = items.Single(i => i.Id == "battania_noble_bracers");
        Assert.Equal(ItemGroup.Hands, bracers.Group);
        Assert.True(bracers.Civilian);
        Assert.True(bracers.IsClothing);
    }

    [Fact]
    public void kasaba_fiyatlari_okunur()
    {
        var p = ServerItemCatalog.ParsePrices(Yanit("coopkeep.prices grain")).Single();
        Assert.Equal(new ItemPrice("grain", 6, "Seonon", 28, "Diathma", 13), p);
    }

    [Fact]
    public void esya_katalogu_onbellege_yazilip_okunur()
    {
        var path = Path.Combine(_dir, "items.json");
        ServerItemCatalog.Save(path, [new ServerItem("grain", "Grain", "Goods", "grain", 10, -1, "", false, true, true, "", 10)],
            [new ItemPrice("grain", 6, "A", 28, "B", 13)]);

        var (items, prices) = ServerItemCatalog.Load(path);
        Assert.Equal("Grain", items.Single().Name);
        Assert.Equal(28, prices.Single().Max);
    }

    [Fact]
    public void eklenti_coop_alt_modulu_olarak_kurulur_ve_kaldirilir()
    {
        var ds = Path.Combine(_dir, "DedicatedServer");
        var coop = ServerToolsInstaller.CoopModuleDir(ds);
        Directory.CreateDirectory(coop);
        const string Original = """
            <?xml version="1.0" encoding="utf-8"?>
            <Module>
              <Name value="Coop" />
              <Id value="Coop" />
              <SubModules>
              </SubModules>
            </Module>
            """;
        File.WriteAllText(Path.Combine(coop, "SubModule.xml"), Original);
        var dll = Path.Combine(_dir, ServerToolsInstaller.DllName);
        File.WriteAllBytes(dll, [1, 2, 3]);

        Assert.False(ServerToolsInstaller.IsInstalled(ds));
        ServerToolsInstaller.Install(ds, dll);
        ServerToolsInstaller.Install(ds, dll); // ikinci kez: değişiklik yok

        Assert.True(ServerToolsInstaller.IsInstalled(ds, dll));
        var xml = File.ReadAllText(Path.Combine(coop, "SubModule.xml"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xml, "CoopKeep.ServerTools.dll"));
        Assert.Contains("<Id value=\"Coop\" />", xml);

        ServerToolsInstaller.Uninstall(ds);
        Assert.False(ServerToolsInstaller.IsInstalled(ds));
        Assert.Equal(Original, File.ReadAllText(Path.Combine(coop, "SubModule.xml")));
        Assert.False(File.Exists(Path.Combine(coop, "bin", "Win64_Shipping_Server", ServerToolsInstaller.DllName)));
    }
}
