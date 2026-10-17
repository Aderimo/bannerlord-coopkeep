using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Images;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Map;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Haritanın yakınlığa bağlı görünümü: işaretlerin ekrandaki boyu ve hangi adların çizileceği.
/// </summary>
/// <remarks>
/// Haritadaki her işaret buna bağlanır (<c>{Binding Source={x:Static vm:MapView.Current}}</c>).
/// İşaretler harita ile birlikte büyüyüp küçülmesin diye yakınlığın tersiyle ölçeklenir;
/// uzaktayken köy ve kale adları çizilmez (kullanıcının 2026-10-17 ekran görüntüsündeki karmaşa).
/// </remarks>
public sealed partial class MapView : ObservableObject
{
    public static MapView Current { get; } = new();

    /// <summary>İşaretlerin ölçeği: ekranda yaklaşık sabit boy.</summary>
    [ObservableProperty] private double _markerScale = 2;

    /// <summary>Kale adları (orta yakınlık ve üstü).</summary>
    [ObservableProperty] private bool _castleNames;

    /// <summary>Köy adları (yalnızca yakında).</summary>
    [ObservableProperty] private bool _villageNames;

    /// <summary>Lordlar nokta yerine portreyle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LordDots))]
    private bool _lordPortraits;

    public bool LordDots => !LordPortraits;

    /// <summary>Lord adları.</summary>
    [ObservableProperty] private bool _lordNames;

    /// <summary>Krallık adlarının saydamlığı: uzakta belirgin, yakında silik.</summary>
    [ObservableProperty] private double _kingdomOpacity = 0.9;

    public void Apply(double zoom)
    {
        MarkerScale = Math.Clamp(1 / zoom, 0.45, 2.6);
        CastleNames = zoom >= 0.75;
        VillageNames = zoom >= 1.4;
        LordPortraits = zoom >= 0.9;
        LordNames = zoom >= 1.5;
        KingdomOpacity = zoom < 0.9 ? 0.92 : zoom < 1.6 ? 0.6 : 0.3;
    }
}

/// <summary>Lord kartı için canlı bilgiler (eklentinin <c>coopkeep.parties</c> çıktısından).</summary>
public sealed partial class MapLord
{
    [ObservableProperty] private string? _kingdomName;
    [ObservableProperty] private int _troops;
    [ObservableProperty] private int _wounded;
    [ObservableProperty] private string _activity = "";
    [ObservableProperty] private IBrush _color = Brushes.Gray;

    public string TroopsText => Wounded > 0 ? $"{Troops:N0} ({Wounded}✚)" : Troops.ToString("N0");

    partial void OnTroopsChanged(int value) => OnPropertyChanged(nameof(TroopsText));
    partial void OnWoundedChanged(int value) => OnPropertyChanged(nameof(TroopsText));
}

public partial class MainViewModel
{
    /// <summary>Haritadan ışınlanacak oyuncu; doluyken yerleşime tıklamak hedef seçer.</summary>
    [ObservableProperty] private HeroRow? _mapTeleportHero;

    /// <summary>Onay bekleyen ışınlama hedefi.</summary>
    [ObservableProperty] private MapSettlement? _mapTeleportTarget;

    [ObservableProperty] private string _mapTeleportSearch = "";

    public ObservableCollection<MapSettlement> MapTeleportResults { get; } = [];

    public bool IsMapTeleporting => MapTeleportHero is not null;
    public bool HasMapTeleportTarget => MapTeleportTarget is not null;

    public string MapTeleportText => MapTeleportHero is { } h
        ? MapTeleportTarget is { } t
            ? string.Format(L["map.tp.confirm"], h.Name, t.Name)
            : string.Format(L["map.tp.pick"], h.Name)
        : "";

    partial void OnMapZoomChanged(double value) => MapView.Current.Apply(value);

    partial void OnMapTeleportHeroChanged(HeroRow? value) => NotifyMapTeleport();

    partial void OnMapTeleportTargetChanged(MapSettlement? value) => NotifyMapTeleport();

    partial void OnMapTeleportSearchChanged(string value)
    {
        MapTeleportResults.Clear();
        var q = value.Trim();
        if (q.Length < 2) return;
        foreach (var s in MapSettlements.Where(s => s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                     .OrderBy(s => s.Kind).ThenBy(s => s.Name).Take(10))
            MapTeleportResults.Add(s);
    }

    private void NotifyMapTeleport()
    {
        OnPropertyChanged(nameof(IsMapTeleporting));
        OnPropertyChanged(nameof(HasMapTeleportTarget));
        OnPropertyChanged(nameof(MapTeleportText));
    }

    /// <summary>Oyuncu kartındaki "Işınla": hedef olarak bir yerleşime tıklanmasını bekler.</summary>
    [RelayCommand]
    private void BeginMapTeleport(MapPlayer? player)
    {
        if ((player ?? SelectedMapPlayer)?.Hero is not { } hero) return;
        MapTeleportHero = hero;
        MapTeleportTarget = null;
        MapTeleportSearch = "";
    }

    [RelayCommand]
    private void CancelMapTeleport()
    {
        MapTeleportHero = null;
        MapTeleportTarget = null;
        MapTeleportSearch = "";
    }

    /// <summary>Arama sonucundan ya da haritadan seçilen hedef.</summary>
    [RelayCommand]
    private void PickMapTeleportTarget(MapSettlement? settlement)
    {
        if (settlement is null || MapTeleportHero is null) return;
        MapTeleportTarget = settlement;
        MapTeleportSearch = "";
    }

    [RelayCommand]
    private async Task ConfirmMapTeleportAsync()
    {
        if (MapTeleportHero is not { } hero || MapTeleportTarget is not { } target) return;

        foreach (var h in Heroes) h.IsTarget = h == hero;
        NotifyTargets();
        await TeleportAsync(target.Info);
        CancelMapTeleport();
    }

    // --- eklentiyle tek çağrıda dünya ------------------------------------------

    /// <summary>
    /// Bütün lord ve oyuncu partilerinin konumunu tek komutla okur (<c>coopkeep.parties</c>).
    /// </summary>
    /// <returns>Okunabildiyse <c>true</c>; eklenti yoksa eski yol kullanılır.</returns>
    private async Task<bool> RefreshPartiesFromToolsAsync()
    {
        if (!ServerToolsAvailable) return false;

        var parties = ServerToolsParser.ParseParties(await _supervisor.SendAndCollectAsync(AdminCommand.ToolsParties(),
            l => l.Trim() == ServerToolsParser.End, TimeSpan.FromSeconds(4)));
        if (parties.Count == 0) return false;

        // Aynı kimliğe iki satır düşebilir (kopyalanmış dünya); ilki kullanılır.
        var byStringId = Heroes.GroupBy(h => AdminCommand.GameStringId(h.HeroId), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var rulers = _kingdoms.Values.Where(k => k.RulerId is not null).Select(k => k.RulerId!).ToHashSet(StringComparer.Ordinal);

        foreach (var p in parties)
        {
            if (p.IsPlayer || byStringId.ContainsKey(p.HeroId))
            {
                if (byStringId.TryGetValue(p.HeroId, out var hero)) UpsertMapPlayer(hero, p);
                continue;
            }

            var lord = MapLords.FirstOrDefault(l => l.HeroId == p.HeroId);
            if (lord is null)
            {
                lord = new MapLord(p.HeroId, p.HeroName, _clans.TryGetValue(p.ClanId, out var c) ? c.Name : null, rulers.Contains(p.HeroId));
                MapLords.Add(lord);
            }

            (lord.Left, lord.Top) = MapProjection.ToImage(p.X, p.Y);

            // Oyundaki gibi: yerleşimin içindeki parti haritada çizilmez (kasabaların üstü dolmasın);
            // kartında ve aramada nerede olduğu yine görünür.
            lord.Located = p.SettlementId.Length == 0;
            lord.Troops = p.Troops;
            lord.Wounded = p.Wounded;
            lord.KingdomName = _kingdoms.TryGetValue(p.KingdomId, out var k) ? k.Name : null;
            lord.Color = new SolidColorBrush(Color.FromUInt32(k?.Color2 ?? (_clans.TryGetValue(p.ClanId, out var cl) ? cl.Color : 0xFFB0A080)));
            lord.Activity = DescribeActivity(p);
            lord.Where = p.SettlementId.Length > 0 ? SettlementName(p.SettlementId) : NearestSettlementText(lord.Left, lord.Top);
        }

        // Partisi kalmayan (esir düşen, dağılan) lordlar haritadan kalkar.
        var alive = parties.Select(p => p.HeroId).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in MapLords.Where(l => !alive.Contains(l.HeroId)).ToList())
        {
            if (gone.IsRuler) { gone.Located = false; gone.Where = L["map.lordNoParty"]; }
            else MapLords.Remove(gone);
        }

        return true;
    }

    private void UpsertMapPlayer(HeroRow hero, WorldParty p)
    {
        var pin = MapPlayers.FirstOrDefault(m => m.Hero == hero);
        if (pin is null)
        {
            pin = new MapPlayer(hero.HeroId, hero);
            MapPlayers.Add(pin);
            OnPropertyChanged(nameof(HasMapPlayers));
        }

        var wasInBattle = pin.InBattle;
        var wasBesieging = pin.Besieging;
        pin.InBattle = p.InBattle;
        pin.Besieging = p.Besieging;
        if (p.InBattle && !wasInBattle) ShowToast(string.Format(L["map.alert.battle"], hero.Name), ToastKind.Warning);
        if (p.Besieging && !wasBesieging) ShowToast(string.Format(L["map.alert.siege"], hero.Name), ToastKind.Warning);

        pin.Name = hero.Name;
        pin.ClanLine = hero.ClanLine;
        pin.IsPriority = hero.IsPriority;
        pin.Online = hero.IsOnline;
        pin.Troops = p.Troops;
        pin.Banner = PlayerBanner(hero.KingdomName);
        MovePin(pin, p.X, p.Y);
        pin.Activity = DescribeActivity(p);
        UpdateDimming();
    }

    /// <summary>"Savaşta", "Makeb içinde", "Ortysia yolunda" …</summary>
    private string DescribeActivity(WorldParty p)
    {
        if (p.InBattle) return L["map.act.battle"];
        if (p.Besieging) return L["map.act.siege"];
        if (p.SettlementId.Length > 0) return string.Format(L["map.act.in"], SettlementName(p.SettlementId));
        if (p.InArmy) return L["map.act.army"];
        if (p.TargetSettlementId.Length > 0) return string.Format(L["map.act.goingTo"], SettlementName(p.TargetSettlementId));
        return L["map.act.travelling"];
    }

    /// <summary>Bütün sahipler tek komutla (<c>coopkeep.owners</c>).</summary>
    private async Task<bool> RefreshOwnersFromToolsAsync()
    {
        if (!ServerToolsAvailable) return false;

        var owners = ServerToolsParser.ParseOwners(await _supervisor.SendAndCollectAsync(AdminCommand.ToolsOwners(),
            l => l.Trim() == ServerToolsParser.End, TimeSpan.FromSeconds(4)));
        if (owners.Count == 0) return false;

        _ownersAt = DateTime.UtcNow;
        var firstRun = !_ownersChecked;
        var changed = false;

        foreach (var pin in MapSettlements.Where(s => s.IsFortified))
        {
            if (!owners.TryGetValue(pin.Id, out var clanId) || clanId == pin.OwnerClanId) continue;

            var before = pin.OwnerName ?? pin.OwnerClanId ?? "?";
            ApplyOwner(pin, clanId);
            foreach (var v in MapSettlements.Where(v => v.IsVillage && v.Info.BoundTo == pin.Id)) ApplyOwner(v, clanId);
            changed = true;

            if (firstRun) continue;
            var line = string.Format(L["map.captured"], pin.Name, before, pin.OwnerName ?? clanId);
            pin.AddHistory(DateTime.Now.ToString("HH:mm") + "  " + line);
            MapEvents.Insert(0, DateTime.Now.ToString("HH:mm") + "  " + line);
            while (MapEvents.Count > 30) MapEvents.RemoveAt(MapEvents.Count - 1);
            OnPropertyChanged(nameof(HasMapEvents));
            ShowToast(line, ToastKind.Warning);
        }

        _ownersChecked = true;
        if (changed) RebuildKingdomLabels();
        return true;
    }

    /// <summary>Eklenti satırları konsola düşmez (harita her 2 sn'de bir okuyor).</summary>
    private static bool IsToolsDataLine(string line)
    {
        if (line.StartsWith("[DedicatedServer] > coopkeep.", StringComparison.Ordinal)) return true;
        var bar = line.IndexOf('|');
        if (bar is > 1 and < 7)
        {
            var tag = line[..bar];
            if (tag is "MP" or "OWN" or "SET" or "STAT" or "ITEM" or "PRICE" or "HERO" or "ATTR" or "SKILL" or "EQ" or "PARTY" or "TROOP")
                return true;
        }

        return line is ServerToolsParser.End or "COOPKEEP_ITEMS_END" or "COOPKEEP_PRICES_END";
    }
}
