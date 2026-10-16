using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Haritada çizilen bir yerleşim.</summary>
public sealed record MapSettlement(string Name, SettlementKind Kind, double Left, double Top, IBrush Color)
{
    public bool IsTown => Kind == SettlementKind.Town;
    public bool IsCastle => Kind == SettlementKind.Castle;
    public bool IsVillage => Kind == SettlementKind.Village;
    public bool IsHideout => Kind == SettlementKind.Hideout;
    public double Size => Kind switch { SettlementKind.Town => 9, SettlementKind.Castle => 7, SettlementKind.Hideout => 4, _ => 3.5 };
    public double Offset => -Size / 2;
}

/// <summary>Haritadaki bir oyuncu.</summary>
public sealed partial class MapPlayer(HeroRow hero) : ObservableObject
{
    public HeroRow Hero { get; } = hero;

    [ObservableProperty] private double _left;
    [ObservableProperty] private double _top;
    [ObservableProperty] private string _activity = "";
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Canlı harita: oyuncuların Calradia'daki konumları.
/// </summary>
/// <remarks>
/// <para>
/// Arka plan, oyunun kendi <c>settlements.xml</c> dosyasındaki yerleşim konumlarından
/// çizilir; oyuncu konumları sunucudan <c>coop.debug.mobileparty.info</c> ile okunur.
/// İkisi aynı koordinat sisteminde (2026-10-16'da doğrulandı). Oyunun y ekseni kuzeye
/// doğru büyüdüğü için ekranda ters çevrilir.
/// </para>
/// <para>
/// Sorgu yalnızca harita sayfası açıkken ve sunucu yayındayken yapılır; başka sayfada
/// sunucuya gereksiz yük bindirilmez.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Haritanın çizim alanı (oyun koordinatları ~60-800 aralığında).</summary>
    public const double MapWidth = 760;
    public const double MapHeight = 600;
    private const double MapMinX = 50, MapMaxX = 810, MapMinY = 40, MapMaxY = 640;

    public ObservableCollection<MapSettlement> MapSettlements { get; } = [];
    public ObservableCollection<MapPlayer> MapPlayers { get; } = [];

    [ObservableProperty] private MapPlayer? _selectedMapPlayer;
    [ObservableProperty] private string _mapStatus = "";
    [ObservableProperty] private bool _showVillages;

    public bool HasMapData => MapSettlements.Count > 0;
    public bool HasSelectedMapPlayer => SelectedMapPlayer is not null;
    public bool HasMapPlayers => MapPlayers.Count > 0;

    private DispatcherTimer? _mapTimer;
    private bool _mapRefreshing;

    private static readonly Dictionary<string, string> CultureColors = new()
    {
        ["empire"] = "#9B6BC8",
        ["sturgia"] = "#5B8BD6",
        ["aserai"] = "#D9A441",
        ["vlandia"] = "#C2453E",
        ["battania"] = "#4E9E5C",
        ["khuzait"] = "#3FB3A6",
    };

    private void LoadMap()
    {
        if (MapSettlements.Count > 0 || _game is null) return;

        try
        {
            foreach (var s in GameCatalogs.ReadSettlements(GameCatalogs.SettlementsPath(_game)))
            {
                var color = new SolidColorBrush(Color.Parse(CultureColors.GetValueOrDefault(s.Culture, "#8A8F99")));
                var (left, top) = ToScreen(s.X, s.Y);
                MapSettlements.Add(new MapSettlement(s.Name, s.Kind, left, top, color));
            }
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }

        OnPropertyChanged(nameof(HasMapData));
    }

    private static (double Left, double Top) ToScreen(double x, double y) =>
        ((x - MapMinX) / (MapMaxX - MapMinX) * MapWidth,
         (MapMaxY - y) / (MapMaxY - MapMinY) * MapHeight);

    private void OnMapPageChanged(bool visible)
    {
        if (visible)
        {
            LoadMap();
            _mapTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Background, (_, _) => _ = RefreshMapAsync());
            _mapTimer.Start();
            _ = RefreshMapAsync();
        }
        else
        {
            _mapTimer?.Stop();
        }
    }

    [RelayCommand]
    private async Task RefreshMapAsync()
    {
        if (_mapRefreshing) return;
        if (!IsServing) { MapStatus = L["map.offline"]; return; }

        _mapRefreshing = true;
        try
        {
            if (Heroes.Count == 0) ReloadHeroes();

            foreach (var hero in Heroes.ToList())
            {
                // Parti kimliği bilinmiyorsa önce karakterden oku.
                if (hero.PartyId is null || hero.Gold is null) await RefreshHeroAsync(hero);
                if (hero.PartyId is not { } partyId) continue;

                var lines = await _supervisor.SendAndCollectAsync(AdminCommand.PartyInfo(partyId),
                    l => l.StartsWith("_ships", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

                if (PartyInfoParser.Parse(partyId, lines) is { } party)
                {
                    hero.Party = party;
                    UpsertMapPlayer(hero, party);
                }
            }

            MapStatus = MapPlayers.Count == 0
                ? L["map.noPlayers"]
                : string.Format(L["map.updated"], DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            MapStatus = ex.Message;
        }
        finally
        {
            _mapRefreshing = false;
        }
    }

    private void UpsertMapPlayer(HeroRow hero, PartyInfo party)
    {
        var pin = MapPlayers.FirstOrDefault(p => p.Hero == hero);
        if (pin is null)
        {
            pin = new MapPlayer(hero);
            MapPlayers.Add(pin);
            OnPropertyChanged(nameof(HasMapPlayers));
        }

        (pin.Left, pin.Top) = ToScreen(party.X, party.Y);
        pin.Activity = DescribeActivity(party);
    }

    /// <summary>Oyuncu o an ne yapıyor? — "Diathma'ya gidiyor", "Savaşta" …</summary>
    private string DescribeActivity(PartyInfo p)
    {
        if (p.InBattle) return L["map.act.battle"];
        if (p.Besieging) return L["map.act.siege"];
        if (p.CurrentSettlement is { } inside) return string.Format(L["map.act.in"], inside);
        if (p.InArmy) return L["map.act.army"];
        if (p.TargetSettlement is { } target) return string.Format(L["map.act.goingTo"], target);
        return L["map.act.travelling"];
    }

    [RelayCommand]
    private void SelectMapPlayer(MapPlayer? player)
    {
        foreach (var p in MapPlayers) p.IsSelected = p == player;
        SelectedMapPlayer = player;
        if (player is null) return;

        // Karttaki düğmeler yalnızca bu oyuncuya uygulanmalı.
        AdminTargetAll = false;
        SelectedHero = player.Hero;
    }

    partial void OnSelectedMapPlayerChanged(MapPlayer? value) => OnPropertyChanged(nameof(HasSelectedMapPlayer));
}
