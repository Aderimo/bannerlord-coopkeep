using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Dünyaya kayıtlı bir oyuncunun kahramanı.</summary>
public sealed partial class HeroRow(string heroId, string? steamId) : ObservableObject
{
    public string HeroId { get; } = heroId;

    /// <summary>SteamID64 — gizlilik modunda gösterilmez.</summary>
    public string? SteamId { get; } = steamId;

    [ObservableProperty] private string _name = heroId;
    [ObservableProperty] private int? _gold;
    [ObservableProperty] private int? _health;
    [ObservableProperty] private int? _age;

    public string GoldText => Gold is { } g ? g.ToString("N0") : "—";
    public string HealthText => Health is { } h ? h.ToString() : "—";
    public string AgeText => Age is { } a ? a.ToString() : "—";

    /// <summary>Can çubuğu için 0-1 arası.</summary>
    public double HealthRatio => Health is { } h ? Math.Clamp(h / 100.0, 0, 1) : 0;

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Initial));
    partial void OnGoldChanged(int? value) => OnPropertyChanged(nameof(GoldText));
    partial void OnAgeChanged(int? value) => OnPropertyChanged(nameof(AgeText));

    partial void OnHealthChanged(int? value)
    {
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(HealthRatio));
    }
}

/// <summary>
/// Oyuncular sayfası: bağlı oyuncular, kahramanlar, yönetici işlemleri, engel listesi.
/// </summary>
/// <remarks>
/// <para>
/// <b>v0.1.0'daki hatalar:</b> (1) altın komutu Coop'tan kaldırılmıştı; (2) hedef olarak
/// oyuncunun Steam adı gönderiliyordu; (3) dünyada birden fazla oyuncu varsa hiçbir işlem
/// yapılmıyordu, çünkü canlı protokol oyuncuyu kahramana bağlamıyor.
/// </para>
/// <para>
/// Çözüm: işlemler bağlı oyunculara değil, dünyanın kayıtlı <b>kahramanlarına</b>
/// uygulanıyor. Liste save'in eş JSON'undan (SteamID → kahraman kimliği) geliyor, ad/altın/can
/// ise sunucudan <c>coop.debug.hero.info</c> ile okunuyor. Kimlikle çalıştığı için kaç oyuncu
/// olursa olsun hedef kesin.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    public ObservableCollection<HeroRow> Heroes { get; } = [];

    [ObservableProperty] private HeroRow? _selectedHero;

    public bool HasHeroes => Heroes.Count > 0;

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

        Heroes.Clear();
        foreach (var p in source?.Players ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.HeroId)) continue;
            Heroes.Add(new HeroRow(p.HeroId, p.ControllerId));
        }

        SelectedHero = Heroes.FirstOrDefault(h => h.HeroId == previous) ?? Heroes.FirstOrDefault();
        OnPropertyChanged(nameof(HasHeroes));
    }

    /// <summary>Kahramanların adını, altınını ve canını sunucudan okur.</summary>
    [RelayCommand(CanExecute = nameof(CanCommand))]
    private async Task RefreshHeroesAsync()
    {
        ReloadHeroes();
        if (!IsServing) return;

        foreach (var hero in Heroes.ToList())
            await RefreshHeroAsync(hero);
    }

    private async Task RefreshHeroAsync(HeroRow hero)
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
            }
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
    }

    // --- yönetici işlemleri ----------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task HealHeroAsync()
    {
        if (SelectedHero is not { } hero) return;

        // "Tam can" canı düşürmemeli: perklerle can 100'ü geçebiliyor.
        if (hero.Health >= AdminCommand.FullHitPoints)
        {
            ShowToast(string.Format(L["msg.alreadyHealthy"], hero.Name), ToastKind.Info);
            return;
        }

        await RunAdminAsync(hero, AdminCommand.Heal(hero.HeroId), L["audit.heal"]);
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task SetGoldAsync()
    {
        if (SelectedHero is not { } hero) return;

        if (!int.TryParse(GoldAmount.Replace(".", "").Replace(",", "").Trim(), out var gold) || gold < 0)
        {
            ShowToast(L["msg.goldInvalid"], ToastKind.Warning);
            return;
        }

        gold = Math.Min(gold, AdminCommand.MaxGold);
        await RunAdminAsync(hero, AdminCommand.SetGold(hero.HeroId, gold), string.Format(L["audit.gold"], gold.ToString("N0")));
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddGoldAsync(string? amountText)
    {
        if (SelectedHero is not { } hero || !int.TryParse(amountText, out var amount)) return;

        // Komut atama yapıyor; eklemek için önce güncel altını okuyoruz.
        await RefreshHeroAsync(hero);
        if (hero.Gold is not { } current)
        {
            ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error);
            return;
        }

        var target = Math.Clamp(current + amount, 0, AdminCommand.MaxGold);
        await RunAdminAsync(hero, AdminCommand.SetGold(hero.HeroId, target),
            string.Format(L["audit.goldAdd"], amount.ToString("N0")));
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task SetAgeAsync()
    {
        if (SelectedHero is not { } hero) return;

        if (!int.TryParse(AgeAmount.Trim(), out var age))
        {
            ShowToast(L["msg.ageInvalid"], ToastKind.Warning);
            return;
        }

        age = Math.Clamp(age, AdminCommand.MinAge, AdminCommand.MaxAge);
        await RunAdminAsync(hero, AdminCommand.SetAge(hero.HeroId, age), string.Format(L["audit.age"], age));
    }

    /// <summary>
    /// Komutu gönderir, sunucunun yanıtını okur ve <b>yalnızca başarılıysa</b> kaydeder.
    /// </summary>
    private async Task RunAdminAsync(HeroRow hero, string command, string auditText)
    {
        try
        {
            var lines = await _supervisor.SendAndCollectAsync(command, AdminResponseParser.IsTerminal);

            switch (AdminResponseParser.Classify(lines))
            {
                case AdminOutcome.Success:
                    Audit(hero.Name, auditText);
                    ShowToast($"{hero.Name}: {auditText}");
                    await RefreshHeroAsync(hero);
                    break;

                case AdminOutcome.NotFound:
                    ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error);
                    break;

                case AdminOutcome.UnknownCommand:
                    ShowToast(L["msg.adminUnsupported"], ToastKind.Error);
                    break;

                default:
                    ShowToast(L["msg.adminNoResponse"], ToastKind.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    private bool CanAdminAct() => IsServing && !IsBusy && SelectedHero is not null;

    private void NotifyAdminCommands()
    {
        HealHeroCommand.NotifyCanExecuteChanged();
        SetGoldCommand.NotifyCanExecuteChanged();
        AddGoldCommand.NotifyCanExecuteChanged();
        SetAgeCommand.NotifyCanExecuteChanged();
        RefreshHeroesCommand.NotifyCanExecuteChanged();
        BlockPlayerCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedHeroChanged(HeroRow? value) => NotifyAdminCommands();

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

    // --- engel listesi ----------------------------------------------------

    /// <summary>
    /// Sunucuya girmesi istenmeyen oyuncu adları.
    /// </summary>
    /// <remarks>
    /// Coop'ta kalıcı ban yok ve bağlantı dışarıdan reddedilemiyor. Ama <c>kick</c> çalışıyor:
    /// listedeki biri bağlandığı anda CoopKeep onu atar. Ad bazlı olduğu için kesin bir
    /// koruma değil (ad değiştirilebilir); arayüz bunu açıkça söylüyor.
    /// </remarks>
    public ObservableCollection<string> BlockList { get; } = [];

    [ObservableProperty] private string _blockNameInput = "";

    public bool HasBlockList => BlockList.Count > 0;

    [RelayCommand(CanExecute = nameof(CanBlockPlayer))]
    private async Task BlockPlayerAsync(ConnectedPlayer? player)
    {
        player ??= SelectedPlayer;
        if (player is null) return;

        AddToBlockList(player.Name);
        await KickAsync(player);
    }

    private bool CanBlockPlayer(ConnectedPlayer? player) => CanCommand() && (player ?? SelectedPlayer) is not null;

    [RelayCommand]
    private void AddBlockName()
    {
        AddToBlockList(BlockNameInput);
        BlockNameInput = "";
    }

    [RelayCommand]
    private void RemoveBlockName(string name)
    {
        BlockList.Remove(name);
        Settings.BlockListStore.Save(BlockList);
        OnPropertyChanged(nameof(HasBlockList));
    }

    private void AddToBlockList(string? name)
    {
        var clean = ServerCommand.SanitizeArgument(name, 64);
        if (clean.Length == 0 || BlockList.Contains(clean, StringComparer.OrdinalIgnoreCase)) return;

        BlockList.Add(clean);
        Settings.BlockListStore.Save(BlockList);
        OnPropertyChanged(nameof(HasBlockList));
        ShowToast(string.Format(L["msg.blocked"], clean));
    }

    /// <summary>Yeni bağlanan oyuncu: engelliyse atılır, değilse kahraman listesi tazelenir.</summary>
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

        // İlk kez katılan oyuncunun kahramanı save'e ancak bir süre sonra yazılıyor.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshHeroesAsync()));
    }
}
