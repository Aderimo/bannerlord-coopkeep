using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Images;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Kuşanımdaki bir parça, gerçek görseliyle.</summary>
public sealed class EquipmentCard(EquipmentPiece piece, string slotName)
{
    public EquipmentPiece Piece { get; } = piece;
    public string SlotName { get; } = slotName;
    public string Name => Piece.Name;
    public GameImage Image => GameImages.Get(GameImageKind.Item, Piece.Name, Piece.ItemId);
}

/// <summary>Ordudaki bir asker satırı (profil).</summary>
public sealed class ArmyCard(ToolsTroop troop)
{
    public ToolsTroop Troop { get; } = troop;
    public string Name => Troop.Name;
    public string CountText => Troop.Wounded > 0 ? $"{Troop.Number} ({Troop.Wounded}✚)" : Troop.Number.ToString("N0");
    public GameImage Image => GameImages.Get(GameImageKind.Troop, Troop.Name, Troop.Id);
}

/// <summary>
/// Oyuncu profili: Oyuncular, Hileler ve Harita'dan açılan ortak pencere.
/// </summary>
/// <remarks>
/// Üst bilgi diskteki save'den ve <c>hero.info</c>'dan; beceriler, kuşanım, envanter ve ordu
/// CoopKeep sunucu eklentisinden okunur. Sunucu kapalıysa ya da eklenti yoksa yalnızca
/// kayıtlı bilgiler gösterilir ve neden eksik olduğu yazılır.
/// </remarks>
public partial class MainViewModel
{
    [ObservableProperty] private bool _profileOpen;
    [ObservableProperty] private HeroRow? _profileHero;
    [ObservableProperty] private bool _profileLoading;

    /// <summary>0: beceriler, 1: kuşanım, 2: envanter, 3: ordu.</summary>
    [ObservableProperty] private int _profileTab;

    public bool ProfileTabSkills { get => ProfileTab == 0; set { if (value) ProfileTab = 0; } }
    public bool ProfileTabGear { get => ProfileTab == 1; set { if (value) ProfileTab = 1; } }
    public bool ProfileTabInventory { get => ProfileTab == 2; set { if (value) ProfileTab = 2; } }
    public bool ProfileTabArmy { get => ProfileTab == 3; set { if (value) ProfileTab = 3; } }

    partial void OnProfileTabChanged(int value)
    {
        OnPropertyChanged(nameof(ProfileTabSkills));
        OnPropertyChanged(nameof(ProfileTabGear));
        OnPropertyChanged(nameof(ProfileTabInventory));
        OnPropertyChanged(nameof(ProfileTabArmy));
    }

    private SkillSheet? _profileSheet;

    public SkillSheet ProfileSheet => _profileSheet ??= NewSkillSheet();

    public ObservableCollection<EquipmentCard> ProfileBattleGear { get; } = [];
    public ObservableCollection<EquipmentCard> ProfileCivilianGear { get; } = [];
    public ObservableCollection<InventoryLine> ProfileInventory { get; } = [];
    public ObservableCollection<ArmyCard> ProfileArmy { get; } = [];
    public ObservableCollection<ArmyCard> ProfilePrisoners { get; } = [];

    public bool ProfileLive => (IsServing && ServerToolsAvailable) || ProfileSheet.HasStats;

    /// <summary>Canlı bilgiler neden yok: sunucu kapalı ya da eklenti kapalı.</summary>
    public string ProfileLiveNote => ProfileSheet.HasStats ? "" : !IsServing ? L["profile.offline"] : !ServerToolsAvailable ? L["profile.noTools"] : "";

    public bool HasProfileLiveNote => ProfileLiveNote.Length > 0;
    public bool HasProfileInventory => ProfileInventory.Count > 0;
    public bool HasProfileArmy => ProfileArmy.Count > 0;
    public bool HasProfilePrisoners => ProfilePrisoners.Count > 0;
    public bool HasProfileGear => ProfileBattleGear.Count + ProfileCivilianGear.Count > 0;

    /// <summary>Durum kutucuğundaki asker sayısı: profilin okuduğu kadro, yoksa son bilinen.</summary>
    public string ProfileTroopsText => ProfileArmy.Count > 0
        ? ProfileArmy.Sum(a => a.Troop.Number).ToString("N0")
        : ProfileHero?.TroopsText ?? "—";

    public string ProfileArmyText => string.Format(L["profile.armyCount"],
        ProfileArmy.Sum(a => a.Troop.Number), ProfileArmy.Sum(a => a.Troop.Wounded));

    public string ProfileInventoryText => string.Format(L["profile.inventoryCount"],
        ProfileInventory.Sum(i => i.Have), ProfileInventory.Sum(i => i.Card.Item.Value * i.Have).ToString("N0"));

    /// <summary>Profildeki oyuncu bağlıysa bağlantısı (atmak için).</summary>
    private ConnectedPlayer? ProfilePlayer => ProfileHero is { } h
        ? Players.FirstOrDefault(p => string.Equals(p.Name, h.Name, StringComparison.OrdinalIgnoreCase))
        : null;

    public bool ProfileCanKick => ProfilePlayer is not null && IsServing;

    private SkillSheet NewSkillSheet() => new(() => L,
        async (hero, command, audit) => (await RunAdminAsync(hero, command, audit, refresh: false)).Ok,
        command => _supervisor.SendAndCollectAsync(command, IsToolsTerminal, TimeSpan.FromSeconds(5)));

    /// <summary>Eklenti komutunun çıktısı bitti mi?</summary>
    private static bool IsToolsTerminal(string line) =>
        line.Trim() == ServerToolsParser.End
        || line.StartsWith("COOPKEEP_ERROR", StringComparison.Ordinal)
        || line.StartsWith("COOPKEEP_DEV", StringComparison.Ordinal)
        || line.Contains("no game console command", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Usage:", StringComparison.Ordinal);

    /// <summary>Profili açar. Hedef: kayıtlı karakter, bağlı oyuncu ya da haritadaki oyuncu.</summary>
    [RelayCommand]
    private async Task OpenProfileAsync(object? target)
    {
        var hero = target switch
        {
            HeroRow h => h,
            MapPlayer m => m.Hero,
            ConnectedPlayer p => Heroes.FirstOrDefault(h => string.Equals(h.Name, p.Name, StringComparison.OrdinalIgnoreCase)),
            _ => SelectedHero,
        };

        if (hero is null)
        {
            if (target is ConnectedPlayer cp) ShowToast(string.Format(L["profile.noHero"], cp.Name), ToastKind.Info);
            return;
        }

        ProfileHero = hero;
        ProfileTab = 0;
        ProfileOpen = true;
        await LoadProfileAsync();
    }

    [RelayCommand]
    private void CloseProfile() => ProfileOpen = false;

    [RelayCommand]
    private Task ReloadProfileAsync() => LoadProfileAsync();

    private async Task LoadProfileAsync()
    {
        if (ProfileHero is not { } hero) return;

        ProfileBattleGear.Clear();
        ProfileCivilianGear.Clear();
        ProfileInventory.Clear();
        ProfileArmy.Clear();
        ProfilePrisoners.Clear();
        ProfileSheet.Hero = hero;
        NotifyProfile();

        if (!IsServing) return;

        ProfileLoading = true;
        try
        {
            await RefreshHeroAsync(hero, full: !ServerToolsAvailable);
            if (!ServerToolsAvailable || ProfileHero != hero) return;

            await ProfileSheet.LoadAsync();

            var gear = ServerToolsParser.ParseEquipment(
                await _supervisor.SendAndCollectAsync(AdminCommand.ToolsHeroEquipment(hero.HeroId), IsToolsTerminal, TimeSpan.FromSeconds(5)));
            foreach (var piece in gear)
                (piece.Set == "civilian" ? ProfileCivilianGear : ProfileBattleGear).Add(new EquipmentCard(piece, L["slot." + piece.Slot]));

            if (ServerToolsParser.ParseRoster(
                    await _supervisor.SendAndCollectAsync(AdminCommand.ToolsRoster(hero.HeroId), IsToolsTerminal, TimeSpan.FromSeconds(5))) is { } roster)
            {
                foreach (var t in roster.Members.Where(m => !m.IsHero)) ProfileArmy.Add(new ArmyCard(t));
                foreach (var t in roster.Prisoners) ProfilePrisoners.Add(new ArmyCard(t));
            }

            if (_itemCards.Count == 0) LoadItemCatalog();
            var items = ServerItemCatalog.ParsePartyItems(await _supervisor.SendAndCollectAsync(AdminCommand.PartyItems(hero.HeroId),
                l => l.StartsWith("COOPKEEP_ITEMS_END", StringComparison.Ordinal) || l.StartsWith("COOPKEEP_ERROR", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5)));
            foreach (var (id, count) in items.OrderByDescending(kv => (_itemCardsById.GetValueOrDefault(kv.Key)?.Item.Value ?? 0) * kv.Value))
            {
                var card = _itemCardsById.GetValueOrDefault(id)
                           ?? new ItemCard(new ServerItem(id, id, "", "", 0, 0, "", false, false, false, "", 0), null, "", "");
                ProfileInventory.Add(new InventoryLine(card, count));
            }
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
        finally
        {
            ProfileLoading = false;
            NotifyProfile();
        }
    }

    private void NotifyProfile()
    {
        foreach (var name in new[] { nameof(ProfileLive), nameof(ProfileLiveNote), nameof(HasProfileLiveNote), nameof(HasProfileInventory),
                     nameof(HasProfileArmy), nameof(HasProfilePrisoners), nameof(HasProfileGear), nameof(ProfileArmyText),
                     nameof(ProfileInventoryText), nameof(ProfileCanKick), nameof(ProfileTroopsText) })
            OnPropertyChanged(name);
    }

    // --- hızlı eylemler ------------------------------------------------------

    [RelayCommand]
    private async Task ProfileKickAsync()
    {
        if (ProfilePlayer is { } p) await KickAsync(p);
        NotifyProfile();
    }

    [RelayCommand]
    private async Task ProfileBanAsync()
    {
        if (ProfileHero is { } h) await BanHeroAsync(h);
    }

    [RelayCommand]
    private void ProfileOpenCheats()
    {
        if (ProfileHero is not { } hero) return;
        foreach (var h in Heroes) h.IsTarget = h == hero;
        SelectedHero = hero;
        ProfileOpen = false;
        CurrentPage = AppPage.Cheats;
    }

    [RelayCommand]
    private void ProfileOpenItems()
    {
        if (ProfileHero is not { } hero) return;
        ProfileOpen = false;
        OpenItemBrowser(hero);
    }

    [RelayCommand]
    private void ProfileShowOnMap()
    {
        if (ProfileHero is not { } hero) return;
        ProfileOpen = false;
        CurrentPage = AppPage.Map;
        if (MapPlayers.FirstOrDefault(p => p.Hero == hero) is { } pin) SelectMapPlayer(pin);
    }
}
