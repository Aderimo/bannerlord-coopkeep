using CoopKeep.Core.Encyclopedia;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;

namespace CoopKeep.Core.Tests.Encyclopedia;

/// <summary>
/// Ansiklopedi: oyunun XML'lerinden okuma, oyunun Türkçe çevirisi, rehber sıralamaları.
/// </summary>
/// <remarks>Örnekler oyunun kendi dosyalarının biçiminde, kısaltılmış (2026-10-17).</remarks>
public sealed class EncyclopediaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coopkeep-enc-" + Guid.NewGuid().ToString("N"));

    public EncyclopediaTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string relative, string content, System.Text.Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, encoding ?? System.Text.Encoding.UTF8);
        return path;
    }

    private void WriteModules()
    {
        Write("SandBox/ModuleData/spkingdoms.xml", """
            <Kingdoms><Kingdom id="empire" name="{=k1}Northern Empire" owner="Hero.lord_1_1" culture="Culture.empire"
              color2="FFAA00CC" text="{=k2}Many of the oldest families live in the north." /></Kingdoms>
            """);
        Write("SandBox/ModuleData/spclans.xml", """
            <Factions>
              <Faction id="clan_empire_north_1" name="{=c1}Osticos" tier="6" owner="Hero.lord_1_1" culture="Culture.empire"
                super_faction="Kingdom.empire" initial_home_settlement="Settlement.town_EN2" />
              <Faction id="looters" name="Looters" is_bandit="true" tier="0" />
              <Faction id="guardians" name="Guardians" is_minor_faction="true" tier="3" />
            </Factions>
            """);
        Write("SandBox/ModuleData/lords.xml", """
            <NPCCharacters>
              <NPCCharacter id="lord_1_1" name="{=CLqPbdiZ}Lucon" age="62" culture="Culture.empire" occupation="Lord">
                <Traits><Trait id="Commander" value="16" /><Trait id="Honor" value="1" /></Traits>
              </NPCCharacter>
              <NPCCharacter id="lord_1_2" name="{=x}Arenicos" age="40" is_female="true" culture="Culture.empire" occupation="Lord" />
            </NPCCharacters>
            """);
        Write("SandBox/ModuleData/heroes.xml", """
            <Heroes>
              <Hero id="lord_1_1" spouse="Hero.lord_1_2" faction="Faction.clan_empire_north_1"
                text="{=WBl5hS0e}The northern third of the Empire is ruled by {FIRSTNAME} Lucon." />
              <Hero id="lord_1_2" faction="Faction.clan_empire_north_1" />
            </Heroes>
            """);
        Write("SandBox/ModuleData/settlements.xml", """
            <Settlements>
              <Settlement id="town_EN1" name="{=Settlements.Settlement.name.town_EN1}Epicrotea" owner="Faction.clan_empire_north_1"
                posX="400" posY="470" culture="Culture.empire" text="{=n9}Epicrotea, the All-Conquering.">
                <Components><Town id="t1" prosperity="5100"><Buildings><Building id="building_settlement_fortifications" level="2" /></Buildings></Town></Components>
              </Settlement>
              <Settlement id="town_EN2" name="Ostican" posX="460" posY="470" culture="Culture.empire">
                <Components><Town id="t2" prosperity="6200" /></Components>
              </Settlement>
              <Settlement id="town_S1" name="Far Town" posX="900" posY="900" culture="Culture.sturgia">
                <Components><Town id="t3" prosperity="3000" /></Components>
              </Settlement>
              <Settlement id="castle_EN1" name="Varagos Castle" posX="430" posY="470" culture="Culture.empire">
                <Components><Town id="c1" is_castle="true" prosperity="420" /></Components>
              </Settlement>
              <Settlement id="castle_S1" name="Lonely Castle" posX="880" posY="880" culture="Culture.sturgia">
                <Components><Town id="c2" is_castle="true" prosperity="300" /></Components>
              </Settlement>
              <Settlement id="village_EN1_1" name="Ain" posX="405" posY="475" culture="Culture.empire">
                <Components><Village id="v1" village_type="VillageType.silk_plant" hearth="322" bound="Settlement.town_EN1" /></Components>
              </Settlement>
              <Settlement id="hideout_1" name="Hideout" posX="1" posY="1"><Components><Hideout id="h" /></Components></Settlement>
              <Settlement id="other" name="Training field" posX="1" posY="1" />
            </Settlements>
            """);
        Write("SandBoxCore/ModuleData/spnpccharacters.xml", """
            <NPCCharacters>
              <NPCCharacter id="imperial_recruit" level="6" name="{=r}Imperial Recruit" occupation="Soldier" culture="Culture.empire">
                <skills><skill id="OneHanded" value="20" /></skills>
                <upgrade_targets><upgrade_target id="NPCCharacter.imperial_infantryman" /></upgrade_targets>
              </NPCCharacter>
              <NPCCharacter id="imperial_infantryman" level="11" name="Imperial Infantryman" occupation="Soldier" culture="Culture.empire">
                <skills><skill id="OneHanded" value="50" /><skill id="Athletics" value="40" /></skills>
                <Equipments><EquipmentRoster><equipment slot="Item0" id="Item.empire_sword_1_t2" /></EquipmentRoster></Equipments>
              </NPCCharacter>
              <NPCCharacter id="villager" level="6" name="Villager" occupation="Villager" culture="Culture.empire" />
            </NPCCharacters>
            """);
        Write("SandBoxCore/ModuleData/spcultures.xml", """<Cultures><Culture id="empire" name="{=empirefaction}Empire" /></Cultures>""");

        // Oyunun Türkçe çevirisi UTF-16 geliyor.
        Write("SandBox/ModuleData/Languages/TR/std_heroes_xml_tur.xml", """
            <?xml version="1.0" encoding="utf-16"?>
            <base><strings>
              <string id="WBl5hS0e" text="İmparatorluğun kuzeyi {FIRSTNAME} Lucon tarafından yönetiliyor." />
              <string id="Settlements.Settlement.name.town_EN1" text="Epikrotea" />
            </strings></base>
            """, System.Text.Encoding.Unicode);
    }

    [Fact]
    public void cevirisiz_metin_anahtarsiz_ingilizce_kalir()
    {
        Assert.Equal("Lucon", GameTexts.English.Resolve("{=CLqPbdiZ}Lucon"));
        Assert.Equal("Plain", GameTexts.English.Resolve("Plain"));
        Assert.Equal("", GameTexts.English.Resolve(null));
    }

    [Fact]
    public void oyunun_turkce_cevirisi_kullanilir()
    {
        WriteModules();
        var tr = GameTexts.Load(_dir, "TR");

        Assert.Equal(2, tr.Count);
        Assert.Equal("İmparatorluğun kuzeyi Lucon tarafından yönetiliyor.",
            tr.Resolve("{=WBl5hS0e}The northern third of the Empire is ruled by {FIRSTNAME} Lucon."));
        Assert.Equal("Epikrotea", tr.Resolve("{=Settlements.Settlement.name.town_EN1}Epicrotea"));
        Assert.Equal("Osticos", tr.Resolve("{=c1}Osticos"));   // çevirisi yoksa İngilizce
    }

    [Fact]
    public void oyun_verileri_okunur()
    {
        WriteModules();
        var data = EncyclopediaData.Load(_dir, GameTexts.English);

        var empire = Assert.Single(data.Kingdoms);
        Assert.Equal("Northern Empire", empire.Name);
        Assert.Equal("lord_1_1", empire.RulerId);
        Assert.StartsWith("Many of the oldest", empire.Text);

        var osticos = data.Clans.Single(c => c.Id == "clan_empire_north_1");
        Assert.Equal(6, osticos.Tier);
        Assert.Equal("empire", osticos.KingdomId);
        Assert.Equal("town_EN2", osticos.HomeSettlementId);
        Assert.True(data.Clans.Single(c => c.Id == "looters").IsBandit);

        var lucon = data.Lords.Single(l => l.Id == "lord_1_1");
        Assert.Equal(62, lucon.Age);
        Assert.Equal("clan_empire_north_1", lucon.ClanId);
        Assert.Equal("lord_1_2", lucon.SpouseId);
        Assert.Equal(16, lucon.Traits["Commander"]);
        Assert.Equal("The northern third of the Empire is ruled by Lucon.", lucon.Text);
        Assert.True(data.Lords.Single(l => l.Id == "lord_1_2").IsFemale);

        Assert.Equal(7, data.Settlements.Count);   // "other" atlanır, sığınak kalır
        var epicrotea = data.Settlements.Single(s => s.Id == "town_EN1");
        Assert.Equal(SettlementKind.Town, epicrotea.Kind);
        Assert.Equal(5100, epicrotea.Prosperity);
        Assert.Equal(2, epicrotea.Fortifications);
        Assert.Equal("clan_empire_north_1", epicrotea.OwnerClanId);
        var ain = data.Settlements.Single(s => s.Id == "village_EN1_1");
        Assert.Equal("town_EN1", ain.BoundTo);
        Assert.Equal("silk_plant", ain.Produces);
        Assert.Equal(SettlementKind.Castle, data.Settlements.Single(s => s.Id == "castle_EN1").Kind);

        Assert.Equal(2, data.Troops.Count);   // köylü asker değil
        var recruit = data.Troops.Single(t => t.Id == "imperial_recruit");
        Assert.Equal(["imperial_infantryman"], recruit.UpgradeTargets);
        Assert.Equal(1, recruit.Tier);
        var infantry = data.Troops.Single(t => t.Id == "imperial_infantryman");
        Assert.Equal(90, infantry.SkillTotal);
        Assert.Equal(["empire_sword_1_t2"], infantry.Equipment);
        Assert.Equal("Empire", data.Cultures["empire"]);
    }

    [Fact]
    public void eksik_dosyalar_bos_liste_olur()
    {
        var data = EncyclopediaData.Load(Path.Combine(_dir, "yok"), GameTexts.English);
        Assert.Empty(data.Kingdoms);
        Assert.Empty(data.Troops);
    }

    [Fact]
    public void rehberler_oyunun_sayilarindan_siralar()
    {
        WriteModules();
        var data = EncyclopediaData.Load(_dir, GameTexts.English);

        var towns = EncyclopediaGuides.RichestTowns(data.Settlements);
        Assert.Equal(["town_EN2", "town_EN1", "town_S1"], towns.Select(t => t.Id));

        // Sunucudan gelen canlı refah XML'deki değerin önüne geçer.
        var live = EncyclopediaGuides.RichestTowns(data.Settlements, new Dictionary<string, double> { ["town_S1"] = 9000 });
        Assert.Equal("town_S1", live[0].Id);

        var castles = EncyclopediaGuides.BestCastleSpots(data.Settlements);
        Assert.Equal("castle_EN1", castles[0].Id);   // iki kasabanın arasında
        Assert.StartsWith("Epicrotea, Ostican", castles[0].Detail);

        var clans = EncyclopediaGuides.StrongestClans(data.Clans, data.Lords);
        var top = Assert.Single(clans);   // haydut ve küçük grup hariç
        Assert.Equal("2", top.Detail);

        var t2 = EncyclopediaGuides.StrongestTroops(data.Troops, tier: 2);
        Assert.Equal("imperial_infantryman", Assert.Single(t2).Id);
    }

    [Fact]
    public void en_iyi_esyalar_eklentinin_degerleriyle_siralanir()
    {
        var items = new[]
        {
            new ServerItem("a", "Sword A", "OneHandedWeapon", "", 100, 2, "", false, false, false, "OneHandedSword", 1),
            new ServerItem("b", "Sword B", "OneHandedWeapon", "", 900, 4, "", false, false, false, "OneHandedSword", 1),
            new ServerItem("c", "No stats", "OneHandedWeapon", "", 50, 1, "", false, false, false, "OneHandedSword", 1),
        };
        var stats = new Dictionary<string, ItemStats>
        {
            ["a"] = new("a", 60, 20, 90, 90, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            ["b"] = new("b", 75, 30, 85, 85, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        };

        var best = EncyclopediaGuides.BestItems(items, stats, i => i.Group == ItemGroup.Sword,
            s => Math.Max(s.Swing, s.Thrust), s => s.Swing + " / " + s.Thrust);

        Assert.Equal(["b", "a"], best.Select(r => r.Id));
        Assert.Equal("75 / 30", best[0].Detail);
    }

    /// <summary>Bu bilgisayarda oyun kuruluysa gerçek dosyalarla; değilse hiçbir şey yapmaz.</summary>
    [Fact]
    public void kurulu_oyunun_turkcesi_okunur()
    {
        const string modules = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules";
        if (!Directory.Exists(modules)) return;

        var tr = GameTexts.Load(modules, "TR");
        Assert.True(tr.Count > 1000, "çeviri sayısı: " + tr.Count);
        var data = EncyclopediaData.Load(modules, tr);
        Assert.StartsWith("Üçe bölünmüş", data.Lords.Single(l => l.Id == "lord_1_1").Text);
    }
}
