using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>SteamID64 — gizlilik modunda gösterilmez.</summary>
    public string? SteamId { get; } = steamId;

    /// <summary>Kayıtlı klan kimliği (nüfuz ve ün komutları için), save'in eş JSON'undan.</summary>
    public string? ClanId { get; } = clanId;

    [ObservableProperty] private string? _partyId = partyId;
    [ObservableProperty] private string _name = heroId;
    [ObservableProperty] private string? _clanName;
    [ObservableProperty] private int? _gold;
    [ObservableProperty] private int? _health;
    [ObservableProperty] private int? _age;

    /// <summary>Haritadan gelen son parti bilgisi.</summary>
    [ObservableProperty] private PartyInfo? _party;

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
/// Oyuncular sayfası: bağlı oyuncular, karakterler, yönetici araçları, engel listesi.
/// </summary>
/// <remarks>
/// <para>
/// İşlemler bağlı oyunculara değil, dünyanın kayıtlı <b>karakterlerine</b> uygulanıyor.
/// Liste save'in eş JSON'undan (SteamID → karakter, klan, parti kimliği) geliyor; ad,
/// altın, can ve parti ise sunucudan <c>coop.debug.hero.info</c> ile okunuyor.
/// </para>
/// <para>
/// <b>Neden yönetici araçları:</b> oyuncuların hileleri dünya ayarındaki "Hile koruması"
/// ile kapalı tutulur. Hile yapan biri başkalarına zarar verirse, sunucu sahibi zararı
/// buradan telafi eder. Oyunun kendi konsol hileleri sunucuda kapalı ve açılamıyor
/// (2026-10-16'da denendi); bu yüzden yalnızca Coop'un kendi komutları kullanılıyor.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    public ObservableCollection<HeroRow> Heroes { get; } = [];

    [ObservableProperty] private HeroRow? _selectedHero;

    /// <summary>İşlemler tüm karakterlere mi uygulanacak?</summary>
    [ObservableProperty] private bool _adminTargetAll;

    public bool AdminTargetOne { get => !AdminTargetAll; set => AdminTargetAll = !value; }

    /// <summary>Denetim kaydına eklenecek isteğe bağlı not (örn. "hileciye karşı telafi").</summary>
    [ObservableProperty] private string _adminNote = "";

    [ObservableProperty] private string _influenceAmount = "100";
    [ObservableProperty] private string _renownAmount = "50";
    [ObservableProperty] private string _troopXpAmount = "1000";
    [ObservableProperty] private string _skillXpAmount = "1000";
    [ObservableProperty] private string _selectedSkill = "Leadership";
    [ObservableProperty] private decimal? _troopBatches = 1;

    public IReadOnlyList<string> Skills => AdminCommand.Skills;

    public bool HasHeroes => Heroes.Count > 0;

    public string AdminTargetText => AdminTargetAll
        ? string.Format(L["admin.targetAllN"], Heroes.Count)
        : SelectedHero?.Name ?? "—";

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
        OnPropertyChanged(nameof(HasHeroes));
        OnPropertyChanged(nameof(AdminTargetText));
    }

    /// <summary>Karakterlerin adını, altınını ve canını sunucudan okur.</summary>
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
                hero.ClanName = info.ClanName;
                if (info.PartyId is not null) hero.PartyId = info.PartyId;
            }
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
    }

    /// <summary>İşlemin uygulanacağı karakterler.</summary>
    private IReadOnlyList<HeroRow> AdminTargets =>
        AdminTargetAll ? Heroes.ToList() : SelectedHero is { } h ? [h] : [];

    // --- yönetici işlemleri ----------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task HealHeroAsync()
    {
        foreach (var hero in AdminTargets)
        {
            // "Tam can" canı düşürmemeli: perklerle can 100'ü geçebiliyor.
            if (hero.Health >= AdminCommand.FullHitPoints) continue;
            await RunAdminAsync(hero, AdminCommand.Heal(hero.HeroId), L["audit.heal"]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task SetGoldAsync()
    {
        if (!TryParseAmount(GoldAmount, out var gold)) { ShowToast(L["msg.goldInvalid"], ToastKind.Warning); return; }

        gold = Math.Min(gold, AdminCommand.MaxGold);
        foreach (var hero in AdminTargets)
            await RunAdminAsync(hero, AdminCommand.SetGold(hero.HeroId, gold), string.Format(L["audit.gold"], gold.ToString("N0")));
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddGoldAsync(string? amountText)
    {
        if (!TryParseAmount(amountText ?? GoldAmount, out var amount)) return;

        foreach (var hero in AdminTargets)
        {
            // Komut atama yapıyor; eklemek için önce güncel altını okuyoruz.
            await RefreshHeroAsync(hero);
            if (hero.Gold is not { } current)
            {
                ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error);
                continue;
            }

            var target = Math.Clamp(current + amount, 0, AdminCommand.MaxGold);
            await RunAdminAsync(hero, AdminCommand.SetGold(hero.HeroId, target),
                string.Format(L["audit.goldAdd"], amount.ToString("N0")));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task SetAgeAsync()
    {
        if (!int.TryParse(AgeAmount.Trim(), out var age)) { ShowToast(L["msg.ageInvalid"], ToastKind.Warning); return; }

        age = Math.Clamp(age, AdminCommand.MinAge, AdminCommand.MaxAge);
        foreach (var hero in AdminTargets)
            await RunAdminAsync(hero, AdminCommand.SetAge(hero.HeroId, age), string.Format(L["audit.age"], age));
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task GiveInfluenceAsync()
    {
        if (!TryParseAmount(InfluenceAmount, out var amount)) return;

        foreach (var hero in AdminTargets)
        {
            if (hero.ClanId is not { Length: > 0 } clan) { ShowToast(string.Format(L["msg.noClan"], hero.Name), ToastKind.Warning); continue; }
            await RunAdminAsync(hero, AdminCommand.GiveInfluence(clan, amount), string.Format(L["audit.influence"], amount));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddRenownAsync()
    {
        if (!TryParseAmount(RenownAmount, out var amount)) return;

        foreach (var hero in AdminTargets)
        {
            if (hero.ClanId is not { Length: > 0 } clan) { ShowToast(string.Format(L["msg.noClan"], hero.Name), ToastKind.Warning); continue; }
            await RunAdminAsync(hero, AdminCommand.AddRenown(clan, amount), string.Format(L["audit.renown"], amount));
        }
    }

    /// <summary>
    /// Asker ekler. Coop'un komutu tür ve sayı almıyor; sabit bir acemi grubu ekliyor.
    /// "Kaç kez" seçeneği bu grubu art arda ekleyerek miktarı ayarlamayı sağlar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddTroopsAsync()
    {
        var batches = Math.Clamp((int)(TroopBatches ?? 1), 1, 20);
        foreach (var hero in AdminTargets)
        {
            for (var i = 0; i < batches; i++)
            {
                var ok = await RunAdminAsync(hero, AdminCommand.AddTroops(hero.Name),
                    string.Format(L["audit.troops"], i + 1, batches), quiet: i < batches - 1);
                if (!ok) break;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddTroopXpAsync()
    {
        if (!TryParseAmount(TroopXpAmount, out var xp)) return;
        foreach (var hero in AdminTargets)
            await RunAdminAsync(hero, AdminCommand.AddTroopXp(hero.Name, xp), string.Format(L["audit.troopXp"], xp));
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddSkillXpAsync()
    {
        if (!TryParseAmount(SkillXpAmount, out var xp)) return;
        foreach (var hero in AdminTargets)
            await RunAdminAsync(hero, AdminCommand.AddSkillXp(hero.HeroId, SelectedSkill, xp),
                string.Format(L["audit.skillXp"], SelectedSkill, xp));
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddAttributePointAsync()
    {
        foreach (var hero in AdminTargets)
            await RunAdminAsync(hero, AdminCommand.AddAttributePoints(hero.HeroId, 1), L["audit.attribute"]);
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddFocusPointAsync()
    {
        foreach (var hero in AdminTargets)
            await RunAdminAsync(hero, AdminCommand.AddFocusPoints(hero.HeroId, 1), L["audit.focus"]);
    }

    private static bool TryParseAmount(string? text, out int amount) =>
        int.TryParse((text ?? "").Replace(".", "").Replace(",", "").Trim(), out amount) && amount > 0;

    /// <summary>
    /// Komutu gönderir, sunucunun yanıtını okur ve <b>yalnızca başarılıysa</b> kaydeder.
    /// </summary>
    /// <returns>İşlem uygulandıysa (ya da hatasız gönderildiyse) <see langword="true"/>.</returns>
    private async Task<bool> RunAdminAsync(HeroRow hero, string command, string auditText, bool quiet = false)
    {
        try
        {
            var lines = await _supervisor.SendAndCollectAsync(command, AdminResponseParser.IsTerminal);
            var note = AdminNote.Trim();
            var described = note.Length > 0 ? $"{auditText} — {note}" : auditText;

            switch (AdminResponseParser.Classify(lines))
            {
                case AdminOutcome.Success:
                case AdminOutcome.Sent:
                    Audit(hero.Name, described);
                    if (!quiet) ShowToast($"{hero.Name}: {auditText}");
                    _ = RefreshHeroAsync(hero);
                    return true;

                case AdminOutcome.NotFound:
                    ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error);
                    return false;

                case AdminOutcome.UnknownCommand:
                case AdminOutcome.InvalidArguments:
                    ShowToast(L["msg.adminUnsupported"], ToastKind.Error);
                    return false;

                default:
                    ShowToast(L["msg.adminNoResponse"], ToastKind.Warning);
                    return false;
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
            return false;
        }
    }

    private bool CanAdminAct() => IsServing && !IsBusy && (AdminTargetAll ? Heroes.Count > 0 : SelectedHero is not null);

    private void NotifyAdminCommands()
    {
        HealHeroCommand.NotifyCanExecuteChanged();
        SetGoldCommand.NotifyCanExecuteChanged();
        AddGoldCommand.NotifyCanExecuteChanged();
        SetAgeCommand.NotifyCanExecuteChanged();
        GiveInfluenceCommand.NotifyCanExecuteChanged();
        AddRenownCommand.NotifyCanExecuteChanged();
        AddTroopsCommand.NotifyCanExecuteChanged();
        AddTroopXpCommand.NotifyCanExecuteChanged();
        AddSkillXpCommand.NotifyCanExecuteChanged();
        AddAttributePointCommand.NotifyCanExecuteChanged();
        AddFocusPointCommand.NotifyCanExecuteChanged();
        RefreshHeroesCommand.NotifyCanExecuteChanged();
        BlockPlayerCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedHeroChanged(HeroRow? value)
    {
        NotifyAdminCommands();
        OnPropertyChanged(nameof(AdminTargetText));
    }

    partial void OnAdminTargetAllChanged(bool value)
    {
        OnPropertyChanged(nameof(AdminTargetOne));
        OnPropertyChanged(nameof(AdminTargetText));
        NotifyAdminCommands();
    }

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
    /// koruma değil (ad değiştirilebilir).
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

        // İlk kez katılan oyuncunun karakteri save'e ancak bir süre sonra yazılıyor.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshHeroesAsync()));
    }
}
