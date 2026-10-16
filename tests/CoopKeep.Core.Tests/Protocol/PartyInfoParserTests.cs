using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Protocol;

/// <summary>Canlı harita ve yönetici araçları için okuyucular.</summary>
public sealed class PartyInfoParserTests : IDisposable
{
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-map-" + Guid.NewGuid().ToString("N"));

    public PartyInfoParserTests() => Directory.CreateDirectory(_dizin);

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    [Fact]
    public void gercek_parti_dokumu_okunur()
    {
        var satirlar = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mobileparty-info.txt"));

        var parti = PartyInfoParser.Parse("lord_1_1_party_1", satirlar)!;

        Assert.Equal("Lucon's Party", parti.Name);
        Assert.Equal(464.27, parti.X, 2);
        Assert.Equal(469.25, parti.Y, 2);
        Assert.Null(parti.CurrentSettlement);
        Assert.Equal("Diathma", parti.TargetSettlement);
        Assert.Equal("GoToSettlement", parti.Behavior);
        Assert.False(parti.InBattle);
        Assert.False(parti.Besieging);
        Assert.False(parti.InArmy);
        Assert.Equal(76.72, parti.Morale!.Value, 2);
        Assert.Equal(31, parti.MountedMen);
        Assert.Equal(246, parti.PartySizeLimit);
    }

    [Fact]
    public void bulunamayan_parti_null_doner()
        => Assert.Null(PartyInfoParser.Parse("MainParty", ["ID: 'MainParty' not found"]));

    [Fact]
    public void kahraman_dokumunden_parti_ve_klan_okunur()
    {
        string[] satirlar =
        [
            "_name = Lucon",
            "_health = 100",
            "_clan = (1207959559) Osticos",
            "_partyBelongedTo = lord_1_1_party_1:549 Name: Lucon's Party",
            "_gold = 160000",
        ];

        var bilgi = AdminResponseParser.ParseHeroInfo(satirlar)!;

        Assert.Equal("lord_1_1_party_1", bilgi.PartyId);
        Assert.Equal("Osticos", bilgi.ClanName);
        Assert.Null(AdminResponseParser.ParseHeroInfo(["_name = X", "_partyBelongedTo = "])!.PartyId);
    }

    [Theory]
    [InlineData("No hero named \"Pacarios\" with a party found.", AdminOutcome.NotFound)]
    [InlineData("Usage: coop.debug.clan.give_influence <clan_id> <amount>", AdminOutcome.InvalidArguments)]
    [InlineData("Influence given to clan.", AdminOutcome.Sent)]
    public void yeni_komut_yanitlari_siniflandirilir(string satir, AdminOutcome beklenen)
        => Assert.Equal(beklenen, AdminResponseParser.Classify(["[Coop] alakasız log: X not found", satir]));

    [Fact]
    public void yeni_komutlar_dogru_bicimde_uretilir()
    {
        Assert.Equal("coop.debug.clan.give_influence clan_1 250", AdminCommand.GiveInfluence("clan_1", 250));
        Assert.Equal("coop.debug.clan.add_renown clan_1 100", AdminCommand.AddRenown("clan_1", 100));
        Assert.Equal("coop.debug.hero_developer.add_skill_xp hero_1 Leadership 5000",
            AdminCommand.AddSkillXp("hero_1", "Leadership", 5000));
        Assert.Equal("coop.debug.mobile_party.add_troops \"Ayşe Kılıç\"", AdminCommand.AddTroops("Ayşe Kılıç"));
        Assert.Equal("coop.debug.mobile_party.add_troop_xp Pacarios 1000", AdminCommand.AddTroopXp("Pacarios", 1000));
        Assert.Throws<ArgumentException>(() => AdminCommand.AddSkillXp("hero_1", "Hacking", 1));
        Assert.Throws<ArgumentException>(() => AdminCommand.AddTroops("kötü\"ad"));

        foreach (var komut in new[] { AdminCommand.GiveInfluence("c", 1), AdminCommand.AddTroops("x"), AdminCommand.PartyInfo("p") })
            Assert.True(AdminCommand.IsAllowed(komut), komut);
    }

    [Fact]
    public void asker_ve_yerlesim_katalogu_okunur()
    {
        var askerler = Path.Combine(_dizin, "spnpccharacters.xml");
        File.WriteAllText(askerler, """
            <NPCCharacters>
              <NPCCharacter id="imperial_recruit" name="{=abc}Imperial Recruit" level="6" occupation="Soldier" culture="Culture.empire" />
              <NPCCharacter id="imperial_legionary" name="{=def}Imperial Legionary" level="26" occupation="Soldier" culture="Culture.empire" />
              <NPCCharacter id="villager_empire" name="{=ghi}Villager" level="1" occupation="Villager" culture="Culture.empire" />
            </NPCCharacters>
            """);

        var liste = GameCatalogs.ReadTroops(askerler);
        Assert.Equal(["imperial_recruit", "imperial_legionary"], liste.Select(t => t.Id));
        Assert.Equal("Imperial Recruit", liste[0].Name);
        Assert.Equal(1, liste[0].Tier);
        Assert.Equal(5, liste[1].Tier);

        var yerlesimler = Path.Combine(_dizin, "settlements.xml");
        File.WriteAllText(yerlesimler, """
            <Settlements>
              <Settlement id="town_EN1" name="{=x}Epicrotea" posX="493.5" posY="403.1" culture="Culture.empire"><Components><Town id="t1" is_castle="false" /></Components></Settlement>
              <Settlement id="castle_EN1" name="{=y}Varagos Castle" posX="10" posY="20" culture="Culture.empire"><Components><Town id="t2" is_castle="true" /></Components></Settlement>
              <Settlement id="village_1" name="{=z}Varagos" posX="1" posY="2" culture="Culture.empire"><Components><Village id="v" /></Components></Settlement>
            </Settlements>
            """);

        var harita = GameCatalogs.ReadSettlements(yerlesimler);
        Assert.Equal([SettlementKind.Town, SettlementKind.Castle, SettlementKind.Village], harita.Select(s => s.Kind));
        Assert.Equal("Epicrotea", harita[0].Name);
        Assert.Equal(493.5, harita[0].X);
    }
}
