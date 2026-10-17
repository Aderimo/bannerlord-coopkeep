using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Encyclopedia;
using CoopKeep.Core.Images;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Ansiklopedinin bölümleri.</summary>
public enum EncSection { Kingdoms, Clans, Lords, Settlements, Troops, Items, Guides }

/// <summary>Kayıttaki bir bilgi: "Refah · 5.100".</summary>
public sealed record EncFact(string Label, string Value);

/// <summary>Başka bir kayda bağlantı.</summary>
public sealed record EncLink(EncSection Section, string Id, string Title, string? Sub = null);

/// <summary>Başlıklı bağlantı grubu: "Klanlar", "Köyler", "Terfi eder" …</summary>
public sealed record EncLinkGroup(string Title, IReadOnlyList<EncLink> Links)
{
    public bool HasLinks => Links.Count > 0;
}

/// <summary>Ansiklopedide bir kayıt (sayfa).</summary>
public sealed class EncEntry
{
    public required EncSection Section { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string Text { get; init; } = "";
    public GameImage? Image { get; init; }
    public IReadOnlyList<EncFact> Facts { get; init; } = [];
    public IReadOnlyList<EncLinkGroup> Groups { get; init; } = [];

    /// <summary>Aramada kullanılan metin (ad + alt başlık).</summary>
    public string SearchText => Title + " " + Subtitle;

    public bool HasText => Text.Length > 0;
    public bool HasImage => Image is not null;
}

/// <summary>
/// Ansiklopedi: oyunun kendi ansiklopedi metinleri ve verileriyle krallıklar, klanlar, lordlar,
/// yerleşimler, askerler, eşyalar ve verilerden hesaplanan rehberler.
/// </summary>
/// <remarks>
/// <para>
/// Veriler kullanıcının oyun kurulumundan okunur (<see cref="EncyclopediaData"/>); Türkçe
/// arayüzde metinler oyunun kendi Türkçe çevirisiyle gelir (<see cref="GameTexts"/>).
/// </para>
/// <para>
/// Sunucu açıksa canlı bilgiler eklenir: yerleşimin güncel sahibi (harita), refah ve
/// güvenlik (<c>coopkeep.settlement_stats</c>), lordun nerede olduğu (harita), eşya değerleri
/// (<c>coopkeep.item_stats</c>, önbelleğe alınır).
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private EncyclopediaData? _enc;
    private string? _encLanguage;
    private readonly Dictionary<EncSection, IReadOnlyList<EncEntry>> _encEntries = [];
    private readonly Stack<EncEntry> _encHistory = new();
    private Dictionary<string, SettlementStats> _liveSettlementStats = [];
    private Dictionary<string, ItemStats> _itemStats = [];

    [ObservableProperty] private EncSection _encSection = EncSection.Kingdoms;
    [ObservableProperty] private string _encQuery = "";
    [ObservableProperty] private EncEntry? _encSelected;
    [ObservableProperty] private bool _encLoading;

    public ObservableCollection<EncEntry> EncResults { get; } = [];

    public bool HasEncSelected => EncSelected is not null;
    public bool CanEncBack => _encHistory.Count > 0;
    public string EncCountText => string.Format(L["enc.count"], EncResults.Count);

    public bool EncKingdoms { get => EncSection == EncSection.Kingdoms; set { if (value) EncSection = EncSection.Kingdoms; } }
    public bool EncClans { get => EncSection == EncSection.Clans; set { if (value) EncSection = EncSection.Clans; } }
    public bool EncLords { get => EncSection == EncSection.Lords; set { if (value) EncSection = EncSection.Lords; } }
    public bool EncSettlements { get => EncSection == EncSection.Settlements; set { if (value) EncSection = EncSection.Settlements; } }
    public bool EncTroops { get => EncSection == EncSection.Troops; set { if (value) EncSection = EncSection.Troops; } }
    public bool EncItems { get => EncSection == EncSection.Items; set { if (value) EncSection = EncSection.Items; } }
    public bool EncGuides { get => EncSection == EncSection.Guides; set { if (value) EncSection = EncSection.Guides; } }

    private string ItemStatsPath => Path.Combine(ServerToolsCacheDir, "item-stats.json");

    partial void OnEncSectionChanged(EncSection value)
    {
        foreach (var n in new[] { nameof(EncKingdoms), nameof(EncClans), nameof(EncLords), nameof(EncSettlements), nameof(EncTroops), nameof(EncItems), nameof(EncGuides) })
            OnPropertyChanged(n);
        EncQuery = "";
        FilterEnc();
        EncSelected = EncResults.FirstOrDefault();
    }

    partial void OnEncQueryChanged(string value) => FilterEnc();

    partial void OnEncSelectedChanged(EncEntry? value) => OnPropertyChanged(nameof(HasEncSelected));

    /// <summary>Başka sayfadan istenen kayıt; veri yüklenince açılır.</summary>
    private EncLink? _pendingEncLink;

    private Task? _encLoad;

    private async void OnEncyclopediaOpened()
    {
        await LoadEncyclopediaAsync();
        if (_pendingEncLink is { } link)
        {
            _pendingEncLink = null;
            OpenEncLink(link);
        }

        if (IsServing && ServerToolsAvailable) _ = RefreshLiveSettlementStatsAsync();
    }

    /// <summary>Aynı anda iki kez çağrılırsa ikincisi birincinin bitmesini bekler.</summary>
    private Task LoadEncyclopediaAsync()
    {
        var language = L.IsTurkish ? "TR" : "EN";
        if (_enc is not null && _encLanguage == language) return Task.CompletedTask;
        if (_encLoad is { IsCompleted: false } running) return running;
        return _encLoad = LoadEncyclopediaCoreAsync(language);
    }

    private async Task LoadEncyclopediaCoreAsync(string language)
    {
        if (_game is null) return;

        EncLoading = true;
        try
        {
            var modules = _game.ModulesDirectory;
            _enc = await Task.Run(() =>
                EncyclopediaData.Load(modules, language == "TR" ? GameTexts.Load(modules, "TR") : GameTexts.English));
            _encLanguage = language;
            _encEntries.Clear();
            LoadItemStatsCache();
            LoadCheatCatalogs();
            if (_itemCards.Count == 0) LoadItemCatalog();
            if (MapSettlements.Count == 0) LoadMap();

            FilterEnc();
            EncSelected ??= EncResults.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
        finally
        {
            EncLoading = false;
        }
    }

    private void FilterEnc()
    {
        EncResults.Clear();
        if (_enc is null) { OnPropertyChanged(nameof(EncCountText)); return; }

        var q = EncQuery.Trim();
        foreach (var e in EntriesFor(EncSection)
                     .Where(e => q.Length == 0 || e.SearchText.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                     .Take(400))
            EncResults.Add(e);
        OnPropertyChanged(nameof(EncCountText));
    }

    private IReadOnlyList<EncEntry> EntriesFor(EncSection section)
    {
        if (_encEntries.TryGetValue(section, out var cached)) return cached;
        var list = section switch
        {
            EncSection.Kingdoms => _enc!.Kingdoms.Select(KingdomEntry).ToList(),
            EncSection.Clans => _enc!.Clans.Where(c => !c.IsBandit && c.Name.Length > 0).OrderByDescending(c => c.Tier).ThenBy(c => c.Name).Select(ClanEntry).ToList(),
            EncSection.Lords => _enc!.Lords.Where(l => l.Name.Length > 0).OrderBy(l => l.Name).Select(LordEntry).ToList(),
            EncSection.Settlements => _enc!.Settlements.Where(s => s.Kind != SettlementKind.Hideout && s.Name.Length > 0)
                .OrderBy(s => s.Kind).ThenBy(s => s.Name).Select(SettlementEntry).ToList(),
            EncSection.Troops => _enc!.Troops.Where(t => t.Name.Length > 0).OrderBy(t => t.Culture).ThenBy(t => t.Tier).ThenBy(t => t.Name)
                .Select(TroopEntry).ToList(),
            EncSection.Items => _itemCards.OrderBy(c => c.Item.Group).ThenByDescending(c => c.Item.Tier).ThenBy(c => c.Name)
                .Select(ItemEntry).ToList(),
            _ => GuideEntries(),
        };
        _encEntries[section] = list;
        return list;
    }

    // --- gezinme ------------------------------------------------------------

    [RelayCommand]
    private void OpenEncLink(EncLink? link)
    {
        if (link is null || _enc is null) return;
        var entry = EntriesFor(link.Section).FirstOrDefault(e => e.Id == link.Id);
        if (entry is null) return;

        if (EncSelected is not null) _encHistory.Push(EncSelected);
        if (EncSection != link.Section)
        {
            EncSection = link.Section;   // listeyi doldurur ve ilkini seçer
        }

        EncSelected = entry;
        OnPropertyChanged(nameof(CanEncBack));
    }

    [RelayCommand]
    private void EncBack()
    {
        if (_encHistory.Count == 0) return;
        var entry = _encHistory.Pop();
        if (EncSection != entry.Section) EncSection = entry.Section;
        EncSelected = entry;
        OnPropertyChanged(nameof(CanEncBack));
    }

    /// <summary>Başka sayfalardan: "Ansiklopedide aç".</summary>
    public void OpenInEncyclopedia(EncSection section, string id)
    {
        var link = new EncLink(section, id, "");
        if (CurrentPage == AppPage.Encyclopedia && _enc is not null) { OpenEncLink(link); return; }

        _pendingEncLink = link;
        CurrentPage = AppPage.Encyclopedia;   // OnEncyclopediaOpened yükler ve açar
    }

    [RelayCommand]
    private void OpenLordInEncyclopedia(MapLord? lord)
    {
        if (lord is not null) OpenInEncyclopedia(EncSection.Lords, lord.HeroId);
    }

    // --- kayıtlar -------------------------------------------------------------

    private string CultureText(string id) => _enc?.Cultures.GetValueOrDefault(id) is { Length: > 0 } n ? n : id;
    private string ClanName(string? id) => id is not null && _enc!.Clans.FirstOrDefault(c => c.Id == id) is { } c ? c.Name : id ?? "—";
    private string LordName(string? id) => id is not null && _enc!.Lords.FirstOrDefault(l => l.Id == id) is { } l ? l.Name : id ?? "—";
    private string KingdomName(string? id) => id is not null && _enc!.Kingdoms.FirstOrDefault(k => k.Id == id) is { } k ? k.Name : id ?? "—";
    private string SettlementTitle(string? id) => id is not null && _enc!.Settlements.FirstOrDefault(s => s.Id == id) is { } s ? s.Name : id ?? "—";

    /// <summary>Yerleşimin şu anki sahibi: harita açıldıysa sunucudan, yoksa oyunun başındaki.</summary>
    private string? CurrentOwner(EncSettlement s) =>
        MapSettlements.FirstOrDefault(m => m.Id == s.Id)?.OwnerClanId
        ?? (s.BoundTo is { } b ? _enc!.Settlements.FirstOrDefault(x => x.Id == b) is { } parent ? CurrentOwner(parent) : null : s.OwnerClanId);

    private static string N(double v) => v.ToString("N0", CultureInfo.CurrentCulture);

    private EncLink Link(EncSection section, string id, string title, string? sub = null) => new(section, id, title, sub);

    private EncEntry KingdomEntry(EncKingdom k)
    {
        var clans = _enc!.Clans.Where(c => c.KingdomId == k.Id && !c.IsBandit).OrderByDescending(c => c.Tier).ToList();
        var clanIds = clans.Select(c => c.Id).ToHashSet();
        var fiefs = _enc.Settlements.Where(s => s.Kind is SettlementKind.Town or SettlementKind.Castle && CurrentOwner(s) is { } o && clanIds.Contains(o)).ToList();
        var lords = _enc.Lords.Count(l => l.ClanId is { } c && clanIds.Contains(c));

        return new EncEntry
        {
            Section = EncSection.Kingdoms, Id = k.Id, Title = k.Name, Subtitle = CultureText(k.Culture), Text = k.Text,
            Image = k.RulerId is null ? null : GameImages.Get(GameImageKind.Lord, LordName(k.RulerId), k.RulerId),
            Facts =
            [
                new(L["enc.ruler"], LordName(k.RulerId)), new(L["enc.culture"], CultureText(k.Culture)),
                new(L["enc.clans"], clans.Count.ToString()), new(L["enc.lords"], lords.ToString()),
                new(L["enc.towns"], fiefs.Count(s => s.Kind == SettlementKind.Town).ToString()),
                new(L["enc.castles"], fiefs.Count(s => s.Kind == SettlementKind.Castle).ToString()),
            ],
            Groups =
            [
                new(L["enc.ruler"], k.RulerId is null ? [] : [Link(EncSection.Lords, k.RulerId, LordName(k.RulerId))]),
                new(L["enc.clans"], clans.Select(c => Link(EncSection.Clans, c.Id, c.Name, string.Format(L["enc.tierN"], c.Tier))).ToList()),
                new(L["enc.fiefs"], fiefs.OrderBy(s => s.Kind).Select(s => Link(EncSection.Settlements, s.Id, s.Name, KindText(s.Kind))).ToList()),
            ],
        };
    }

    private EncEntry ClanEntry(EncClan c)
    {
        var members = _enc!.Lords.Where(l => l.ClanId == c.Id).ToList();
        var fiefs = _enc.Settlements.Where(s => s.Kind is SettlementKind.Town or SettlementKind.Castle && CurrentOwner(s) == c.Id).ToList();
        return new EncEntry
        {
            Section = EncSection.Clans, Id = c.Id, Title = c.Name,
            Subtitle = c.IsMinor ? L["enc.minorFaction"] : c.KingdomId is null ? CultureText(c.Culture) : KingdomName(c.KingdomId),
            Text = c.Text,
            Image = GameImages.Get(GameImageKind.ClanBanner, c.Name, c.Id),
            Facts =
            [
                new(L["enc.tier"], c.Tier.ToString()), new(L["enc.kingdom"], c.KingdomId is null ? "—" : KingdomName(c.KingdomId)),
                new(L["enc.leader"], LordName(c.LeaderId)), new(L["enc.culture"], CultureText(c.Culture)),
                new(L["enc.lords"], members.Count.ToString()), new(L["enc.fiefs"], fiefs.Count.ToString()),
            ],
            Groups =
            [
                new(L["enc.kingdom"], c.KingdomId is null ? [] : [Link(EncSection.Kingdoms, c.KingdomId, KingdomName(c.KingdomId))]),
                new(L["enc.members"], members.Select(l => Link(EncSection.Lords, l.Id, l.Name, l.Id == c.LeaderId ? L["enc.leader"] : null)).ToList()),
                new(L["enc.fiefs"], fiefs.Select(s => Link(EncSection.Settlements, s.Id, s.Name, KindText(s.Kind))).ToList()),
                new(L["enc.home"], c.HomeSettlementId is null ? [] : [Link(EncSection.Settlements, c.HomeSettlementId, SettlementTitle(c.HomeSettlementId))]),
            ],
        };
    }

    private EncEntry LordEntry(EncLord l)
    {
        var clan = _enc!.Clans.FirstOrDefault(c => c.Id == l.ClanId);
        var facts = new List<EncFact>
        {
            new(L["enc.clan"], clan?.Name ?? "—"), new(L["enc.kingdom"], clan?.KingdomId is { } k ? KingdomName(k) : "—"),
            new(L["profile.age"], l.Age.ToString()), new(L["enc.culture"], CultureText(l.Culture)),
        };

        // Canlı: haritadaki yeri ve ordusu.
        if (MapLords.FirstOrDefault(m => m.HeroId == l.Id) is { } live && (live.Located || live.Where.Length > 0))
        {
            facts.Add(new(L["enc.now"], string.Join(" · ", new[] { live.Activity, live.Where }.Where(s => s.Length > 0))));
            if (live.Troops > 0) facts.Add(new(L["profile.tab.army"], live.TroopsText));
        }

        facts.AddRange(l.Traits.Where(t => t.Value != 0).OrderByDescending(t => Math.Abs(t.Value))
            .Select(t => new EncFact(L["trait." + t.Key] is var name && name != "trait." + t.Key ? name : t.Key, t.Value > 0 ? "+" + t.Value : t.Value.ToString())));

        var family = new List<EncLink>();
        if (l.SpouseId is { } sp) family.Add(Link(EncSection.Lords, sp, LordName(sp), L["enc.spouse"]));
        if (l.FatherId is { } f) family.Add(Link(EncSection.Lords, f, LordName(f), L["enc.father"]));
        if (l.MotherId is { } m) family.Add(Link(EncSection.Lords, m, LordName(m), L["enc.mother"]));
        family.AddRange(_enc.Lords.Where(x => x.FatherId == l.Id || x.MotherId == l.Id).Select(x => Link(EncSection.Lords, x.Id, x.Name, L["enc.child"])));

        return new EncEntry
        {
            Section = EncSection.Lords, Id = l.Id, Title = l.Name,
            Subtitle = clan is null ? CultureText(l.Culture) : clan.Name, Text = l.Text,
            Image = GameImages.Get(GameImageKind.Lord, l.Name, l.Id),
            Facts = facts,
            Groups =
            [
                new(L["enc.clan"], clan is null ? [] : [Link(EncSection.Clans, clan.Id, clan.Name)]),
                new(L["enc.family"], family.Where(x => _enc.Lords.Any(y => y.Id == x.Id)).ToList()),
            ],
        };
    }

    private string KindText(SettlementKind kind) => L["enc.kind." + kind];

    private EncEntry SettlementEntry(EncSettlement s)
    {
        var owner = CurrentOwner(s);
        var clan = _enc!.Clans.FirstOrDefault(c => c.Id == owner);
        var facts = new List<EncFact>
        {
            new(L["enc.kindLabel"], KindText(s.Kind)), new(L["enc.culture"], CultureText(s.Culture)),
            new(L["enc.owner"], clan?.Name ?? "—"), new(L["enc.kingdom"], clan?.KingdomId is { } k ? KingdomName(k) : "—"),
        };

        if (_liveSettlementStats.TryGetValue(s.Id, out var live))
        {
            facts.Add(new(L["enc.prosperity"], N(live.Prosperity) + " ●"));
            facts.Add(new(L["enc.security"], N(live.Security)));
            facts.Add(new(L["enc.loyalty"], N(live.Loyalty)));
            facts.Add(new(L["enc.militia"], N(live.Militia)));
        }
        else if (s.Prosperity > 0) facts.Add(new(L["enc.prosperity"], N(s.Prosperity)));

        if (s.Fortifications > 0) facts.Add(new(L["enc.walls"], s.Fortifications.ToString()));
        if (s.Hearth > 0) facts.Add(new(L["enc.hearth"], N(s.Hearth)));
        if (s.Produces is { } p) facts.Add(new(L["enc.produces"], ProduceText(p)));

        var villages = _enc.Settlements.Where(v => v.BoundTo == s.Id).ToList();
        return new EncEntry
        {
            Section = EncSection.Settlements, Id = s.Id, Title = s.Name,
            Subtitle = KindText(s.Kind) + (clan is null ? "" : " · " + clan.Name), Text = s.Text,
            Image = s.Kind == SettlementKind.Village ? null : GameImages.Get(GameImageKind.Settlement, s.Name),
            Facts = facts,
            Groups =
            [
                new(L["enc.owner"], clan is null ? [] : [Link(EncSection.Clans, clan.Id, clan.Name)]),
                new(L["enc.villages"], villages.Select(v => Link(EncSection.Settlements, v.Id, v.Name, v.Produces is { } pr ? ProduceText(pr) : null)).ToList()),
                new(L["enc.boundTo"], s.BoundTo is null ? [] : [Link(EncSection.Settlements, s.BoundTo, SettlementTitle(s.BoundTo))]),
            ],
        };
    }

    /// <summary><c>silk_plant</c> → "Silk plant".</summary>
    private static string ProduceText(string villageType)
    {
        var t = villageType.Replace('_', ' ');
        return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
    }

    private EncEntry TroopEntry(EncTroop t)
    {
        var from = _enc!.Troops.Where(x => x.UpgradeTargets.Contains(t.Id)).ToList();
        var gear = t.Equipment.Distinct().Select(id => (id, card: _itemCardsById.GetValueOrDefault(id))).ToList();
        return new EncEntry
        {
            Section = EncSection.Troops, Id = t.Id, Title = t.Name,
            Subtitle = CultureText(t.Culture) + " · " + new string('★', Math.Clamp(t.Tier, 1, 6)),
            Image = GameImages.Get(GameImageKind.Troop, t.Name, t.Id),
            Facts = new List<EncFact>
            {
                new(L["enc.tier"], t.Tier.ToString()), new(L["enc.level"], t.Level.ToString()),
                new(L["enc.class"], L["enc.class." + t.Class] is var c && c != "enc.class." + t.Class ? c : t.Class),
                new(L["enc.culture"], CultureText(t.Culture)),
            }.Concat(t.Skills.OrderByDescending(k => k.Value).Select(k => new EncFact(L["skill." + k.Key], k.Value.ToString()))).ToList(),
            Groups =
            [
                new(L["enc.upgradesTo"], t.UpgradeTargets.Select(id => _enc.Troops.FirstOrDefault(x => x.Id == id)).OfType<EncTroop>()
                    .Select(x => Link(EncSection.Troops, x.Id, x.Name, new string('★', Math.Clamp(x.Tier, 1, 6)))).ToList()),
                new(L["enc.upgradesFrom"], from.Select(x => Link(EncSection.Troops, x.Id, x.Name, new string('★', Math.Clamp(x.Tier, 1, 6)))).ToList()),
                new(L["enc.equipment"], gear.Select(g => Link(EncSection.Items, g.id, g.card?.Name ?? g.id)).ToList()),
            ],
        };
    }

    private EncEntry ItemEntry(ItemCard card)
    {
        var i = card.Item;
        var facts = new List<EncFact>
        {
            new(L["enc.group"], card.GroupName), new(L["enc.tier"], i.Tier.ToString()),
            new(L["enc.value"], N(i.Value)), new(L["enc.weight"], i.Weight.ToString("0.##", CultureInfo.CurrentCulture)),
        };
        if (card.Price is { } p) facts.Add(new(L["enc.prices"], card.PriceRangeText + $"  ({p.MinTown} – {p.MaxTown})"));
        if (i.Culture.Length > 0) facts.Add(new(L["enc.culture"], CultureText(i.Culture)));

        if (_itemStats.TryGetValue(i.Id, out var s))
        {
            if (s.Swing > 0) facts.Add(new(L["enc.swing"], s.Swing.ToString()));
            if (s.Thrust > 0) facts.Add(new(L["enc.thrust"], s.Thrust.ToString()));
            if (s.SwingSpeed > 0) facts.Add(new(L["enc.speed"], s.SwingSpeed + (s.ThrustSpeed > 0 ? " / " + s.ThrustSpeed : "")));
            if (s.Length > 0) facts.Add(new(L["enc.length"], s.Length.ToString()));
            if (s.Accuracy > 0) facts.Add(new(L["enc.accuracy"], s.Accuracy.ToString()));
            if (s.HeadArmor > 0) facts.Add(new(L["slot.Head"], s.HeadArmor.ToString()));
            if (s.BodyArmor > 0) facts.Add(new(L["slot.Body"], s.BodyArmor.ToString()));
            if (s.ArmArmor > 0) facts.Add(new(L["slot.Gloves"], s.ArmArmor.ToString()));
            if (s.LegArmor > 0) facts.Add(new(L["slot.Leg"], s.LegArmor.ToString()));
            if (s.HorseSpeed > 0) facts.Add(new(L["enc.horseSpeed"], s.HorseSpeed.ToString()));
            if (s.Maneuver > 0) facts.Add(new(L["enc.maneuver"], s.Maneuver.ToString()));
            if (s.Charge > 0) facts.Add(new(L["enc.charge"], s.Charge.ToString()));
            if (s.HitPoints > 0) facts.Add(new(L["enc.hp"], s.HitPoints.ToString()));
        }

        var users = _enc!.Troops.Where(t => t.Equipment.Contains(i.Id)).Take(30).ToList();
        return new EncEntry
        {
            Section = EncSection.Items, Id = i.Id, Title = card.Name,
            Subtitle = card.GroupName + (card.TierText.Length > 0 ? " · " + card.TierText : ""),
            Image = card.Image,
            Facts = facts,
            Groups = [new(L["enc.usedBy"], users.Select(t => Link(EncSection.Troops, t.Id, t.Name)).ToList())],
        };
    }

    // --- rehberler --------------------------------------------------------------

    private List<EncEntry> GuideEntries()
    {
        var enc = _enc!;
        var list = new List<EncEntry>();

        list.Add(new EncEntry
        {
            Section = EncSection.Guides, Id = "troops", Title = L["guide.troops"], Text = L["guide.troops.text"],
            Groups = Enumerable.Range(1, 6).Select(tier => new EncLinkGroup(string.Format(L["enc.tierN"], tier),
                EncyclopediaGuides.StrongestTroops(enc.Troops, tier, 8)
                    .Select(r => Link(EncSection.Troops, r.Id, r.Name, N(r.Score))).ToList())).ToList(),
        });

        var live = _liveSettlementStats.Count > 0 ? _liveSettlementStats.ToDictionary(k => k.Key, k => k.Value.Prosperity) : null;
        list.Add(new EncEntry
        {
            Section = EncSection.Guides, Id = "towns", Title = L["guide.towns"],
            Text = live is null ? L["guide.towns.text"] : L["guide.towns.live"],
            Groups =
            [
                new(L["enc.prosperity"], EncyclopediaGuides.RichestTowns(enc.Settlements, live)
                    .Select(r => Link(EncSection.Settlements, r.Id, r.Name, N(r.Score))).ToList()),
                new(L["enc.security"], _liveSettlementStats.Values.Where(s => s.Id.StartsWith("town", StringComparison.Ordinal))
                    .OrderByDescending(s => s.Security).Take(10)
                    .Select(s => Link(EncSection.Settlements, s.Id, SettlementTitle(s.Id), N(s.Security))).ToList()),
            ],
        });

        list.Add(new EncEntry
        {
            Section = EncSection.Guides, Id = "clans", Title = L["guide.clans"], Text = L["guide.clans.text"],
            Groups = [new(L["enc.clans"], EncyclopediaGuides.StrongestClans(enc.Clans, enc.Lords)
                .Select(r => Link(EncSection.Clans, r.Id, r.Name, string.Format(L["guide.clanRow"], r.Score, r.Detail))).ToList())],
        });

        list.Add(new EncEntry
        {
            Section = EncSection.Guides, Id = "castles", Title = L["guide.castles"], Text = L["guide.castles.text"],
            Groups = [new(L["enc.castles"], EncyclopediaGuides.BestCastleSpots(enc.Settlements)
                .Select(r => Link(EncSection.Settlements, r.Id, r.Name, r.Detail)).ToList())],
        });

        var items = _itemCards.Select(c => c.Item).ToList();
        string NoStats(string text) => _itemStats.Count == 0 ? L["guide.needStats"] : text;
        EncLinkGroup Best(string title, Func<ServerItem, bool> filter, Func<ItemStats, double> score, Func<ItemStats, string> detail) =>
            new(title, EncyclopediaGuides.BestItems(items, _itemStats, filter, score, detail, 8)
                .Select(r => Link(EncSection.Items, r.Id, r.Name, r.Detail)).ToList());

        list.Add(new EncEntry
        {
            Section = EncSection.Guides, Id = "weapons", Title = L["guide.weapons"], Text = NoStats(L["guide.weapons.text"]),
            Groups =
            [
                Best(L["itemgroup.Sword"], i => i.Group == ItemGroup.Sword, s => Math.Max(s.Swing, s.Thrust), s => $"{s.Swing} / {s.Thrust} · {s.SwingSpeed}"),
                Best(L["itemgroup.Axe"], i => i.Group == ItemGroup.Axe, s => s.Swing, s => $"{s.Swing} · {s.SwingSpeed}"),
                Best(L["itemgroup.Mace"], i => i.Group == ItemGroup.Mace, s => s.Swing, s => $"{s.Swing} · {s.SwingSpeed}"),
                Best(L["itemgroup.Polearm"], i => i.Group == ItemGroup.Polearm, s => Math.Max(s.Swing, s.Thrust), s => $"{s.Swing} / {s.Thrust} · {s.Length}"),
                Best(L["itemgroup.Bow"], i => i.Group == ItemGroup.Bow, s => s.Thrust + s.Accuracy / 4.0, s => $"{s.Thrust} · {s.Accuracy}"),
                Best(L["itemgroup.Crossbow"], i => i.Group == ItemGroup.Crossbow, s => s.Thrust + s.Accuracy / 4.0, s => $"{s.Thrust} · {s.Accuracy}"),
                Best(L["itemgroup.Shield"], i => i.Group == ItemGroup.Shield, s => s.HitPoints, s => $"{s.HitPoints}"),
            ],
        });

        list.Add(new EncEntry
        {
            Section = EncSection.Guides, Id = "armor", Title = L["guide.armor"], Text = NoStats(L["guide.armor.text"]),
            Groups =
            [
                Best(L["itemgroup.Head"], i => i.Group == ItemGroup.Head, s => s.HeadArmor, s => s.HeadArmor.ToString()),
                Best(L["itemgroup.Body"], i => i.Group == ItemGroup.Body, s => s.TotalArmor, s => $"{s.BodyArmor} · {s.ArmArmor} · {s.LegArmor}"),
                Best(L["itemgroup.Legs"], i => i.Group == ItemGroup.Legs, s => s.LegArmor, s => s.LegArmor.ToString()),
                Best(L["itemgroup.Hands"], i => i.Group == ItemGroup.Hands, s => s.ArmArmor, s => s.ArmArmor.ToString()),
                Best(L["itemgroup.Cape"], i => i.Group == ItemGroup.Cape, s => s.TotalArmor, s => $"{s.BodyArmor} · {s.ArmArmor}"),
                Best(L["itemgroup.Horse"], i => i.Group == ItemGroup.Horse, s => s.HorseSpeed + s.Maneuver / 2.0, s => $"{s.HorseSpeed} · {s.Maneuver} · {s.Charge}"),
                Best(L["itemgroup.Harness"], i => i.Group == ItemGroup.Harness, s => s.TotalArmor, s => s.TotalArmor.ToString()),
            ],
        });

        return list;
    }

    // --- canlı veriler ve önbellek --------------------------------------------------

    private async Task RefreshLiveSettlementStatsAsync()
    {
        try
        {
            var stats = ServerToolsParser.ParseSettlementStats(await _supervisor.SendAndCollectAsync(AdminCommand.ToolsSettlementStats(),
                l => l.Trim() == ServerToolsParser.End, TimeSpan.FromSeconds(5)));
            if (stats.Count == 0) return;

            _liveSettlementStats = stats.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            _encEntries.Remove(EncSection.Settlements);
            _encEntries.Remove(EncSection.Guides);
            var selected = EncSelected;
            if (EncSection is EncSection.Settlements or EncSection.Guides) FilterEnc();
            if (selected is not null)
                EncSelected = EncResults.FirstOrDefault(e => e.Id == selected.Id && e.Section == selected.Section) ?? selected;
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
    }

    private void LoadItemStatsCache()
    {
        try
        {
            if (File.Exists(ItemStatsPath))
                _itemStats = (JsonSerializer.Deserialize<List<ItemStats>>(File.ReadAllText(ItemStatsPath)) ?? [])
                    .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception e) when (e is IOException or JsonException) { }
    }

    /// <summary>Eşya değerlerini sunucudan alıp önbelleğe yazar (eşya listesiyle birlikte, günde bir).</summary>
    private async Task RefreshItemStatsAsync()
    {
        var stats = ServerToolsParser.ParseItemStats(await _supervisor.SendAndCollectAsync(AdminCommand.ToolsItemStats(),
            l => l.Trim() == ServerToolsParser.End, TimeSpan.FromSeconds(20)));
        if (stats.Count == 0) return;

        _itemStats = stats.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        try
        {
            Directory.CreateDirectory(ServerToolsCacheDir);
            File.WriteAllText(ItemStatsPath, JsonSerializer.Serialize(stats));
        }
        catch (IOException) { }

        _encEntries.Remove(EncSection.Items);
        _encEntries.Remove(EncSection.Guides);
    }
}
