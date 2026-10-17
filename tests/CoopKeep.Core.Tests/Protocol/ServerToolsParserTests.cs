using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Protocol;

/// <summary>
/// Sunucu eklentisi v2'nin okuma komutları: kahraman gelişimi, kuşanım, kadro, dünya.
/// </summary>
/// <remarks>Çıktılar test sunucusunda şablon dünyadan alındı (<c>Fixtures/server-tools-v2.txt</c>).</remarks>
public sealed class ServerToolsParserTests
{
    private static readonly string[] Fikstur =
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "server-tools-v2.txt"));

    private static IReadOnlyList<string> Bolum(string ad, int kacinci = 1)
    {
        var i = -1;
        for (var k = 0; k < kacinci; k++)
        {
            i = Array.IndexOf(Fikstur, "## " + ad, i + 1);
            Assert.True(i >= 0, "fikstürde yok: " + ad);
        }

        return Fikstur.Skip(i + 1).TakeWhile(l => !l.StartsWith("## ")).ToList();
    }

    [Fact]
    public void kahraman_gelisimi_okunur()
    {
        var h = ServerToolsParser.ParseHeroStats(Bolum("hero_stats"));

        Assert.NotNull(h);
        Assert.Equal("lord_1_1", h.Id);
        Assert.Equal("Lucon", h.Name);
        Assert.Equal(27, h.Level);
        Assert.Equal(2, h.UnspentAttributePoints);
        Assert.Equal(3, h.UnspentFocusPoints);
        Assert.Equal(160000, h.Gold);
        Assert.Equal(62.13, h.Age, 2);
        Assert.Equal(6, h.Attributes.Count);
        Assert.Equal(5, h.Attributes.Single(a => a.Id == "social").Value);
        Assert.Equal(18, h.Skills.Count);

        var liderlik = h.Skills.Single(s => s.Id == "Leadership");
        Assert.Equal(209, liderlik.Value);
        Assert.Equal(2, liderlik.Focus);
        Assert.Equal(5000, liderlik.XpProgress);
        Assert.Equal("social", liderlik.AttributeId);
        Assert.Equal("Smithing", h.Skills.Single(s => s.Id == "Crafting").Name);
    }

    [Fact]
    public void gelisim_degisikligi_okunur_ve_basarili_sayilir()
    {
        var satirlar = Bolum("dev");
        Assert.Equal(AdminOutcome.Success, AdminResponseParser.Classify(satirlar.Take(1).ToList()));

        var odak = ServerToolsParser.ParseDevChange(satirlar.Skip(1).Take(1));
        Assert.Equal(new DevChange("lord_1_1", "focus", "Leadership", 2, 3), odak);

        var son = ServerToolsParser.ParseDevChange(satirlar);
        Assert.Equal(new DevChange("lord_1_1", "skill", "Leadership", 210, 216), son);
    }

    [Fact]
    public void kusanim_yuva_adlariyla_okunur()
    {
        var eq = ServerToolsParser.ParseEquipment(Bolum("equipment"));

        Assert.Equal(18, eq.Count);
        Assert.Contains(eq, p => p is { Set: "battle", Slot: "Head", ItemId: "empire_battle_crown_north" });
        Assert.Contains(eq, p => p is { Set: "battle", Slot: "Banner", ItemId: "testudo_standard_t3" });
        Assert.Contains(eq, p => p is { Set: "civilian", Slot: "Body", Name: "Toga" });
    }

    [Fact]
    public void kadro_okunur()
    {
        var r = ServerToolsParser.ParseRoster(Bolum("roster"));

        Assert.NotNull(r);
        Assert.Equal("lord_1_1_party_1", r.PartyId);
        Assert.Equal(201, r.Total);
        Assert.Equal(200, r.TroopCount);
        Assert.True(r.Members[0].IsHero);
        Assert.Equal(46, r.Members.Single(m => m.Id == "imperial_recruit").Number);
        Assert.Empty(r.Prisoners);
    }

    [Fact]
    public void partiler_okunur()
    {
        var p = ServerToolsParser.ParseParties(Bolum("parties"));

        Assert.Equal(12, p.Count);
        var kusatan = p.Single(x => x.HeroId == "Player8");
        Assert.True(kusatan.Besieging);
        Assert.Equal("castle_ES1", kusatan.TargetSettlementId);
        var oyuncu = p[0];
        Assert.True(oyuncu.IsPlayer);
        Assert.Equal("town_ES3", oyuncu.SettlementId);
        Assert.Equal("", oyuncu.KingdomId);
        Assert.Equal(1, oyuncu.Wounded);

        var olek = p.Single(x => x.HeroId == "lord_2_3");
        Assert.Equal("sturgia", olek.KingdomId);
        Assert.Equal(426.46, olek.X, 2);
        Assert.Equal(131, olek.Troops);
        Assert.Equal("GoToSettlement", olek.Behavior);
        Assert.False(olek.InArmy);
    }

    [Fact]
    public void sahipler_yerlesim_durumu_ve_esya_degerleri_okunur()
    {
        var sahip = ServerToolsParser.ParseOwners(Bolum("owners"));
        Assert.Equal(4, sahip.Count);
        Assert.Equal("clan_sturgia_1", sahip["town_S1"]);

        var yer = ServerToolsParser.ParseSettlementStats(Bolum("settlement_stats"));
        Assert.Equal(4700, yer[0].Prosperity);
        Assert.Equal(302.5, yer[0].Militia);

        var esya = ServerToolsParser.ParseItemStats(Bolum("item_stats"));
        var zirh = esya.Single(e => e.Id == "imperial_scale_armor");
        Assert.True(zirh.IsArmor);
        Assert.Equal(93, zirh.TotalArmor);
        var kilic = esya.Single(e => e.Id == "empire_sword_5_t4");
        Assert.True(kilic.IsWeapon);
        Assert.Equal(72, kilic.Swing);
        Assert.True(esya.Single(e => e.Id == "khuzait_horse").IsMount);
        Assert.True(ServerToolsParser.IsComplete(Bolum("item_stats")));
    }

    [Fact]
    public void komutlar_dogru_yazilir_ve_izinlidir()
    {
        Assert.Equal("coopkeep.add_skill lord_1_1 Leadership -5", AdminCommand.ToolsAddSkill("lord_1_1", "Leadership", -5));
        Assert.Equal("coopkeep.add_skill lord_1_1 Leadership 330", AdminCommand.ToolsAddSkill("lord_1_1", "Leadership", 999));
        Assert.Equal("coopkeep.add_focus lord_1_1 Bow 1", AdminCommand.ToolsAddFocus("lord_1_1", "Bow", 1));
        Assert.Equal("coopkeep.add_attribute lord_1_1 social 1", AdminCommand.ToolsAddAttribute("lord_1_1", "social", 1));
        Assert.Equal("coopkeep.add_unspent lord_1_1 focus 3", AdminCommand.ToolsAddUnspent("lord_1_1", false, 3));
        Assert.Throws<ArgumentException>(() => AdminCommand.ToolsAddSkill("lord_1_1", "Bow; stop", 1));

        foreach (var c in new[]
                 {
                     AdminCommand.ToolsHeroStats("lord_1_1"), AdminCommand.ToolsHeroEquipment("lord_1_1"), AdminCommand.ToolsRoster("lord_1_1"),
                     AdminCommand.ToolsParties(), AdminCommand.ToolsOwners(), AdminCommand.ToolsSettlementStats(), AdminCommand.ToolsItemStats(),
                     AdminCommand.ToolsAddSkillXp("lord_1_1", "Bow", 100),
                 })
            Assert.True(AdminCommand.IsAllowed(c), c);
    }

    [Fact]
    public void kadro_komutu_kayit_kimligini_oyun_kimligine_cevirir()
    {
        // Coop'un character_ids komutu "Hero_lord_1_1" için "No hero … found" diyor.
        Assert.Equal("coop.debug.mobile_party.character_ids lord_1_1", AdminCommand.PartyRoster("Hero_lord_1_1"));
        Assert.Equal("coop.debug.mobile_party.character_ids Player2863", AdminCommand.PartyRoster("Hero_Player2863"));
        Assert.Equal("coop.debug.mobile_party.character_ids lord_1_1", AdminCommand.PartyRoster("lord_1_1"));
    }
}
