using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Images;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Hileler sekmesindeki kategoriler.</summary>
public enum CheatCategory { Health, Gold, Clan, Character, Army, Compensation }

/// <summary>Açılır listelerde bir seçenek.</summary>
public sealed record FilterOption(string Key, string Value);

/// <summary>Seçilebilir bir asker türü (gerçek portresiyle).</summary>
public sealed class TroopChoice(TroopType troop, string cultureName, string className)
{
    public TroopType Troop { get; } = troop;
    public string Id => Troop.Id;
    public string Name => Troop.Name;
    public string CultureName { get; } = cultureName;
    public string ClassName { get; } = className;
    public string TierText => new('★', Math.Clamp(Troop.Tier, 1, 6));
    public GameImage Image => GameImages.Get(GameImageKind.Troop, Troop.Name, Troop.Id);
}

/// <summary>Verilecek asker sepetindeki satır.</summary>
public sealed partial class TroopBasketLine(TroopChoice troop, int count) : ObservableObject
{
    public TroopChoice Troop { get; } = troop;
    [ObservableProperty] private decimal? _count = count;
}

/// <summary>Oyuncunun ordusundaki bir satır (çıkarmak için).</summary>
public sealed partial class RosterLine(RosterEntry entry, string name) : ObservableObject
{
    public RosterEntry Entry { get; } = entry;
    public string Name { get; } = name;
    public string CountText => Entry.Wounded > 0 ? $"{Entry.Number} ({Entry.Wounded}✚)" : Entry.Number.ToString();
    public GameImage Image => GameImages.Get(GameImageKind.Troop, Name, Entry.TroopId);
    [ObservableProperty] private decimal? _remove;
}

/// <summary>Kaydedilmiş telafi paketi: altın + asker + can.</summary>
public sealed record CompensationPackage(string Name, int Gold, bool Heal, List<PackageTroop> Troops)
{
    public string Summary => string.Join(" · ", new[]
    {
        Gold > 0 ? Gold.ToString("N0") + " ⛁" : null,
        Troops.Count > 0 ? Troops.Sum(t => t.Count) + " ⚔" : null,
        Heal ? "♥" : null,
    }.Where(s => s is not null));
}

public sealed record PackageTroop(string Id, string Name, int Count);

/// <summary>Geri alınabilir bir işlem.</summary>
public sealed record CheatHistoryEntry(DateTimeOffset At, string Text, Func<Task>? Undo)
{
    public string TimeText => At.ToLocalTime().ToString("HH:mm:ss");
    public bool CanUndo => Undo is not null;
}

/// <summary>
/// Hileler sekmesi: sunucu sahibinin oyunculara uyguladığı işlemler.
/// </summary>
/// <remarks>
/// <para>
/// Oyuncuların kendi hileleri dünya ayarındaki "Hile koruması" ile kapalı; bu sekme hile
/// yapan birinin verdiği zararı telafi etmek için var. Her işlem sunucunun yanıtıyla
/// doğrulanır ve denetim kaydına yazılır.
/// </para>
/// <para>
/// Komutlar ve yanıtları 2026-10-16'da Coop v0.1.6'da denendi
/// (<c>Fixtures/troops-clans.txt</c>):
/// </para>
/// <list type="bullet">
/// <item>Asker türü ve sayısı: <c>set_troop_state</c> — ver de al da.</item>
/// <item>Nüfuz: <c>add_influence</c> eksi değeri de kabul ediyor.</item>
/// <item><b>Ün</b>, özellik ve odak puanı eksi değerle azaltılamıyor (sunucu "verildi"
/// diyor ama değer değişmiyor ya da doğrulanamıyor); bu yüzden "al" düğmeleri yok.</item>
/// <item>Belirli bir eşya veren sunucu komutu yok; eşya kaybı değeri kadar altınla
/// telafi edilir (değer <c>item_object.data</c>'dan).</item>
/// </list>
/// </remarks>
public partial class MainViewModel
{
    // --- hedefler ---------------------------------------------------------

    /// <summary>
    /// İşaretli karakterler. v0.5: seçili karaktere düşülmez; kimse seçilmeden panel ve
    /// işlemler görünmez (100 kişilik sunucuda yanlış kişiye işlem yapılmasın).
    /// </summary>
    private IReadOnlyList<HeroRow> AdminTargets => Heroes.Where(h => h.IsTarget).ToList();

    public bool HasCheatTarget => Heroes.Any(h => h.IsTarget);

    /// <summary>Hileler sekmesindeki oyuncu araması.</summary>
    [ObservableProperty] private string _cheatHeroFilter = "";

    public ObservableCollection<HeroRow> CheatVisibleHeroes { get; } = [];

    partial void OnCheatHeroFilterChanged(string value) => ApplyCheatHeroFilter();

    private void ApplyCheatHeroFilter()
    {
        CheatVisibleHeroes.Clear();
        foreach (var h in FilterHeroes(Heroes, CheatHeroFilter)) CheatVisibleHeroes.Add(h);
    }

    /// <summary>Satıra tıklanınca: yalnız o oyuncu hedef olur (çoklu seçim için kutucuklar).</summary>
    [RelayCommand]
    private void SelectCheatHero(HeroRow? hero)
    {
        if (hero is null) return;
        foreach (var h in Heroes) h.IsTarget = h == hero;
        SelectedHero = hero;
        NotifyTargets();
    }

    private SkillSheet? _cheatSheet;

    /// <summary>Karakter sekmesindeki beceri ekranı (odaktaki oyuncu).</summary>
    public SkillSheet CheatSheet => _cheatSheet ??= NewSkillSheet();

    private void SyncCheatSheet()
    {
        if (CheatSheet.Hero != CheatFocus) CheatSheet.Hero = CheatFocus;
        if (CheatCategory == CheatCategory.Character && CheatFocus is not null && IsServing && ServerToolsAvailable
            && !CheatSheet.HasStats && !CheatSheet.Loading)
            _ = CheatSheet.LoadAsync();
    }

    public string AdminTargetText => AdminTargets.Count switch
    {
        0 => L["cheat.noTarget"],
        1 => AdminTargets[0].Name,
        var n => string.Format(L["cheat.targetsN"], n),
    };

    /// <summary>Canlı bilgileri gösterilen karakter.</summary>
    public HeroRow? CheatFocus => AdminTargets.FirstOrDefault();

    public bool AllTargetsChecked
    {
        get => Heroes.Count > 0 && Heroes.All(h => h.IsTarget);
        set { foreach (var h in Heroes) h.IsTarget = value; }
    }

    /// <summary>Denetim kaydına eklenecek isteğe bağlı not.</summary>
    [ObservableProperty] private string _adminNote = "";

    private void OnHeroesReloaded()
    {
        foreach (var h in Heroes)
        {
            h.PropertyChanged -= OnHeroPropertyChanged;
            h.PropertyChanged += OnHeroPropertyChanged;
        }

        NotifyTargets();
        ApplyCheatHeroFilter();
        SyncMapPlayers();
    }

    private void OnHeroPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeroRow.IsTarget)) NotifyTargets();
        if (e.PropertyName == nameof(HeroRow.IsPriority)) OnPriorityChanged();
        if (e.PropertyName == nameof(HeroRow.Roster) && sender == CheatFocus) RebuildRosterLines();
    }

    private void NotifyTargets()
    {
        OnPropertyChanged(nameof(AdminTargetText));
        OnPropertyChanged(nameof(CheatFocus));
        OnPropertyChanged(nameof(AllTargetsChecked));
        OnPropertyChanged(nameof(HasCheatTarget));
        RebuildRosterLines();
        NotifyAdminCommands();
        SyncCheatSheet();
    }

    // --- kategoriler ------------------------------------------------------

    [ObservableProperty] private CheatCategory _cheatCategory = CheatCategory.Gold;

    public bool CatHealth { get => CheatCategory == CheatCategory.Health; set { if (value) CheatCategory = CheatCategory.Health; } }
    public bool CatGold { get => CheatCategory == CheatCategory.Gold; set { if (value) CheatCategory = CheatCategory.Gold; } }
    public bool CatClan { get => CheatCategory == CheatCategory.Clan; set { if (value) CheatCategory = CheatCategory.Clan; } }
    public bool CatCharacter { get => CheatCategory == CheatCategory.Character; set { if (value) CheatCategory = CheatCategory.Character; } }
    public bool CatArmy { get => CheatCategory == CheatCategory.Army; set { if (value) CheatCategory = CheatCategory.Army; } }
    public bool CatCompensation { get => CheatCategory == CheatCategory.Compensation; set { if (value) CheatCategory = CheatCategory.Compensation; } }

    partial void OnCheatCategoryChanged(CheatCategory value)
    {
        foreach (var name in new[] { nameof(CatHealth), nameof(CatGold), nameof(CatClan), nameof(CatCharacter), nameof(CatArmy), nameof(CatCompensation) })
            OnPropertyChanged(name);

        if (value == CheatCategory.Army && CheatFocus is { } h && IsServing) _ = RefreshRosterAsync(h);
        SyncCheatSheet();
    }

    // --- tutarlar ---------------------------------------------------------

    [ObservableProperty] private string _healthAmount = "50";
    [ObservableProperty] private string _goldAmount = "10000";
    [ObservableProperty] private string _ageAmount = "25";
    [ObservableProperty] private string _influenceAmount = "100";
    [ObservableProperty] private string _renownAmount = "50";
    [ObservableProperty] private string _troopXpAmount = "1000";
    private static bool TryParseAmount(string? text, out int amount) =>
        int.TryParse((text ?? "").Replace(".", "").Replace(",", "").Trim(), out amount) && amount > 0;

    // --- can --------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task HealHeroAsync()
    {
        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            // "Tam can" canı düşürmemeli: perklerle can 100'ü geçebiliyor.
            if (hero.Health >= AdminCommand.FullHitPoints) continue;
            await SetHealthAsync(hero, AdminCommand.FullHitPoints, L["audit.heal"]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task HealthGiveAsync() => ChangeHealthAsync(+1);

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task HealthTakeAsync() => ChangeHealthAsync(-1);

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task HealthSetAsync()
    {
        if (!TryParseAmount(HealthAmount, out var hp)) { ShowToast(L["msg.amountInvalid"], ToastKind.Warning); return; }
        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            await SetHealthAsync(hero, hp, string.Format(L["audit.healthSet"], hp));
        }
    }

    private async Task ChangeHealthAsync(int sign)
    {
        if (!TryParseAmount(HealthAmount, out var amount)) { ShowToast(L["msg.amountInvalid"], ToastKind.Warning); return; }
        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            if (hero.Health is not { } current) { ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error); continue; }

            var target = Math.Clamp(current + sign * amount, 1, AdminCommand.FullHitPoints);
            await SetHealthAsync(hero, target, string.Format(sign > 0 ? L["audit.healthGive"] : L["audit.healthTake"], amount));
        }
    }

    private async Task SetHealthAsync(HeroRow hero, int value, string audit)
    {
        var before = hero.Health;
        var (ok, _) = await RunAdminAsync(hero, AdminCommand.SetHitPoints(hero.HeroId, value), audit);
        if (ok && before is { } old)
            Remember(hero, audit, () => RunAdminAsync(hero, AdminCommand.SetHitPoints(hero.HeroId, old), L["audit.undo"]));
    }

    // --- altın ------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task AddGoldAsync(string? amountText) => ChangeGoldAsync(amountText ?? GoldAmount, +1);

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task GoldGiveAsync() => ChangeGoldAsync(GoldAmount, +1);

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task GoldTakeAsync() => ChangeGoldAsync(GoldAmount, -1);

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task SetGoldAsync()
    {
        if (!TryParseAmount(GoldAmount, out var gold)) { ShowToast(L["msg.goldInvalid"], ToastKind.Warning); return; }

        gold = Math.Min(gold, AdminCommand.MaxGold);
        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            await SetHeroGoldAsync(hero, gold, string.Format(L["audit.gold"], gold.ToString("N0")));
        }
    }

    private async Task ChangeGoldAsync(string amountText, int sign)
    {
        if (!TryParseAmount(amountText, out var amount)) { ShowToast(L["msg.goldInvalid"], ToastKind.Warning); return; }

        foreach (var hero in AdminTargets)
        {
            // Komut atama yapıyor; eklemek/çıkarmak için önce güncel altını okuyoruz.
            await RefreshHeroAsync(hero);
            if (hero.Gold is not { } current) { ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error); continue; }

            var target = Math.Clamp(current + sign * amount, 0, AdminCommand.MaxGold);
            await SetHeroGoldAsync(hero, target,
                string.Format(sign > 0 ? L["audit.goldAdd"] : L["audit.goldTake"], amount.ToString("N0")));
        }
    }

    private async Task SetHeroGoldAsync(HeroRow hero, int value, string audit)
    {
        var before = hero.Gold;
        var (ok, _) = await RunAdminAsync(hero, AdminCommand.SetGold(hero.HeroId, value), audit);
        if (ok && before is { } old)
            Remember(hero, audit, () => RunAdminAsync(hero, AdminCommand.SetGold(hero.HeroId, old), L["audit.undo"]));
    }

    // --- klan -------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task InfluenceGiveAsync() => ChangeInfluenceAsync(+1);

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private Task InfluenceTakeAsync() => ChangeInfluenceAsync(-1);

    private async Task ChangeInfluenceAsync(int sign)
    {
        if (!TryParseAmount(InfluenceAmount, out var amount)) { ShowToast(L["msg.amountInvalid"], ToastKind.Warning); return; }

        foreach (var hero in AdminTargets)
        {
            if (hero.ClanId is not { Length: > 0 } clan) { ShowToast(string.Format(L["msg.noClan"], hero.Name), ToastKind.Warning); continue; }

            var audit = string.Format(sign > 0 ? L["audit.influence"] : L["audit.influenceTake"], amount);
            var (ok, lines) = await RunAdminAsync(hero, AdminCommand.AddInfluence(clan, sign * amount), audit, refresh: false);
            if (!ok) continue;

            if (GameStateParser.ParseInfluenceApplied(lines) is { } now) hero.Influence = now;
            Remember(hero, audit, () => RunAdminAsync(hero, AdminCommand.AddInfluence(clan, -sign * amount), L["audit.undo"], refresh: false));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddRenownAsync()
    {
        if (!TryParseAmount(RenownAmount, out var amount)) return;

        foreach (var hero in AdminTargets)
        {
            if (hero.ClanId is not { Length: > 0 } clan) { ShowToast(string.Format(L["msg.noClan"], hero.Name), ToastKind.Warning); continue; }
            var audit = string.Format(L["audit.renown"], amount);
            var (ok, _) = await RunAdminAsync(hero, AdminCommand.AddRenown(clan, amount), audit, refresh: false);
            if (ok) Remember(hero, audit, null);
        }
    }

    // --- karakter (beceriler SkillSheet'te) ----------------------------------

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task SetAgeAsync()
    {
        if (!int.TryParse(AgeAmount.Trim(), out var age)) { ShowToast(L["msg.ageInvalid"], ToastKind.Warning); return; }

        age = Math.Clamp(age, AdminCommand.MinAge, AdminCommand.MaxAge);
        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            var before = hero.Age;
            var audit = string.Format(L["audit.age"], age);
            var (ok, _) = await RunAdminAsync(hero, AdminCommand.SetAge(hero.HeroId, age), audit);
            if (ok && before is { } old)
                Remember(hero, audit, () => RunAdminAsync(hero, AdminCommand.SetAge(hero.HeroId, old), L["audit.undo"]));
        }
    }

    // --- ordu: asker seçici -------------------------------------------------

    private IReadOnlyList<TroopChoice> _allTroops = [];
    private Dictionary<string, TroopChoice> _troopsById = [];

    public ObservableCollection<TroopChoice> TroopChoices { get; } = [];
    public ObservableCollection<TroopBasketLine> TroopBasket { get; } = [];
    public ObservableCollection<RosterLine> RosterLines { get; } = [];

    public IReadOnlyList<FilterOption> TroopCultures { get; private set; } = [];
    public IReadOnlyList<FilterOption> TroopClasses => new[]
    {
        new FilterOption("", L["troop.allClasses"]),
        new FilterOption("Infantry", L["troop.Infantry"]),
        new FilterOption("Ranged", L["troop.Ranged"]),
        new FilterOption("Cavalry", L["troop.Cavalry"]),
        new FilterOption("HorseArcher", L["troop.HorseArcher"]),
    };

    [ObservableProperty] private FilterOption? _troopCulture;
    [ObservableProperty] private FilterOption? _troopClass;
    [ObservableProperty] private string _troopSearch = "";
    [ObservableProperty] private decimal? _troopAddCount = 10;
    [ObservableProperty] private bool _armyRemoveMode;

    public bool ArmyGiveMode { get => !ArmyRemoveMode; set => ArmyRemoveMode = !value; }
    public bool HasTroopBasket => TroopBasket.Count > 0;
    public int TroopBasketTotal => TroopBasket.Sum(l => (int)(l.Count ?? 0));
    public bool HasRosterLines => RosterLines.Count > 0;

    partial void OnTroopCultureChanged(FilterOption? value) => FilterTroops();
    partial void OnTroopClassChanged(FilterOption? value) => FilterTroops();
    partial void OnTroopSearchChanged(string value) => FilterTroops();
    partial void OnArmyRemoveModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ArmyGiveMode));
        if (value && CheatFocus is { } h && IsServing) _ = RefreshRosterAsync(h);
    }

    private bool _cheatCatalogsLoaded;

    private void LoadCheatCatalogs()
    {
        if (_cheatCatalogsLoaded || _game is null) return;
        _cheatCatalogsLoaded = true;

        try
        {
            _allTroops = GameCatalogs.ReadTroops(GameCatalogs.TroopsPath(_game))
                .Select(t => new TroopChoice(t, CultureName(t.Culture), L["troop." + t.Class]))
                .ToList();
            _troopsById = _allTroops.ToDictionary(t => t.Id);
            TroopNames.Resolve = id => _troopsById.TryGetValue(id, out var t) ? t.Name : id;

            TroopCultures = new[] { new FilterOption("", L["troop.allCultures"]) }
                .Concat(_allTroops.Select(t => t.Troop.Culture).Distinct()
                    .Select(c => new FilterOption(c, CultureName(c)))
                    .OrderBy(c => c.Value))
                .ToList();
            OnPropertyChanged(nameof(TroopCultures));
            TroopCulture = TroopCultures[0];
            TroopClass = TroopClasses[0];

            LoadItemCatalog();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }

        FilterTroops();
        LoadPackages();
    }

    private void FilterTroops()
    {
        var search = TroopSearch.Trim();
        TroopChoices.Clear();
        foreach (var t in _allTroops)
        {
            if (!string.IsNullOrEmpty(TroopCulture?.Key) && t.Troop.Culture != TroopCulture.Key) continue;
            if (!string.IsNullOrEmpty(TroopClass?.Key) && t.Troop.Class != TroopClass.Key) continue;
            if (search.Length > 0 && !t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)) continue;
            TroopChoices.Add(t);
        }
    }

    public string CultureName(string culture) => L["culture." + culture] is var n && n != "culture." + culture ? n : culture;


    [RelayCommand]
    private void AddToTroopBasket(TroopChoice? troop)
    {
        if (troop is null) return;
        var count = (int)Math.Clamp(TroopAddCount ?? 10, 1, AdminCommand.MaxTroopCount);

        if (TroopBasket.FirstOrDefault(l => l.Troop == troop) is { } line) line.Count = (line.Count ?? 0) + count;
        else
        {
            var added = new TroopBasketLine(troop, count);
            added.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TroopBasketTotal));
            TroopBasket.Add(added);
        }

        OnPropertyChanged(nameof(HasTroopBasket));
        OnPropertyChanged(nameof(TroopBasketTotal));
    }

    [RelayCommand]
    private void RemoveFromTroopBasket(TroopBasketLine? line)
    {
        if (line is null) return;
        TroopBasket.Remove(line);
        OnPropertyChanged(nameof(HasTroopBasket));
        OnPropertyChanged(nameof(TroopBasketTotal));
    }

    /// <summary>Sepetteki askerleri her hedefe ekler (mevcut sayının üstüne).</summary>
    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task GiveTroopBasketAsync()
    {
        var basket = TroopBasket.Select(l => (l.Troop, Count: (int)(l.Count ?? 0))).Where(l => l.Count > 0).ToList();
        if (basket.Count == 0) return;

        foreach (var hero in AdminTargets)
            await GiveTroopsAsync(hero, basket.Select(b => (b.Troop.Id, b.Troop.Name, b.Count)).ToList());

        TroopBasket.Clear();
        OnPropertyChanged(nameof(HasTroopBasket));
        OnPropertyChanged(nameof(TroopBasketTotal));
    }

    private async Task GiveTroopsAsync(HeroRow hero, IReadOnlyList<(string Id, string Name, int Count)> troops)
    {
        if (!await EnsurePartyAsync(hero)) return;
        await RefreshRosterAsync(hero);

        var undo = new List<(string Id, int Old, int Wounded)>();
        foreach (var (id, name, count) in troops)
        {
            var current = hero.Roster?.Members.FirstOrDefault(m => m.TroopId == id);
            var old = current?.Number ?? 0;
            var target = Math.Min(old + count, AdminCommand.MaxTroopCount);

            var (ok, _) = await RunAdminAsync(hero, AdminCommand.SetTroopState(hero.PartyId!, id, target, current?.Wounded ?? 0, current?.Xp ?? 0),
                string.Format(L["audit.troopGive"], count, name), refresh: false, quiet: true);
            if (ok) undo.Add((id, old, current?.Wounded ?? 0));
        }

        if (undo.Count == 0) return;

        var text = string.Format(L["audit.troopsGiven"], troops.Sum(t => t.Count));
        ShowToast($"{hero.Name}: {text}", ToastKind.Success);
        Remember(hero, text, async () =>
        {
            foreach (var (id, old, wounded) in undo)
                await RunAdminAsync(hero, AdminCommand.SetTroopState(hero.PartyId!, id, old, wounded), L["audit.undo"], refresh: false, quiet: true);
            await RefreshRosterAsync(hero);
        });
        await RefreshRosterAsync(hero);
    }

    private void RebuildRosterLines()
    {
        RosterLines.Clear();
        if (CheatFocus?.Roster is { } roster)
        {
            foreach (var m in roster.Members.Where(m => !m.IsHero).OrderByDescending(m => m.Number))
                RosterLines.Add(new RosterLine(m, _troopsById.TryGetValue(m.TroopId, out var t) ? t.Name : m.TroopId));
        }

        OnPropertyChanged(nameof(HasRosterLines));
    }

    /// <summary>Odaktaki karakterin ordusundan işaretlenen sayıda askeri çıkarır.</summary>
    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task RemoveRosterTroopsAsync()
    {
        if (CheatFocus is not { } hero || !await EnsurePartyAsync(hero)) return;

        var lines = RosterLines.Where(l => (l.Remove ?? 0) > 0).ToList();
        if (lines.Count == 0) return;

        var undo = new List<(string Id, int Old, int Wounded)>();
        var removed = 0;
        foreach (var line in lines)
        {
            var remove = (int)Math.Min(line.Remove ?? 0, line.Entry.Number);
            var target = line.Entry.Number - remove;
            var (ok, _) = await RunAdminAsync(hero,
                AdminCommand.SetTroopState(hero.PartyId!, line.Entry.TroopId, target, Math.Min(line.Entry.Wounded, target), line.Entry.Xp),
                string.Format(L["audit.troopTake"], remove, line.Name), refresh: false, quiet: true);
            if (!ok) continue;
            undo.Add((line.Entry.TroopId, line.Entry.Number, line.Entry.Wounded));
            removed += remove;
        }

        if (undo.Count > 0)
        {
            var text = string.Format(L["audit.troopsTaken"], removed);
            ShowToast($"{hero.Name}: {text}", ToastKind.Success);
            Remember(hero, text, async () =>
            {
                foreach (var (id, old, wounded) in undo)
                    await RunAdminAsync(hero, AdminCommand.SetTroopState(hero.PartyId!, id, old, wounded), L["audit.undo"], refresh: false, quiet: true);
                await RefreshRosterAsync(hero);
            });
        }

        await RefreshRosterAsync(hero);
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task HealWoundedAsync()
    {
        foreach (var hero in AdminTargets)
        {
            if (!await EnsurePartyAsync(hero)) continue;
            await RefreshRosterAsync(hero);

            var wounded = hero.Roster?.Members.Where(m => !m.IsHero && m.Wounded > 0).ToList() ?? [];
            foreach (var m in wounded)
                await RunAdminAsync(hero, AdminCommand.SetTroopWounded(hero.PartyId!, m.TroopId, 0), L["audit.woundedHealed"], refresh: false, quiet: true);

            if (wounded.Count > 0)
            {
                Remember(hero, L["audit.woundedHealed"], null);
                ShowToast($"{hero.Name}: {L["audit.woundedHealed"]}", ToastKind.Success);
            }

            await RefreshRosterAsync(hero);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task AddTroopXpAsync()
    {
        if (!TryParseAmount(TroopXpAmount, out var xp)) return;
        foreach (var hero in AdminTargets)
        {
            var audit = string.Format(L["audit.troopXp"], xp);
            var (ok, _) = await RunAdminAsync(hero, AdminCommand.AddTroopXp(hero.Name, xp), audit, refresh: false);
            if (ok) Remember(hero, audit, null);
        }
    }

    /// <summary>Parti kimliği bilinmiyorsa karakterden okur.</summary>
    private async Task<bool> EnsurePartyAsync(HeroRow hero)
    {
        if (hero.PartyId is null) await RefreshHeroAsync(hero);
        if (hero.PartyId is not null) return true;

        ShowToast(string.Format(L["msg.noParty"], hero.Name), ToastKind.Warning);
        return false;
    }

    // --- konum (ışınlama haritadan başlar) ----------------------------------

    private async Task TeleportAsync(SettlementInfo target)
    {
        foreach (var hero in AdminTargets)
        {
            if (!await EnsurePartyAsync(hero)) continue;
            var audit = string.Format(L["audit.teleport"], target.Name);
            var (ok, _) = await RunAdminAsync(hero, AdminCommand.MoveToSettlement(hero.PartyId!, target.Id), audit, refresh: false);
            if (ok) Remember(hero, audit, null);
        }
    }

    // --- telafi paketleri -------------------------------------------------

    public ObservableCollection<CompensationPackage> Packages { get; } = [];

    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private bool _packageHeal = true;

    public bool HasPackages => Packages.Count > 0;

    private static string PackagesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "packages.json");

    private void LoadPackages()
    {
        Packages.Clear();
        try
        {
            if (File.Exists(PackagesPath))
                foreach (var p in JsonSerializer.Deserialize<List<CompensationPackage>>(File.ReadAllText(PackagesPath)) ?? [])
                    Packages.Add(p);
        }
        catch (Exception e) when (e is IOException or JsonException) { }

        OnPropertyChanged(nameof(HasPackages));
    }

    private void SavePackages()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PackagesPath)!);
            File.WriteAllText(PackagesPath, JsonSerializer.Serialize(Packages.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }

        OnPropertyChanged(nameof(HasPackages));
    }

    /// <summary>Altın kutusundaki miktar + asker sepeti + "can doldur" bir paket olarak kaydedilir.</summary>
    [RelayCommand]
    private void SavePackage()
    {
        var name = PackageName.Trim();
        if (name.Length == 0) { ShowToast(L["msg.packageName"], ToastKind.Warning); return; }

        TryParseAmount(GoldAmount, out var gold);
        var troops = TroopBasket.Where(l => (l.Count ?? 0) > 0)
            .Select(l => new PackageTroop(l.Troop.Id, l.Troop.Name, (int)l.Count!.Value)).ToList();

        Packages.Remove(Packages.FirstOrDefault(p => p.Name == name)!);
        Packages.Add(new CompensationPackage(name, gold, PackageHeal, troops));
        SavePackages();
        PackageName = "";
        ShowToast(string.Format(L["msg.packageSaved"], name), ToastKind.Success);
    }

    [RelayCommand]
    private void DeletePackage(CompensationPackage? package)
    {
        if (package is null) return;
        Packages.Remove(package);
        SavePackages();
    }

    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task ApplyPackageAsync(CompensationPackage? package)
    {
        if (package is null) return;

        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            if (package.Heal && hero.Health < AdminCommand.FullHitPoints)
                await SetHealthAsync(hero, AdminCommand.FullHitPoints, L["audit.heal"]);
            if (package.Gold > 0 && hero.Gold is { } g)
                await SetHeroGoldAsync(hero, Math.Min(g + package.Gold, AdminCommand.MaxGold),
                    string.Format(L["audit.goldAdd"], package.Gold.ToString("N0")));
            if (package.Troops.Count > 0)
                await GiveTroopsAsync(hero, package.Troops.Select(t => (t.Id, t.Name, t.Count)).ToList());

            Audit(hero.Name, string.Format(L["audit.package"], package.Name));
        }
    }

    // --- geçmiş ve geri alma ----------------------------------------------

    public ObservableCollection<CheatHistoryEntry> CheatHistory { get; } = [];

    public bool HasCheatHistory => CheatHistory.Count > 0;

    private void Remember(HeroRow hero, string text, Func<Task<(bool, IReadOnlyList<string>)>>? undo) =>
        Remember(hero, text, undo is null ? null : async () => { await undo(); });

    private void Remember(HeroRow hero, string text, Func<Task>? undo)
    {
        CheatHistory.Insert(0, new CheatHistoryEntry(DateTimeOffset.Now, $"{hero.Name}: {text}", undo));
        while (CheatHistory.Count > 15) CheatHistory.RemoveAt(CheatHistory.Count - 1);
        OnPropertyChanged(nameof(HasCheatHistory));
    }

    [RelayCommand]
    private async Task UndoCheatAsync(CheatHistoryEntry? entry)
    {
        if (entry?.Undo is not { } undo) return;

        CheatHistory.Remove(entry);
        OnPropertyChanged(nameof(HasCheatHistory));
        await undo();
        ShowToast(L["msg.undone"], ToastKind.Info);
    }

    // --- ortak ------------------------------------------------------------

    /// <summary>
    /// Komutu gönderir, sunucunun yanıtını okur ve <b>yalnızca başarılıysa</b> kaydeder.
    /// </summary>
    private async Task<(bool Ok, IReadOnlyList<string> Lines)> RunAdminAsync(HeroRow hero, string command, string auditText,
        bool quiet = false, bool refresh = true)
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
                    if (!quiet) ShowToast($"{hero.Name}: {auditText}", ToastKind.Success);
                    if (refresh) _ = RefreshHeroAsync(hero);
                    return (true, lines);

                case AdminOutcome.NotFound:
                    ShowToast(string.Format(L["msg.heroNotFound"], hero.Name), ToastKind.Error);
                    return (false, lines);

                case AdminOutcome.Rejected:
                    ShowToast(string.Format(L["msg.adminRejected"], hero.Name), ToastKind.Error);
                    return (false, lines);

                case AdminOutcome.UnknownCommand:
                case AdminOutcome.InvalidArguments:
                    ShowToast(L["msg.adminUnsupported"], ToastKind.Error);
                    return (false, lines);

                default:
                    ShowToast(L["msg.adminNoResponse"], ToastKind.Warning);
                    return (false, lines);
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
            return (false, []);
        }
    }

    private bool CanAdminAct() => IsServing && !IsBusy && AdminTargets.Count > 0;

    private void NotifyAdminCommands()
    {
        foreach (var command in new IRelayCommand[]
                 {
                     HealHeroCommand, HealthGiveCommand, HealthTakeCommand, HealthSetCommand,
                     AddGoldCommand, GoldGiveCommand, GoldTakeCommand, SetGoldCommand,
                     InfluenceGiveCommand, InfluenceTakeCommand, AddRenownCommand,
                     SetAgeCommand,
                     GiveTroopBasketCommand, RemoveRosterTroopsCommand, HealWoundedCommand, AddTroopXpCommand,
                     ApplyPackageCommand, GiveItemBasketCommand, GiveItemBasketAsGoldCommand, TakeItemsCommand,
                     RefreshHeroesCommand, BlockPlayerCommand, KickCommand,
                 })
            command.NotifyCanExecuteChanged();
    }
}
