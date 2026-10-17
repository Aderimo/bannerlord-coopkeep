using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Protocol;

/// <summary>
/// v0.4'te kullanılmaya başlanan sunucu komutları: asker türü ver/al, kadro, konum,
/// sahiplik, klan bilgisi, listeler.
/// </summary>
/// <remarks>
/// Yanıtlar 2026-10-16'da Coop v0.1.6 sunucusunda <c>lord_1_1_party_1</c> üzerinde
/// alındı (<c>Fixtures/troops-clans.txt</c>, <c>Fixtures/lists.txt</c>).
/// </remarks>
public class GameStateParserTests
{
    private static readonly string[] Fikstur =
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "troops-clans.txt"));

    private static readonly string[] Listeler =
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lists.txt"));

    private static IReadOnlyList<string> Yanit(string komut, string[]? kaynak = null)
    {
        kaynak ??= Fikstur;
        var i = Array.FindIndex(kaynak, l => l == "[DedicatedServer] > " + komut);
        Assert.True(i >= 0, "fikstürde yok: " + komut);
        return kaynak.Skip(i + 1).TakeWhile(l => !l.StartsWith("[DedicatedServer] > ")).ToList();
    }

    [Fact]
    public void asker_turu_sayisi_ayarlanir_ve_dogrulanir()
    {
        Assert.Equal("coop.debug.mobile_party.set_troop_state lord_1_1_party_1 imperial_legionary true 25 0 0",
            AdminCommand.SetTroopState("lord_1_1_party_1", "imperial_legionary", 25));

        var yanit = Yanit("coop.debug.mobile_party.set_troop_state lord_1_1_party_1 imperial_legionary true 25 0 0");
        Assert.Equal(AdminOutcome.Success, AdminResponseParser.Classify(yanit));
        Assert.Equal(("imperial_legionary", 1, 25), GameStateParser.ParseTroopStateSet(yanit));
    }

    [Fact]
    public void sifir_asker_turu_kadrodan_cikarir()
    {
        Assert.Equal("coop.debug.mobile_party.set_troop_state p imperial_legionary false 0 0 0",
            AdminCommand.SetTroopState("p", "imperial_legionary", 0));

        var yanit = Yanit("coop.debug.mobile_party.set_troop_state lord_1_1_party_1 imperial_legionary false 0 0 0");
        Assert.Equal(("imperial_legionary", 5, 0), GameStateParser.ParseTroopStateSet(yanit));
    }

    [Fact]
    public void yarali_sayisi_toplam_sayiyi_gecemez()
        => Assert.EndsWith(" true 3 3 0", AdminCommand.SetTroopState("p", "t", 3, wounded: 10));

    [Fact]
    public void bilinmeyen_asker_bulunamadi_sayilir()
        => Assert.Equal(AdminOutcome.NotFound,
            AdminResponseParser.Classify(Yanit("coop.debug.mobile_party.set_troop_state lord_1_1_party_1 no_such_troop true 3 0 0")));

    [Fact]
    public void yaralilari_iyilestirme_dogrulanir()
        => Assert.Equal(AdminOutcome.Success,
            AdminResponseParser.Classify(Yanit("coop.debug.mobile_party.set_troop_wounded lord_1_1_party_1 imperial_legionary 0")));

    [Fact]
    public void kadro_okunur()
    {
        var kadro = GameStateParser.ParseRoster(Yanit("coop.debug.mobile_party.character_ids lord_1_1"));

        Assert.NotNull(kadro);
        Assert.Equal("Lucon", kadro.HeroName);
        Assert.Contains(kadro.Members, m => m is { TroopId: "lord_1_1", IsHero: true });
        Assert.Contains(kadro.Members, m => m is { TroopId: "imperial_recruit", Number: 46 });
        Assert.Equal(46 + 34 + 18 + 9 + 20 + 26 + 20 + 14 + 7 + 1 + 2 + 3, kadro.TroopCount);
        Assert.Empty(kadro.Prisoners);
    }

    [Fact]
    public void konum_okunur()
    {
        var konum = GameStateParser.ParsePosition(Yanit("coop.debug.mobile_party.position lord_1_1_party_1"));

        Assert.NotNull(konum);
        Assert.Equal(464.26944, konum.X, 4);
        Assert.Equal(469.2502, konum.Y, 4);
        Assert.True(konum.OnLand);
        Assert.Null(konum.SettlementId);
    }

    [Fact]
    public void lord_partisi_isinlanamaz()
        => Assert.Equal(AdminOutcome.Rejected,
            AdminResponseParser.Classify(Yanit("coop.debug.mobile_party.move_to_settlement lord_1_1_party_1 town_EN1 true")));

    [Fact]
    public void yerlesim_sahibi_okunur()
        => Assert.Equal(("town_EN1", "clan_empire_north_2"),
            GameStateParser.ParseOwnerState(Yanit("coop.debug.settlements.owner_state town_EN1")));

    [Fact]
    public void klan_bilgisi_okunur()
    {
        var klan = GameStateParser.ParseClanInfo(Yanit("coop.debug.clan.info clan_empire_north_1"));

        Assert.NotNull(klan);
        Assert.Equal("Osticos", klan.Name);
        Assert.Equal("Northern Empire", klan.Kingdom);
        Assert.Equal(465, klan.Influence);
        Assert.Equal(6408, klan.Renown);
        Assert.Contains("Zerosica", klan.Members);
    }

    [Fact]
    public void nufuz_eksi_degerle_alinabilir()
    {
        Assert.Equal("coop.debug.clan.add_influence clan_x -50", AdminCommand.AddInfluence("clan_x", -50));

        var yanit = Yanit("coop.debug.clan.add_influence clan_empire_north_1 -50");
        Assert.Equal(AdminOutcome.Success, AdminResponseParser.Classify(yanit));
        Assert.Equal(415, GameStateParser.ParseInfluenceApplied(yanit));
    }

    [Fact]
    public void esya_degeri_okunur()
        => Assert.Equal(10, GameStateParser.ParseItemValue(Yanit("coop.debug.item_object.data grain")));

    [Fact]
    public void kahraman_klan_ve_krallik_listeleri_okunur()
    {
        var heroes = GameStateParser.ParseHeroList(Yanit("coop.debug.hero.list", Listeler));
        Assert.Contains(heroes, h => h is { StringId: "lord_1_1", Name: "Lucon" });

        var clans = GameStateParser.ParseClanList(Yanit("coop.debug.clan.list", Listeler));
        Assert.Contains(clans, c => c is { Id: "clan_sturgia_2", Name: "Kuloving" });

        var kingdoms = GameStateParser.ParseKingdomList(Yanit("coop.debug.kingdom.list", Listeler));
        Assert.Equal(8, kingdoms.Count);
        Assert.Contains(kingdoms, k => k is { Id: "empire_w", Name: "Western Empire" });
    }

    [Fact]
    public void yeni_komutlar_allow_listte()
    {
        Assert.True(AdminCommand.IsAllowed(AdminCommand.SetTroopState("p", "t", 1)));
        Assert.True(AdminCommand.IsAllowed(AdminCommand.PartyRoster("h")));
        Assert.True(AdminCommand.IsAllowed(AdminCommand.MoveToSettlement("p", "town_EN1")));
        Assert.True(AdminCommand.IsAllowed(AdminCommand.SettlementOwner("town_EN1")));
        Assert.True(AdminCommand.IsAllowed(AdminCommand.AddInfluence("c", -1)));
    }

    [Fact]
    public void kimlik_bosluk_iceremez()
        => Assert.Throws<ArgumentException>(() => AdminCommand.SetTroopState("p", "imperial legionary", 1));
}
