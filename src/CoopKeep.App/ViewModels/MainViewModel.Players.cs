using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Dünyaya kayıtlı bir oyuncunun kahramanı.</summary>
public sealed partial class HeroRow(string heroId, string? steamId, string? clanId, string? partyId) : ObservableObject
{
    public string HeroId { get; } = heroId;

    /// <summary>SteamID64 — arayüzde yalnızca son dört hanesi gösterilir.</summary>
    public string? SteamId { get; } = steamId;

    /// <summary>Kayıtlı klan kimliği (nüfuz ve ün komutları için), save'in eş JSON'undan.</summary>
    public string? ClanId { get; } = clanId;

    [ObservableProperty] private string? _partyId = partyId;
    [ObservableProperty] private string _name = heroId;
    [ObservableProperty] private string? _clanName;
    [ObservableProperty] private string? _kingdomName;
    [ObservableProperty] private int? _gold;
    [ObservableProperty] private int? _health;
    [ObservableProperty] private int? _age;
    [ObservableProperty] private int? _influence;
    [ObservableProperty] private int? _renown;
    [ObservableProperty] private PartyRoster? _roster;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isBanned;

    /// <summary>Son görülme ve toplam oynama süresi.</summary>
    [ObservableProperty] private string _statText = "";

    /// <summary>Hileler sekmesinde hedef olarak seçili mi?</summary>
    [ObservableProperty] private bool _isTarget;

    /// <summary>Haritada öncelikli (büyük, sarı çerçeveli) gösterilsin mi?</summary>
    [ObservableProperty] private bool _isPriority;

    /// <summary>Haritadan gelen son parti bilgisi.</summary>
    [ObservableProperty] private PartyInfo? _party;

    public string GoldText => Gold is { } g ? g.ToString("N0") : "—";
    public string HealthText => Health is { } h ? h.ToString() : "—";
    public string AgeText => Age is { } a ? a.ToString() : "—";
    public string InfluenceText => Influence is { } i ? i.ToString("N0") : "—";
    public string RenownText => Renown is { } r ? r.ToString("N0") : "—";
    public string TroopsText => Roster is { } r ? r.TroopCount.ToString("N0") : "—";

    /// <summary>Kadro, asker adları ve portreleriyle (harita kartı için).</summary>
    public IReadOnlyList<RosterLine> RosterLines =>
        Roster?.Members.Where(m => !m.IsHero)
            .Select(m => new RosterLine(m, TroopNames.Resolve?.Invoke(m.TroopId) ?? m.TroopId))
            .ToList() ?? [];

    public bool HasSteamId => CoopKeep.Core.Saves.CoopSessionPlayer.IsSteamId64(SteamId);
    public string SteamIdShort => HasSteamId ? "Steam ••••" + SteamId![^4..] : "—";

    /// <summary>Can çubuğu için 0-1 arası.</summary>
    public double HealthRatio => Health is { } h ? Math.Clamp(h / 100.0, 0, 1) : 0;

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    public string ClanLine => string.Join(" · ", new[] { ClanName, KingdomName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Initial));
    partial void OnGoldChanged(int? value) => OnPropertyChanged(nameof(GoldText));
    partial void OnAgeChanged(int? value) => OnPropertyChanged(nameof(AgeText));
    partial void OnInfluenceChanged(int? value) => OnPropertyChanged(nameof(InfluenceText));
    partial void OnRenownChanged(int? value) => OnPropertyChanged(nameof(RenownText));
    partial void OnRosterChanged(PartyRoster? value)
    {
        OnPropertyChanged(nameof(TroopsText));
        OnPropertyChanged(nameof(RosterLines));
    }
    partial void OnClanNameChanged(string? value) => OnPropertyChanged(nameof(ClanLine));
    partial void OnKingdomNameChanged(string? value) => OnPropertyChanged(nameof(ClanLine));

    partial void OnHealthChanged(int? value)
    {
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(HealthRatio));
    }
}

/// <summary>Banlılar listesindeki bir satır: Steam banı ya da (kimlik bilinmiyorsa) ad ile engel.</summary>
public sealed record BanRow(SteamBan? Ban, string? BlockedName = null)
{
    public bool IsByName => Ban is null;
    public string Name => Ban is null ? BlockedName ?? "—" : string.IsNullOrWhiteSpace(Ban.Name) ? "—" : Ban.Name!;
    public string SteamIdShort => Ban is null ? "" : "Steam ••••" + Ban.SteamId[^4..];
    public string DateText => Ban?.BannedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "";
    public string? Note => Ban?.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

/// <summary>
/// Oyuncular sayfası: bağlı oyuncular, kayıtlı karakterler, at / banla / ban kaldır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ban:</b> Coop'un kendi <c>steam-bans.json</c> listesi kullanılıyor. Sunucu dosyayı
/// her girişte yeniden okuyup banlı SteamID'yi karakteri yüklenmeden reddediyor; yeniden
/// başlatmak gerekmiyor. SteamID'ler save'in eş JSON'unda (<c>ControllerId</c>).
/// </para>
/// <para>
/// Steam kimliği bilinmeyen (henüz karakteri kaydedilmemiş) biri için ad listesi
/// kullanılır: o ad bağlandığında CoopKeep onu atar. Arayüzde ayrı bir "engel listesi"
/// yok; bu adlar Banlılar listesinde "ad ile" etiketiyle görünür (v0.5, tek kavram).
/// </para>
/// <para>
/// Hile/yönetici araçları ayrı sekmede (<c>MainViewModel.Cheats.cs</c>).
/// </para>
/// </remarks>
public partial class MainViewModel
{
    public ObservableCollection<HeroRow> Heroes { get; } = [];

    [ObservableProperty] private HeroRow? _selectedHero;

    public bool HasHeroes => Heroes.Count > 0;

    /// <summary>Oyuncular sayfasındaki arama kutusu.</summary>
    [ObservableProperty] private string _heroFilter = "";

    /// <summary>Aramaya uyan karakterler; bağlı olanlar önce.</summary>
    public ObservableCollection<HeroRow> VisibleHeroes { get; } = [];

    partial void OnHeroFilterChanged(string value) => ApplyHeroFilter();

    private void ApplyHeroFilter()
    {
        VisibleHeroes.Clear();
        foreach (var h in FilterHeroes(Heroes, HeroFilter)) VisibleHeroes.Add(h);
    }

    /// <summary>Ad, klan ya da Steam kimliğinin son hanelerine göre süzer.</summary>
    private static IEnumerable<HeroRow> FilterHeroes(IEnumerable<HeroRow> heroes, string? query)
    {
        var q = query?.Trim() ?? "";
        return heroes
            .Where(h => q.Length == 0
                        || h.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || (h.ClanName?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
                        || (h.SteamId?.EndsWith(q, StringComparison.Ordinal) ?? false))
            .OrderByDescending(h => h.IsOnline)
            .ThenBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>Kahraman listesinin kaynağı: çalışan dünya, yoksa seçili dünya.</summary>
    private Core.Saves.SaveSet? HeroSource =>
        IsRunning
            ? Campaigns.FirstOrDefault(c => string.Equals(c.Name, ActiveSaveName, StringComparison.OrdinalIgnoreCase))
            : SelectedCampaign;

    private void ReloadHeroes()
    {
        var previous = SelectedHero?.HeroId;

        // Save listesi diskten tazelenmeli: oyuncu ilk kez katıldığında JSON yeni yazılmış olur.
        var source = HeroSource is { } s ? _saves?.Find(s.Name) ?? s : null;

        var known = Heroes.ToDictionary(h => h.HeroId);
        Heroes.Clear();
        foreach (var p in source?.Players ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.HeroId)) continue;

            // Var olan satırı koru: okunmuş ad/altın kaybolmasın.
            Heroes.Add(known.TryGetValue(p.HeroId, out var row)
                ? row
                : new HeroRow(p.HeroId, p.ControllerId, p.ClanId, p.MobilePartyId));
        }

        SelectedHero = Heroes.FirstOrDefault(h => h.HeroId == previous) ?? Heroes.FirstOrDefault();
        UpdateHeroFlags();
        UpdatePlayerStatTexts();
        OnPropertyChanged(nameof(HasHeroes));
        OnHeroesReloaded();
    }

    /// <summary>Çevrimiçi ve banlı işaretlerini tazeler.</summary>
    private void UpdateHeroFlags()
    {
        var online = Players.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var h in Heroes)
        {
            h.IsOnline = online.Contains(h.Name);
            h.IsBanned = _banList?.IsBanned(h.SteamId) == true
                         || BlockList.Contains(h.Name, StringComparer.OrdinalIgnoreCase);
        }

        ApplyHeroFilter();
        ApplyCheatHeroFilter();
    }

    /// <summary>Karakterlerin adını, altınını, canını, klanını ve ordusunu sunucudan okur.</summary>
    [RelayCommand(CanExecute = nameof(CanCommand))]
    private async Task RefreshHeroesAsync()
    {
        ReloadHeroes();
        if (!IsServing) return;

        // Kalabalık sunucuda her kahraman için 3-4 komut konsolu dakikalarca meşgul ediyordu:
        // ayrıntı (klan, kadro) yalnızca bağlı oyunculara; diğerlerine seçilince.
        foreach (var hero in Heroes.OrderByDescending(h => h.IsOnline).ToList())
            await RefreshHeroAsync(hero, full: hero.IsOnline);
    }

    private async Task RefreshHeroAsync(HeroRow hero, bool full = false)
    {
        try
        {
            var lines = await _supervisor.SendAndCollectAsync(
                AdminCommand.HeroInfo(hero.HeroId), AdminResponseParser.IsTerminal);

            if (AdminResponseParser.ParseHeroInfo(lines) is { } info)
            {
                if (!string.IsNullOrEmpty(info.Name)) hero.Name = info.Name;
                hero.Gold = info.Gold;
                hero.Health = info.Health;
                hero.Age = info.Age;
                hero.ClanName = info.ClanName;
                if (info.PartyId is not null) hero.PartyId = info.PartyId;
            }

            if (!full) return;

            if (hero.ClanId is { Length: > 0 } clanId)
            {
                var clanLines = await _supervisor.SendAndCollectAsync(AdminCommand.ClanInfo(clanId),
                    l => l.StartsWith("Members:", StringComparison.Ordinal) || AdminResponseParser.IsTerminal(l));
                if (GameStateParser.ParseClanInfo(clanLines) is { } clan)
                {
                    hero.Influence = clan.Influence;
                    hero.Renown = clan.Renown;
                    hero.KingdomName = clan.Kingdom;
                }
            }

            await RefreshRosterAsync(hero);
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
    }

    /// <summary>
    /// Kahramanın ordusunu okur: eklenti açıksa <c>coopkeep.roster</c>, değilse Coop'un
    /// <c>character_ids</c> komutu.
    /// </summary>
    /// <remarks>
    /// v0.4'te oyuncu kahramanlarında "Asker al" listesi boş kalıyordu; eklentinin tek satırlık
    /// çıktısı oyuncu ve lord partilerinde aynı biçimde geliyor.
    /// </remarks>
    private async Task RefreshRosterAsync(HeroRow hero)
    {
        if (ServerToolsAvailable)
        {
            var tools = ServerToolsParser.ParseRoster(await _supervisor.SendAndCollectAsync(AdminCommand.ToolsRoster(hero.HeroId),
                IsToolsTerminal, TimeSpan.FromSeconds(4)));
            if (tools is not null)
            {
                static RosterEntry Entry(ToolsTroop t) => new(t.Id, t.Number, t.Wounded, t.Xp, t.IsHero);
                hero.Roster = new PartyRoster(hero.Name, tools.Members.Select(Entry).ToList(), tools.Prisoners.Select(Entry).ToList());
                return;
            }
        }

        var lines = await _supervisor.SendAndCollectAsync(AdminCommand.PartyRoster(hero.HeroId),
            AdminResponseParser.IsTerminal, TimeSpan.FromSeconds(3));
        if (GameStateParser.ParseRoster(lines) is { } roster) hero.Roster = roster;
    }

    partial void OnSelectedHeroChanged(HeroRow? value) => NotifyAdminCommands();

    // --- ban -------------------------------------------------------------

    private SteamBanList? _banList;

    public ObservableCollection<BanRow> Bans { get; } = [];

    public bool HasBans => Bans.Count > 0;

    /// <summary>Son at/banla işlemi; "Geri al" bunu çevirir.</summary>
    [ObservableProperty] private string _lastPlayerAction = "";

    private Func<Task>? _undoPlayerAction;

    public bool CanUndoPlayerAction => _undoPlayerAction is not null;

    private string BanFilePath => Path.Combine(DataDirectory, SteamBanList.FileName);

    private void ReloadBans()
    {
        _banList = SteamBanList.Load(BanFilePath);
        Bans.Clear();
        foreach (var b in _banList.Bans.OrderByDescending(b => b.BannedAt)) Bans.Add(new BanRow(b));
        foreach (var name in BlockList) Bans.Add(new BanRow(null, name));
        UpdateHeroFlags();
        OnPropertyChanged(nameof(HasBans));
    }

    /// <summary>Karakteri SteamID'siyle banlar; bağlıysa atar.</summary>
    [RelayCommand]
    private async Task BanHeroAsync(HeroRow? hero)
    {
        hero ??= SelectedHero;
        if (hero is null) return;

        if (!hero.HasSteamId)
        {
            // Steam kimliği yok: ad listesine düş.
            AddToBlockList(hero.Name);
            await KickByNameAsync(hero.Name);
            return;
        }

        ReloadBans();
        _banList!.Ban(hero.SteamId!, hero.Name, AdminNote.Trim());
        _banList.Save();
        ReloadBans();

        var kicked = await KickByNameAsync(hero.Name);
        Audit(hero.Name, L["audit.ban"]);
        ShowToast(string.Format(L["msg.banned"], hero.Name), ToastKind.Warning);

        RememberPlayerAction(string.Format(kicked ? L["players.last.banKick"] : L["players.last.ban"], hero.Name), async () =>
        {
            await UnbanAsync(Bans.FirstOrDefault(b => b.Ban?.SteamId == hero.SteamId));
        });
    }

    /// <summary>Bağlı oyuncuyu banlar: karakteri bulunursa SteamID ile, yoksa adıyla.</summary>
    [RelayCommand(CanExecute = nameof(CanBlockPlayer))]
    private async Task BlockPlayerAsync(ConnectedPlayer? player)
    {
        player ??= SelectedPlayer;
        if (player is null) return;

        var hero = Heroes.FirstOrDefault(h => string.Equals(h.Name, player.Name, StringComparison.OrdinalIgnoreCase));
        if (hero is { HasSteamId: true })
        {
            await BanHeroAsync(hero);
            return;
        }

        AddToBlockList(player.Name);
        await KickAsync(player);
        RememberPlayerAction(string.Format(L["players.last.ban"], player.Name), () =>
        {
            RemoveBlockName(player.Name);
            return Task.CompletedTask;
        });
    }

    private bool CanBlockPlayer(ConnectedPlayer? player) => CanCommand() && (player ?? SelectedPlayer) is not null;

    [RelayCommand]
    private Task UnbanAsync(BanRow? row)
    {
        if (row is null) return Task.CompletedTask;

        if (row.IsByName)
        {
            var name = row.Name;
            RemoveBlockName(name);
            Audit(name, L["audit.unban"]);
            ShowToast(string.Format(L["msg.unbanned"], name));
            RememberPlayerAction(string.Format(L["players.last.unban"], name), () =>
            {
                AddToBlockList(name);
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        }

        ReloadBans();
        if (_banList!.Unban(row.Ban!.SteamId))
        {
            _banList.Save();
            Audit(row.Name, L["audit.unban"]);
            ShowToast(string.Format(L["msg.unbanned"], row.Name));
            RememberPlayerAction(string.Format(L["players.last.unban"], row.Name), () =>
            {
                ReloadBans();
                _banList!.Ban(row.Ban.SteamId, row.Ban.Name, row.Ban.Note);
                _banList.Save();
                ReloadBans();
                return Task.CompletedTask;
            });
        }

        ReloadBans();
        return Task.CompletedTask;
    }

    /// <summary>Atma işlemi geri alınamaz (oyuncu zaten tekrar girebilir); yalnızca kaydedilir.</summary>
    [RelayCommand(CanExecute = nameof(CanKick))]
    private async Task KickAsync(ConnectedPlayer? player)
    {
        player ??= SelectedPlayer;
        if (player is null) return;

        await SendAsync(ServerCommand.Kick(player.Id.ToString()));
        ShowToast(string.Format(L["msg.kicked"], player.Name));
        RememberPlayerAction(string.Format(L["players.last.kick"], player.Name), null);
    }

    private bool CanKick(ConnectedPlayer? player) => CanCommand() && (player ?? SelectedPlayer) is not null;

    private async Task<bool> KickByNameAsync(string name)
    {
        var online = Players.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (online is null || !IsServing) return false;

        await SendAsync(ServerCommand.Kick(online.Id.ToString()));
        return true;
    }

    [RelayCommand]
    private async Task UndoPlayerActionAsync()
    {
        var undo = _undoPlayerAction;
        _undoPlayerAction = null;
        LastPlayerAction = "";
        OnPropertyChanged(nameof(CanUndoPlayerAction));
        if (undo is not null) await undo();
    }

    private void RememberPlayerAction(string text, Func<Task>? undo)
    {
        LastPlayerAction = text;
        _undoPlayerAction = undo;
        OnPropertyChanged(nameof(CanUndoPlayerAction));
    }

    // --- ad ile engelleme (SteamID bilinmiyorsa) --------------------------

    /// <summary>
    /// Steam kimliği bilinmeyen oyuncular için ad listesi: listedeki ad bağlandığı anda
    /// CoopKeep onu atar. Ad değiştirilebildiği için kesin koruma değil.
    /// </summary>
    public ObservableCollection<string> BlockList { get; } = [];

    private void RemoveBlockName(string name)
    {
        var match = BlockList.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        BlockList.Remove(match);
        Settings.BlockListStore.Save(BlockList);
        ReloadBans();
    }

    private void AddToBlockList(string? name)
    {
        var clean = ServerCommand.SanitizeArgument(name, 64);
        if (clean.Length == 0 || BlockList.Contains(clean, StringComparer.OrdinalIgnoreCase)) return;

        BlockList.Add(clean);
        Settings.BlockListStore.Save(BlockList);
        ReloadBans();
        ShowToast(string.Format(L["msg.blocked"], clean));
    }

    /// <summary>Yeni bağlanan oyuncu: engelliyse atılır, değilse karakter listesi tazelenir.</summary>
    private void OnPlayerJoined(ConnectedPlayer player)
    {
        if (BlockList.Contains(player.Name, StringComparer.OrdinalIgnoreCase))
        {
            _ = SendAsync(ServerCommand.Kick(player.Id.ToString()));
            Audit(player.Name, L["audit.autoKick"]);
            ShowToast(string.Format(L["msg.autoKicked"], player.Name), ToastKind.Warning);
            return;
        }

        ShowToast(string.Format(L["msg.joined"], player.Name), ToastKind.Info);
        PlayerSeen(player.Name);

        // İlk kez katılan oyuncunun karakteri save'e ancak bir süre sonra yazılıyor.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshHeroesAsync()));
    }

    // --- denetim kaydı ---------------------------------------------------

    /// <summary>
    /// Yönetici işlemini kaydeder ve diske yazar.
    /// </summary>
    /// <remarks>
    /// Kayıt kalıcı: uygulama kapanınca silinen bir denetim kaydının anlamı olmaz.
    /// </remarks>
    private void Audit(string playerName, string description)
    {
        AuditLog.Insert(0, new AdminAuditEntry(DateTimeOffset.UtcNow, playerName, description));
        while (AuditLog.Count > Settings.AuditLogStore.MaxEntries) AuditLog.RemoveAt(AuditLog.Count - 1);

        Settings.AuditLogStore.Save(AuditLog);
        OnPropertyChanged(nameof(HasAuditEntries));
    }
}
