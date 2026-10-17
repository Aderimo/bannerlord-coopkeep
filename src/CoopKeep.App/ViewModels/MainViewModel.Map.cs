using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Images;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Map;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;
using CoopKeep.Core.Viewer;

namespace CoopKeep.App.ViewModels;

/// <summary>Haritada çizilen bir yerleşim.</summary>
public sealed partial class MapSettlement(SettlementInfo info, double left, double top) : ObservableObject
{
    public SettlementInfo Info { get; } = info;
    public string Id => Info.Id;
    public string Name => Info.Name;
    public SettlementKind Kind => Info.Kind;
    public double Left { get; } = left;
    public double Top { get; } = top;

    public bool IsTown => Kind == SettlementKind.Town;
    public bool IsCastle => Kind == SettlementKind.Castle;
    public bool IsVillage => Kind == SettlementKind.Village;
    public bool IsFortified => IsTown || IsCastle;

    [ObservableProperty] private string? _ownerClanId;
    [ObservableProperty] private string? _ownerName;
    [ObservableProperty] private string? _kingdomName;
    [ObservableProperty] private GameImage? _banner;
    [ObservableProperty] private IBrush _color = Brushes.Gray;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Oyun başındaki sahip (harita görüntüsündeki basılı sancak bu klanın).</summary>
    public string? InitialOwnerClanId { get; } = info.OwnerClanId;

    /// <summary>
    /// Kasabanın üstüne CoopKeep'in sancağı çizilsin mi? Harita görüntüsünde her kasabanın
    /// gerçek sancağı zaten basılı; yalnızca el değiştirdiyse ya da oyundan çıkarılmış
    /// sancak varsa üstüne çizilir.
    /// </summary>
    public bool DrawTownOverlay => IsTown && (OwnerClanId != InitialOwnerClanId || Banner?.HasImage == true);

    public bool HasBannerImage => Banner?.HasImage == true;

    partial void OnBannerChanged(GameImage? oldValue, GameImage? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnBannerImageChanged;
        if (newValue is not null) newValue.PropertyChanged += OnBannerImageChanged;
        NotifyOverlay();
    }

    partial void OnOwnerClanIdChanged(string? value) => NotifyOverlay();

    private void OnBannerImageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => NotifyOverlay();

    private void NotifyOverlay()
    {
        OnPropertyChanged(nameof(DrawTownOverlay));
        OnPropertyChanged(nameof(HasBannerImage));
    }

    public GameImage Picture => GameImages.Get(GameImageKind.Settlement, Name);

    /// <summary>Bu oturumda görülen sahiplik değişimleri (en yeni üstte).</summary>
    public ObservableCollection<string> OwnerHistory { get; } = [];

    public bool HasOwnerHistory => OwnerHistory.Count > 0;

    public void AddHistory(string line)
    {
        OwnerHistory.Insert(0, line);
        OnPropertyChanged(nameof(HasOwnerHistory));
    }
}

/// <summary>Haritadaki bir oyuncu (ev sahibinde kahraman, izleyicide anlık görüntü).</summary>
public sealed partial class MapPlayer(string heroId, HeroRow? hero) : ObservableObject
{
    public string HeroId { get; } = heroId;
    public HeroRow? Hero { get; } = hero;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _clanLine = "";
    [ObservableProperty] private double _left;
    [ObservableProperty] private double _top;
    [ObservableProperty] private string _activity = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isPriority;
    [ObservableProperty] private bool _dimmed;
    [ObservableProperty] private GameImage? _banner;
    [ObservableProperty] private int? _troops;
    [ObservableProperty] private bool _online = true;
    [ObservableProperty] private bool _inBattle;
    [ObservableProperty] private bool _besieging;

    /// <summary>Son 10 dakikanın rotası (harita pikselleri).</summary>
    [ObservableProperty] private Points _trail = [];

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    /// <summary>Öncelikli oyuncu büyük, diğerleri (biri öncelikliyken) küçük.</summary>
    public double Scale => IsPriority ? 1.45 : Dimmed ? 0.8 : 1.0;

    public bool HasBanner => Banner?.HasImage == true;

    partial void OnIsPriorityChanged(bool value) => OnPropertyChanged(nameof(Scale));
    partial void OnDimmedChanged(bool value) => OnPropertyChanged(nameof(Scale));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Initial));

    internal readonly List<(DateTime At, Point P)> History = [];
}

/// <summary>Haritada izlenen bir lord ya da hükümdar.</summary>
public sealed partial class MapLord(string heroId, string name, string? clanName, bool isRuler) : ObservableObject
{
    public string HeroId { get; } = heroId;
    public string Name { get; } = name;
    public string? ClanName { get; } = clanName;
    public bool IsRuler { get; } = isRuler;
    public GameImage Portrait => GameImages.Get(GameImageKind.Lord, Name, HeroId);
    public GameImage? Banner => ClanName is null ? null : GameImages.Get(GameImageKind.ClanBanner, ClanName);

    [ObservableProperty] private double _left = -1000;
    [ObservableProperty] private double _top = -1000;
    [ObservableProperty] private bool _located;
    [ObservableProperty] private string _where = "";
}

/// <summary>Bir krallığın adının haritadaki yeri (sahip olduğu kasabaların ortası).</summary>
public sealed record KingdomLabel(string Name, double Left, double Top, IBrush Color);

/// <summary>
/// Canlı harita: oyunun gerçek Calradia haritası, sancaklar, oyuncular, lordlar.
/// </summary>
/// <remarks>
/// <para>
/// Zemin, topluluk wiki'sindeki oyun içi harita görüntüsü (bkz. <see cref="MapProjection"/>).
/// Yerleşimler oyunun <c>settlements.xml</c>'inden, güncel sahipleri sunucudan
/// (<c>settlements.owner_state</c>), sancaklar klan adlarıyla wiki'den gelir.
/// </para>
/// <para>
/// Sorgular yalnızca harita sayfası açıkken ve sunucu yayındayken yapılır.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    public const double MapWidth = MapProjection.ImageWidth;
    public const double MapHeight = MapProjection.ImageHeight;

    public ObservableCollection<MapSettlement> MapSettlements { get; } = [];
    public ObservableCollection<MapPlayer> MapPlayers { get; } = [];
    public ObservableCollection<MapLord> MapLords { get; } = [];
    public ObservableCollection<KingdomLabel> KingdomLabels { get; } = [];
    public ObservableCollection<HeroListEntry> LordResults { get; } = [];

    [ObservableProperty] private Bitmap? _mapImage;
    [ObservableProperty] private MapPlayer? _selectedMapPlayer;
    [ObservableProperty] private MapSettlement? _selectedSettlement;
    [ObservableProperty] private MapLord? _selectedLord;
    [ObservableProperty] private string _mapStatus = "";
    [ObservableProperty] private double _mapZoom = 0.5;

    [ObservableProperty] private bool _showTowns = true;
    [ObservableProperty] private bool _showCastles = true;
    [ObservableProperty] private bool _showVillages;
    [ObservableProperty] private bool _showKingdoms = true;
    [ObservableProperty] private bool _showLords = true;
    [ObservableProperty] private bool _showMapPlayers = true;
    [ObservableProperty] private bool _showTrails = true;

    [ObservableProperty] private string _lordSearch = "";

    public bool HasMapData => MapSettlements.Count > 0;
    public bool HasMapImage => MapImage is not null;
    public bool HasSelectedMapPlayer => SelectedMapPlayer is not null;
    public bool HasSelectedSettlement => SelectedSettlement is not null;
    public bool HasSelectedLord => SelectedLord is not null;
    public bool HasMapPlayers => MapPlayers.Count > 0;
    public bool HasLordResults => LordResults.Count > 0;

    private DispatcherTimer? _mapTimer;
    private bool _mapRefreshing;
    private DateTime _ownersAt = DateTime.MinValue;
    private DateTime _lordsAt = DateTime.MinValue;
    private DateTime _slowMapAt = DateTime.MinValue;

    private Dictionary<string, ClanCatalogEntry> _clans = [];
    private Dictionary<string, KingdomCatalogEntry> _kingdoms = [];
    private IReadOnlyList<HeroListEntry> _heroList = [];
    private readonly HashSet<string> _priorityNames = LoadPriorityNames();

    private void LoadMap()
    {
        if (MapSettlements.Count > 0 || _game is null) return;
        LoadCheatCatalogs();

        try
        {
            _clans = GameCatalogs.ReadClans(GameCatalogs.ClansPath(_game)).ToDictionary(c => c.Id);
            _kingdoms = GameCatalogs.ReadKingdoms(GameCatalogs.KingdomsPath(_game)).ToDictionary(k => k.Id);

            foreach (var s in GameCatalogs.ReadSettlements(GameCatalogs.SettlementsPath(_game)))
            {
                if (s.Kind == SettlementKind.Hideout) continue;
                var (left, top) = MapProjection.ToImage(s.X, s.Y);
                var pin = new MapSettlement(s, left, top);
                MapSettlements.Add(pin);
                if (s.OwnerClanId is { } owner) ApplyOwner(pin, owner);
            }

            // Köyler bağlı oldukları kasaba/kalenin sahibine aittir.
            foreach (var v in MapSettlements.Where(s => s.IsVillage && s.Info.BoundTo is not null))
                if (MapSettlements.FirstOrDefault(s => s.Id == v.Info.BoundTo) is { OwnerClanId: { } owner }) ApplyOwner(v, owner);

            RebuildKingdomLabels();
            InitRulers();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }

        OnPropertyChanged(nameof(HasMapData));
        _ = LoadMapImageAsync();
    }

    private async Task LoadMapImageAsync()
    {
        if (MapImage is not null) return;
        MapStatus = L["map.loadingImage"];

        try
        {
            var path = GameImages.Enabled ? await GameImages.Service.GetMapAsync() : null;
            var bitmap = path is null ? null : await Task.Run(() => GameImages.Decode(path));
            MapImage = bitmap;
            MapStatus = bitmap is null ? L["map.noImage"] : "";
        }
        catch (Exception)
        {
            MapStatus = L["map.noImage"];
        }

        OnPropertyChanged(nameof(HasMapImage));
    }

    private void ApplyOwner(MapSettlement pin, string clanId)
    {
        pin.OwnerClanId = clanId;
        if (_clans.TryGetValue(clanId, out var clan))
        {
            pin.OwnerName = clan.Name;
            pin.Banner = GameImages.Get(GameImageKind.ClanBanner, clan.Name, clan.Id);
            var kingdom = clan.KingdomId is { } k && _kingdoms.TryGetValue(k, out var ke) ? ke : null;
            pin.KingdomName = kingdom?.Name;
            pin.Color = new SolidColorBrush(Color.FromUInt32(kingdom?.Color2 ?? clan.Color));
        }
        else
        {
            pin.OwnerName = clanId;
            pin.Banner = null;
        }
    }

    private void RebuildKingdomLabels()
    {
        KingdomLabels.Clear();
        foreach (var group in MapSettlements.Where(s => s.IsTown && s.KingdomName is not null).GroupBy(s => s.KingdomName!))
        {
            var kingdom = _kingdoms.Values.FirstOrDefault(k => k.Name == group.Key);
            KingdomLabels.Add(new KingdomLabel(group.Key.ToUpperInvariant(), group.Average(s => s.Left), group.Average(s => s.Top) + 40,
                new SolidColorBrush(Color.FromUInt32(kingdom?.Color2 ?? 0xFFE8D9A8))));
        }
    }

    /// <summary>Krallıkların hükümdarları haritada hep izlenir.</summary>
    private void InitRulers()
    {
        MapLords.Clear();
        foreach (var k in _kingdoms.Values.Where(k => k.RulerId is not null))
        {
            var clan = _clans.Values.FirstOrDefault(c => c.LeaderId == k.RulerId);
            MapLords.Add(new MapLord(k.RulerId!, RulerName(k.RulerId!), clan?.Name, isRuler: true));
        }
    }

    private string RulerName(string heroId) =>
        _heroList.FirstOrDefault(h => h.StringId == heroId)?.Name ?? heroId;

    private void OnMapPageChanged(bool visible)
    {
        if (visible)
        {
            LoadMap();
            // 2 sn: işaretlerin geçişi de 2 sn ve doğrusal (MapPage), simge durmadan kayar.
            _mapTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = RefreshMapAsync());
            MapView.Current.Apply(MapZoom);
            _mapTimer.Start();
            _ = RefreshMapAsync();
        }
        else if (!IsViewerMode && _viewerServer is null)
        {
            _mapTimer?.Stop();
        }
    }

    [RelayCommand]
    private async Task RefreshMapAsync()
    {
        if (_mapRefreshing) return;
        if (IsViewerMode) { await RefreshFromHostAsync(); return; }
        if (!IsServing) { if (CurrentPage == AppPage.Map) MapStatus = L["map.offline"]; return; }

        _mapRefreshing = true;
        try
        {
            if (Heroes.Count == 0) ReloadHeroes();

            // Eklenti varsa bütün partiler ve sahipler tek çağrıda; yoksa eski yol (oyuncu başına).
            if (await RefreshPartiesFromToolsAsync())
            {
                if (DateTime.UtcNow - _ownersAt > TimeSpan.FromSeconds(30)) await RefreshOwnersFromToolsAsync();
                if (_heroList.Count == 0 && DateTime.UtcNow - _lordsAt > TimeSpan.FromMinutes(2))
                {
                    _lordsAt = DateTime.UtcNow;
                    _heroList = GameStateParser.ParseHeroList(
                        await _supervisor.SendAndCollectAsync(AdminCommand.HeroList(), _ => false, TimeSpan.FromSeconds(4)));
                }

                if (CurrentPage == AppPage.Map)
                    MapStatus = string.Format(L["map.updated"], DateTime.Now.ToString("HH:mm:ss"));
                PublishSnapshot();
                return;
            }

            // Eski yol oyuncu başına komut gönderiyor; 2 sn'de bir değil 4 sn'de bir.
            if (DateTime.UtcNow - _slowMapAt < TimeSpan.FromSeconds(3.5)) return;
            _slowMapAt = DateTime.UtcNow;

            foreach (var hero in Heroes.ToList())
            {
                if (hero.PartyId is null || hero.Gold is null) await RefreshHeroAsync(hero);
                if (hero.PartyId is not { } partyId) continue;

                var lines = await _supervisor.SendAndCollectAsync(AdminCommand.PartyInfo(partyId),
                    l => l.StartsWith("_ships", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

                if (PartyInfoParser.Parse(partyId, lines) is { } party)
                {
                    var wasInBattle = hero.Party?.InBattle == true;
                    var wasBesieging = hero.Party?.Besieging == true;
                    hero.Party = party;
                    UpsertMapPlayer(hero, party);

                    // Harita uyarıları: savaşa giren, kuşatmaya başlayan oyuncu.
                    if (party.InBattle && !wasInBattle) ShowToast(string.Format(L["map.alert.battle"], hero.Name), ToastKind.Warning);
                    if (party.Besieging && !wasBesieging) ShowToast(string.Format(L["map.alert.siege"], hero.Name), ToastKind.Warning);
                }
            }

            if (DateTime.UtcNow - _ownersAt > TimeSpan.FromSeconds(45) && !_ownersRefreshing) _ = RefreshOwnersAsync();
            if (DateTime.UtcNow - _lordsAt > TimeSpan.FromSeconds(30)) await RefreshLordsAsync();

            if (CurrentPage == AppPage.Map)
                MapStatus = MapPlayers.Count == 0
                    ? L["map.noPlayers"]
                    : string.Format(L["map.updated"], DateTime.Now.ToString("HH:mm:ss"));

            PublishSnapshot();
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

    /// <summary>Kasaba ve kalelerin güncel sahipleri (savaşta el değiştirebiliyor).</summary>
    private bool _ownersRefreshing;

    /// <summary>Haritada son görülen el değiştirmeler (en yeni üstte).</summary>
    public ObservableCollection<string> MapEvents { get; } = [];

    public bool HasMapEvents => MapEvents.Count > 0;

    private async Task RefreshOwnersAsync()
    {
        _ownersAt = DateTime.UtcNow;
        _ownersRefreshing = true;
        var changed = false;
        var firstRun = MapSettlements.All(s => s.OwnerHistory.Count == 0) && !_ownersChecked;

        foreach (var pin in MapSettlements.Where(s => s.IsFortified).ToList())
        {
            var lines = await _supervisor.SendAndCollectAsync(AdminCommand.SettlementOwner(pin.Id),
                l => l.StartsWith("LIVE_TEST_JSON", StringComparison.Ordinal) || AdminResponseParser.IsTerminal(l),
                TimeSpan.FromSeconds(2));
            if (GameStateParser.ParseOwnerState(lines) is { } owner && owner.ClanId != pin.OwnerClanId)
            {
                var before = pin.OwnerName ?? pin.OwnerClanId ?? "?";
                ApplyOwner(pin, owner.ClanId);

                // İlk taramada (save'deki hâl XML'den farklı olabilir) bildirim gösterme.
                if (!firstRun)
                {
                    var line = string.Format(L["map.captured"], pin.Name, before, pin.OwnerName ?? owner.ClanId);
                    pin.AddHistory(DateTime.Now.ToString("HH:mm") + "  " + line);
                    MapEvents.Insert(0, DateTime.Now.ToString("HH:mm") + "  " + line);
                    while (MapEvents.Count > 30) MapEvents.RemoveAt(MapEvents.Count - 1);
                    OnPropertyChanged(nameof(HasMapEvents));
                    ShowToast(line, ToastKind.Warning);
                }

                foreach (var v in MapSettlements.Where(v => v.IsVillage && v.Info.BoundTo == pin.Id)) ApplyOwner(v, owner.ClanId);
                changed = true;
            }
        }

        _ownersChecked = true;
        _ownersRefreshing = false;
        if (changed) RebuildKingdomLabels();
    }

    private bool _ownersChecked;

    private async Task RefreshLordsAsync()
    {
        _lordsAt = DateTime.UtcNow;

        if (_heroList.Count == 0)
        {
            var lines = await _supervisor.SendAndCollectAsync(AdminCommand.HeroList(), _ => false, TimeSpan.FromSeconds(4));
            _heroList = GameStateParser.ParseHeroList(lines);
            if (_heroList.Count > 0) InitRulers();
        }

        foreach (var lord in MapLords.ToList())
            await LocateLordAsync(lord);
    }

    private async Task LocateLordAsync(MapLord lord)
    {
        var info = AdminResponseParser.ParseHeroInfo(
            await _supervisor.SendAndCollectAsync(AdminCommand.HeroInfo(lord.HeroId), AdminResponseParser.IsTerminal));
        if (info?.PartyId is not { } partyId) { lord.Located = false; lord.Where = L["map.lordNoParty"]; return; }

        var pos = GameStateParser.ParsePosition(
            await _supervisor.SendAndCollectAsync(AdminCommand.PartyPosition(partyId), l => l.StartsWith("id=", StringComparison.Ordinal),
                TimeSpan.FromSeconds(2)));
        if (pos is null) return;

        (lord.Left, lord.Top) = MapProjection.ToImage(pos.X, pos.Y);
        lord.Located = true;
        lord.Where = pos.SettlementId is { } s ? SettlementName(s) : NearestSettlementText(lord.Left, lord.Top);
    }

    private string SettlementName(string id) => MapSettlements.FirstOrDefault(s => s.Id == id)?.Name ?? id;

    private string NearestSettlementText(double left, double top)
    {
        var nearest = MapSettlements.Where(s => !s.IsVillage)
            .OrderBy(s => (s.Left - left) * (s.Left - left) + (s.Top - top) * (s.Top - top))
            .FirstOrDefault();
        return nearest is null ? "" : string.Format(L["map.near"], nearest.Name);
    }

    partial void OnLordSearchChanged(string value)
    {
        LordResults.Clear();
        var q = value.Trim();
        if (q.Length >= 2)
            foreach (var h in _heroList.Where(h => h.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) &&
                                                   !h.StringId.StartsWith("dead_", StringComparison.Ordinal)).Take(12))
                LordResults.Add(h);
        OnPropertyChanged(nameof(HasLordResults));
    }

    /// <summary>Aranan lordu haritaya ekler ve yerini bulur.</summary>
    [RelayCommand]
    private async Task TrackLordAsync(HeroListEntry? entry)
    {
        if (entry is null) return;

        var lord = MapLords.FirstOrDefault(l => l.HeroId == entry.StringId);
        if (lord is null)
        {
            var clan = _clans.Values.FirstOrDefault(c => c.LeaderId == entry.StringId);
            lord = new MapLord(entry.StringId, entry.Name, clan?.Name, isRuler: false);
            MapLords.Add(lord);
        }

        LordSearch = "";
        ShowLords = true;
        if (IsServing) await LocateLordAsync(lord);
        SelectLord(lord);
    }

    private void UpsertMapPlayer(HeroRow hero, PartyInfo party)
    {
        var pin = MapPlayers.FirstOrDefault(p => p.Hero == hero);
        if (pin is null)
        {
            pin = new MapPlayer(hero.HeroId, hero);
            MapPlayers.Add(pin);
            OnPropertyChanged(nameof(HasMapPlayers));
        }

        pin.Name = hero.Name;
        pin.ClanLine = hero.ClanLine;
        pin.IsPriority = hero.IsPriority;
        pin.Online = hero.IsOnline;
        pin.Troops = hero.Roster?.TroopCount;
        pin.Banner = PlayerBanner(hero.KingdomName);
        MovePin(pin, party.X, party.Y);
        pin.Activity = DescribeActivity(party);
        UpdateDimming();
    }

    private void MovePin(MapPlayer pin, double x, double y)
    {
        (pin.Left, pin.Top) = MapProjection.ToImage(x, y);

        var now = DateTime.UtcNow;
        pin.History.Add((now, new Point(pin.Left, pin.Top)));
        pin.History.RemoveAll(h => now - h.At > TimeSpan.FromMinutes(10));
        pin.Trail = new Points(pin.History.Select(h => h.P));
    }

    /// <summary>
    /// Oyuncunun kendi sancağı sunucudan okunamıyor; bir krallıktaysa krallığın
    /// (hükümdar klanının) sancağı gösterilir.
    /// </summary>
    private GameImage? PlayerBanner(string? kingdomName)
    {
        if (kingdomName is null) return null;
        var kingdom = _kingdoms.Values.FirstOrDefault(k => k.Name == kingdomName);
        var clan = kingdom is null ? null : _clans.Values.FirstOrDefault(c => c.LeaderId == kingdom.RulerId);
        return clan is null ? null : GameImages.Get(GameImageKind.ClanBanner, clan.Name, clan.Id);
    }

    private void SyncMapPlayers()
    {
        foreach (var pin in MapPlayers.Where(p => p.Hero is not null))
        {
            pin.IsPriority = pin.Hero!.IsPriority;
            pin.Name = pin.Hero.Name;
        }

        foreach (var h in Heroes) h.IsPriority = _priorityNames.Contains(h.Name);
        UpdateDimming();
    }

    /// <summary>Oyuncu ★ ile işaretlendi ya da işareti kalktı.</summary>
    private void OnPriorityChanged()
    {
        foreach (var h in Heroes)
        {
            if (h.IsPriority) _priorityNames.Add(h.Name);
            else _priorityNames.Remove(h.Name);
        }

        foreach (var pin in MapPlayers)
            pin.IsPriority = pin.Hero?.IsPriority ?? _priorityNames.Contains(pin.Name);

        SavePriorityNames();
        UpdateDimming();
    }

    [RelayCommand]
    private void TogglePriority(MapPlayer? pin)
    {
        if (pin is null) return;
        if (pin.Hero is { } hero) hero.IsPriority = !hero.IsPriority;
        else
        {
            pin.IsPriority = !pin.IsPriority;
            if (pin.IsPriority) _priorityNames.Add(pin.Name); else _priorityNames.Remove(pin.Name);
            SavePriorityNames();
            UpdateDimming();
        }
    }

    private void UpdateDimming()
    {
        var any = MapPlayers.Any(p => p.IsPriority);
        foreach (var p in MapPlayers) p.Dimmed = any && !p.IsPriority;
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
        ClearMapSelection();
        if (player is null) return;
        player.IsSelected = true;
        SelectedMapPlayer = player;
        if (player.Hero is { } hero)
        {
            SelectedHero = hero;
            if (IsServing) _ = RefreshHeroAsync(hero, full: true);
        }
    }

    [RelayCommand]
    private void SelectSettlement(MapSettlement? settlement)
    {
        // Işınlama sırasında tıklanan yerleşim hedef olur.
        if (IsMapTeleporting && settlement is not null)
        {
            PickMapTeleportTarget(settlement);
            return;
        }

        ClearMapSelection();
        if (settlement is null) return;
        settlement.IsSelected = true;
        SelectedSettlement = settlement;
    }

    [RelayCommand]
    private void SelectLord(MapLord? lord)
    {
        ClearMapSelection();
        SelectedLord = lord;
    }

    [RelayCommand]
    private void ClearMapSelection()
    {
        foreach (var p in MapPlayers) p.IsSelected = false;
        foreach (var s in MapSettlements) s.IsSelected = false;
        SelectedMapPlayer = null;
        SelectedSettlement = null;
        SelectedLord = null;
    }

    /// <summary>Haritadaki oyuncu kartından Hileler sekmesine: yalnız o oyuncu hedef olur.</summary>
    [RelayCommand]
    private void OpenInCheats(MapPlayer? player)
    {
        if (player?.Hero is not { } hero) return;
        foreach (var h in Heroes) h.IsTarget = h == hero;
        SelectedHero = hero;
        CurrentPage = AppPage.Cheats;
    }

    partial void OnSelectedMapPlayerChanged(MapPlayer? value) => OnPropertyChanged(nameof(HasSelectedMapPlayer));
    partial void OnSelectedSettlementChanged(MapSettlement? value) => OnPropertyChanged(nameof(HasSelectedSettlement));
    partial void OnSelectedLordChanged(MapLord? value) => OnPropertyChanged(nameof(HasSelectedLord));
    partial void OnMapImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasMapImage));

    [RelayCommand] private void ZoomIn() => MapZoom = Math.Min(MaxMapZoom, MapZoom * 1.25);
    [RelayCommand] private void ZoomOut() => MapZoom = Math.Max(MinMapZoom, MapZoom / 1.25);

    public const double MinMapZoom = 0.35;
    public const double MaxMapZoom = 4;

    // --- öncelikli oyuncuların kaydı ---------------------------------------

    private static string PriorityPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "map-priority.json");

    private static HashSet<string> LoadPriorityNames()
    {
        try
        {
            if (File.Exists(PriorityPath))
                return new HashSet<string>(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PriorityPath)) ?? [],
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException) { }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void SavePriorityNames()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PriorityPath)!);
            File.WriteAllText(PriorityPath, JsonSerializer.Serialize(_priorityNames.ToList()));
        }
        catch (IOException) { }
    }

    // --- izleyici paylaşımı ------------------------------------------------

    /// <summary>Ev sahibinin haritası; izleyici sunucusu açıksa gönderilir.</summary>
    private MapSnapshot BuildSnapshot() => new()
    {
        World = ActiveSaveName,
        Players = MapPlayers.Where(p => p.Hero is not null).Select(p => new PlayerSnapshot
        {
            HeroId = p.HeroId,
            Name = p.Name,
            ClanName = p.Hero!.ClanName,
            KingdomId = _kingdoms.Values.FirstOrDefault(k => k.Name == p.Hero.KingdomName)?.Id,
            X = p.Hero.Party?.X ?? 0,
            Y = p.Hero.Party?.Y ?? 0,
            Activity = p.Activity,
            Troops = p.Hero.Roster?.TroopCount,
            Wounded = p.Hero.Roster?.WoundedCount,
            Health = p.Hero.Health,
            Online = p.Hero.IsOnline,
        }).ToList(),
        Lords = MapLords.Where(l => l.Located).Select(l =>
        {
            var (x, y) = MapProjection.ToGame(l.Left, l.Top);
            return new LordSnapshot { HeroId = l.HeroId, Name = l.Name, ClanId = _clans.Values.FirstOrDefault(c => c.Name == l.ClanName)?.Id, X = x, Y = y, IsRuler = l.IsRuler };
        }).ToList(),
        Owners = MapSettlements.Where(s => s.IsFortified && s.OwnerClanId is not null).ToDictionary(s => s.Id, s => s.OwnerClanId!),
    };

    /// <summary>İzleyici: ev sahibinin anlık görüntüsünü haritaya uygular.</summary>
    private void ApplySnapshot(MapSnapshot snap)
    {
        foreach (var (settlementId, clanId) in snap.Owners)
        {
            var pin = MapSettlements.FirstOrDefault(s => s.Id == settlementId);
            if (pin is null || pin.OwnerClanId == clanId) continue;
            ApplyOwner(pin, clanId);
            foreach (var v in MapSettlements.Where(v => v.IsVillage && v.Info.BoundTo == settlementId)) ApplyOwner(v, clanId);
        }

        foreach (var p in snap.Players)
        {
            var pin = MapPlayers.FirstOrDefault(m => m.HeroId == p.HeroId);
            if (pin is null)
            {
                pin = new MapPlayer(p.HeroId, null);
                MapPlayers.Add(pin);
            }

            pin.Name = p.Name;
            pin.ClanLine = p.ClanName ?? "";
            pin.Activity = p.Activity ?? "";
            pin.Troops = p.Troops;
            pin.Online = p.Online;
            pin.IsPriority = _priorityNames.Contains(p.Name);
            pin.Banner = p.KingdomId is { } k && _kingdoms.TryGetValue(k, out var kingdom) ? PlayerBanner(kingdom.Name) : null;
            MovePin(pin, p.X, p.Y);
        }

        foreach (var gone in MapPlayers.Where(m => snap.Players.All(p => p.HeroId != m.HeroId)).ToList()) MapPlayers.Remove(gone);

        foreach (var l in snap.Lords)
        {
            var lord = MapLords.FirstOrDefault(m => m.HeroId == l.HeroId);
            if (lord is null)
            {
                lord = new MapLord(l.HeroId, l.Name, l.ClanId is { } c && _clans.TryGetValue(c, out var clan) ? clan.Name : null, l.IsRuler);
                MapLords.Add(lord);
            }

            (lord.Left, lord.Top) = MapProjection.ToImage(l.X, l.Y);
            lord.Located = true;
            lord.Where = NearestSettlementText(lord.Left, lord.Top);
        }

        UpdateDimming();
        OnPropertyChanged(nameof(HasMapPlayers));
        RebuildKingdomLabels();
    }
}
