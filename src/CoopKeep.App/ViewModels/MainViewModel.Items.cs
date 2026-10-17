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

/// <summary>Eşya ekranında bir kart.</summary>
public sealed class ItemCard(ServerItem item, ItemPrice? price, string groupName, string cultureName)
{
    public ServerItem Item { get; } = item;
    public ItemPrice? Price { get; } = price;
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string GroupName { get; } = groupName;
    public string CultureName { get; } = cultureName;
    public string TierText => Item.Tier > 0 ? new string('★', Math.Min(Item.Tier, 6)) : "";
    public bool IsCivilian => Item.Civilian && Item.IsClothing;
    public bool HasPrice => Price is not null;

    /// <summary>Kasabalardaki en düşük ve en yüksek alış fiyatı.</summary>
    public string PriceRangeText => Price is { } p
        ? (p.Min == p.Max ? p.Min.ToString("N0") : $"{p.Min:N0} – {p.Max:N0}")
        : Item.Value.ToString("N0");

    public string PriceTip => Price is { } p
        ? $"▼ {p.MinTown}: {p.Min:N0}   ▲ {p.MaxTown}: {p.Max:N0}   ⌀ {p.Average:N0}"
        : "";

    /// <summary>Telafi hesabı için: kasaba ortalaması, yoksa taban değer.</summary>
    public int UnitValue => Price?.Average ?? Item.Value;

    public GameImage Image => GameImages.Get(GameImageKind.Item, Item.Name, Item.Id);
}

/// <summary>Verilecek eşya sepetindeki satır.</summary>
public sealed partial class ItemBasketEntry(ItemCard card, int count) : ObservableObject
{
    public ItemCard Card { get; } = card;
    [ObservableProperty] private decimal? _count = count;
}

/// <summary>Oyuncunun envanterindeki bir eşya (almak için).</summary>
public sealed partial class InventoryLine(ItemCard card, int have) : ObservableObject
{
    public ItemCard Card { get; } = card;
    public int Have { get; } = have;
    [ObservableProperty] private decimal? _remove;
}

/// <summary>Sol taraftaki grup listesi.</summary>
public sealed record ItemGroupOption(ItemGroup? Group, string Name, int Count, string Section);

/// <summary>
/// Eşya ekranı: oyundaki bütün eşyalar (silah, zırh, kıyafet, at, yiyecek, malzeme)
/// gruplara ayrılmış, aranabilir, kasaba fiyatlarıyla; seçilenler oyuncuya verilir.
/// </summary>
/// <remarks>
/// Eşyanın kendisini vermek CoopKeep sunucu eklentisini gerektirir
/// (<c>coopkeep.give_item</c>). Eklenti yoksa sepetin değeri kadar altın verilebilir.
/// </remarks>
public partial class MainViewModel
{
    private IReadOnlyList<ItemCard> _itemCards = [];
    private Dictionary<string, ItemCard> _itemCardsById = [];

    public ObservableCollection<ItemGroupOption> ItemGroups { get; } = [];
    public ObservableCollection<ItemCard> VisibleItems { get; } = [];
    public ObservableCollection<ItemBasketEntry> ItemBasket { get; } = [];
    public ObservableCollection<InventoryLine> InventoryLines { get; } = [];

    [ObservableProperty] private bool _itemBrowserOpen;
    [ObservableProperty] private ItemGroupOption? _selectedItemGroup;
    [ObservableProperty] private string _itemQuery = "";
    [ObservableProperty] private bool _itemTakeMode;
    [ObservableProperty] private decimal? _itemPerClick = 1;
    [ObservableProperty] private string _itemSort = "price";

    /// <summary>Kıyafette: 0 = hepsi, 1 = şehir içi (sivil), 2 = savaş.</summary>
    [ObservableProperty] private int _clothingFilter;

    [ObservableProperty] private string _itemResultText = "";

    public bool ItemGiveMode { get => !ItemTakeMode; set => ItemTakeMode = !value; }
    public bool ClothingAll { get => ClothingFilter == 0; set { if (value) ClothingFilter = 0; } }
    public bool ClothingCivilian { get => ClothingFilter == 1; set { if (value) ClothingFilter = 1; } }
    public bool ClothingBattle { get => ClothingFilter == 2; set { if (value) ClothingFilter = 2; } }
    public bool SortPrice { get => ItemSort == "price"; set { if (value) ItemSort = "price"; } }
    public bool SortPriceDesc { get => ItemSort == "priceDesc"; set { if (value) ItemSort = "priceDesc"; } }
    public bool SortName { get => ItemSort == "name"; set { if (value) ItemSort = "name"; } }
    public bool SortTier { get => ItemSort == "tier"; set { if (value) ItemSort = "tier"; } }

    public bool IsClothingGroup => SelectedItemGroup?.Group is ItemGroup.Head or ItemGroup.Body or ItemGroup.Legs or ItemGroup.Hands or ItemGroup.Cape
                                   || SelectedItemGroup?.Section == "armor";

    public bool HasItemBasket => ItemBasket.Count > 0;
    public int ItemBasketCount => ItemBasket.Sum(e => (int)(e.Count ?? 0));
    public int ItemBasketValue => ItemBasket.Sum(e => e.Card.UnitValue * (int)(e.Count ?? 0));
    public string ItemBasketValueText => ItemBasketValue.ToString("N0");
    public bool HasInventoryLines => InventoryLines.Count > 0;
    public bool HasItemCatalog => _itemCards.Count > 0;
    public bool ItemsNeedTools => !ServerToolsAvailable;

    /// <summary>Önbellekteki eşya listesi; yoksa oyunun XML'i (yiyeceksiz, fiyatsız).</summary>
    private void LoadItemCatalog()
    {
        var (items, prices) = ServerItemCatalog.Load(ItemCatalogPath);
        if (items.Count == 0 && _game is not null)
        {
            items = GameCatalogs.ReadItems(_game.ModulesDirectory)
                .Select(i => new ServerItem(i.Id, i.Name, i.Type == "Weapon" ? "OneHandedWeapon" : i.Type, "", 0, 0, i.Culture,
                    false, false, false, "", 0))
                .ToList();
        }

        SetServerItems(items, prices);
    }

    private void SetServerItems(IReadOnlyList<ServerItem> items, IReadOnlyList<ItemPrice> prices)
    {
        var priceById = prices.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        _itemCards = items
            .Where(i => i.Name.Length > 0 && i.Type is not ("Banner" or "Invalid") && !i.Id.Contains("tournament", StringComparison.Ordinal)
                        && !i.Name.StartsWith("Dummy", StringComparison.OrdinalIgnoreCase) && !i.Id.StartsWith("dummy", StringComparison.OrdinalIgnoreCase)
                        && i.Id != "trash_item" && !i.Name.Equals("Trash Item", StringComparison.OrdinalIgnoreCase))
            .Select(i => new ItemCard(i, priceById.GetValueOrDefault(i.Id), GroupName(i.Group), CultureName(i.Culture)))
            .ToList();
        _itemCardsById = _itemCards.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());

        var previous = SelectedItemGroup?.Group;
        ItemGroups.Clear();
        ItemGroups.Add(new ItemGroupOption(null, L["items.all"], _itemCards.Count, ""));
        foreach (var (section, groups) in Sections)
            foreach (var g in groups)
            {
                var count = _itemCards.Count(c => c.Item.Group == g);
                if (count > 0) ItemGroups.Add(new ItemGroupOption(g, GroupName(g), count, section));
            }

        SelectedItemGroup = ItemGroups.FirstOrDefault(o => o.Group == previous) ?? ItemGroups.FirstOrDefault();
        OnPropertyChanged(nameof(HasItemCatalog));
        FilterItemCards();
    }

    private static readonly (string Section, ItemGroup[] Groups)[] Sections =
    [
        ("weapons", [ItemGroup.Sword, ItemGroup.Dagger, ItemGroup.Axe, ItemGroup.Mace, ItemGroup.Polearm, ItemGroup.Thrown,
            ItemGroup.Bow, ItemGroup.Crossbow, ItemGroup.Ammo, ItemGroup.Shield]),
        ("armor", [ItemGroup.Head, ItemGroup.Body, ItemGroup.Legs, ItemGroup.Hands, ItemGroup.Cape]),
        ("mounts", [ItemGroup.Horse, ItemGroup.Harness]),
        ("goods", [ItemGroup.Food, ItemGroup.Goods, ItemGroup.Other]),
    ];

    private string GroupName(ItemGroup g) => L["itemgroup." + g];

    partial void OnSelectedItemGroupChanged(ItemGroupOption? value)
    {
        OnPropertyChanged(nameof(IsClothingGroup));
        FilterItemCards();
    }

    partial void OnItemQueryChanged(string value) => FilterItemCards();

    partial void OnClothingFilterChanged(int value)
    {
        OnPropertyChanged(nameof(ClothingAll));
        OnPropertyChanged(nameof(ClothingCivilian));
        OnPropertyChanged(nameof(ClothingBattle));
        FilterItemCards();
    }

    partial void OnItemSortChanged(string value)
    {
        OnPropertyChanged(nameof(SortPrice));
        OnPropertyChanged(nameof(SortPriceDesc));
        OnPropertyChanged(nameof(SortName));
        OnPropertyChanged(nameof(SortTier));
        FilterItemCards();
    }

    partial void OnItemTakeModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ItemGiveMode));
        if (value) _ = RefreshInventoryAsync();
    }

    partial void OnServerToolsAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(ItemsNeedTools));
        NotifyAdminCommands();
    }

    private const int MaxVisibleItems = 240;

    private void FilterItemCards()
    {
        var q = ItemQuery.Trim();
        IEnumerable<ItemCard> cards = _itemCards;

        if (SelectedItemGroup?.Group is { } group) cards = cards.Where(c => c.Item.Group == group);
        if (q.Length > 0)
            cards = cards
                .Select(c => (c, score: Score(c, q)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.c);

        if (IsClothingGroup && ClothingFilter != 0)
            cards = cards.Where(c => c.Item.Civilian == (ClothingFilter == 1));

        if (q.Length == 0)
            cards = ItemSort switch
            {
                "priceDesc" => cards.OrderByDescending(c => c.UnitValue),
                "name" => cards.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
                "tier" => cards.OrderByDescending(c => c.Item.Tier).ThenBy(c => c.UnitValue),
                _ => cards.OrderBy(c => c.UnitValue),
            };

        var list = cards.ToList();
        VisibleItems.Clear();
        foreach (var c in list.Take(MaxVisibleItems)) VisibleItems.Add(c);
        ItemResultText = list.Count > MaxVisibleItems
            ? string.Format(L["items.showingOf"], MaxVisibleItems, list.Count)
            : string.Format(L["items.count"], list.Count);

        static int Score(ItemCard c, string q)
        {
            if (c.Name.Equals(q, StringComparison.CurrentCultureIgnoreCase)) return 100;
            if (c.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 80;
            if (c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return 60;
            if (c.GroupName.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return 30;
            if (c.CultureName.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return 20;
            return 0;
        }
    }

    // --- ekranı açma --------------------------------------------------------

    /// <summary>Eşya ekranını açar; kahraman verilirse yalnızca o hedef olur.</summary>
    [RelayCommand]
    private void OpenItemBrowser(object? target)
    {
        var hero = target as HeroRow ?? (target as MapPlayer)?.Hero;
        if (hero is not null)
        {
            foreach (var h in Heroes) h.IsTarget = h == hero;
            SelectedHero = hero;
        }

        LoadCheatCatalogs();
        if (_itemCards.Count == 0) LoadItemCatalog();
        ItemBrowserOpen = true;
        if (ItemTakeMode) _ = RefreshInventoryAsync();
        if (ItemCatalogStale()) _ = RefreshServerItemsAsync();
    }

    [RelayCommand]
    private void CloseItemBrowser() => ItemBrowserOpen = false;

    [RelayCommand]
    private void SelectItemGroup(ItemGroupOption? option) => SelectedItemGroup = option;

    // --- sepet -------------------------------------------------------------

    [RelayCommand]
    private void AddItemCard(ItemCard? card)
    {
        if (card is null) return;
        var count = (int)Math.Clamp(ItemPerClick ?? 1, 1, 10_000);

        if (ItemBasket.FirstOrDefault(e => e.Card == card) is { } entry) entry.Count = (entry.Count ?? 0) + count;
        else
        {
            var added = new ItemBasketEntry(card, count);
            added.PropertyChanged += (_, _) => NotifyItemBasket();
            ItemBasket.Add(added);
        }

        NotifyItemBasket();
    }

    [RelayCommand]
    private void RemoveItemBasketEntry(ItemBasketEntry? entry)
    {
        if (entry is null) return;
        ItemBasket.Remove(entry);
        NotifyItemBasket();
    }

    [RelayCommand]
    private void ClearItemBasket()
    {
        ItemBasket.Clear();
        NotifyItemBasket();
    }

    private void NotifyItemBasket()
    {
        OnPropertyChanged(nameof(HasItemBasket));
        OnPropertyChanged(nameof(ItemBasketCount));
        OnPropertyChanged(nameof(ItemBasketValue));
        OnPropertyChanged(nameof(ItemBasketValueText));
    }

    /// <summary>Sepetteki eşyaları her hedefin envanterine koyar (eklenti gerekir).</summary>
    [RelayCommand(CanExecute = nameof(CanGiveItems))]
    private async Task GiveItemBasketAsync()
    {
        var basket = ItemBasket.Select(e => (e.Card, Count: (int)(e.Count ?? 0))).Where(e => e.Count > 0).ToList();
        if (basket.Count == 0) return;

        foreach (var hero in AdminTargets)
        {
            var given = new List<(string Id, int Count)>();
            foreach (var (card, count) in basket)
            {
                var (ok, _) = await RunAdminAsync(hero, AdminCommand.GiveItem(hero.HeroId, card.Id, count),
                    string.Format(L["audit.itemGive"], count, card.Name), refresh: false, quiet: true);
                if (ok) given.Add((card.Id, count));
            }

            if (given.Count == 0) continue;

            var text = string.Format(L["audit.itemsGiven"], given.Sum(g => g.Count));
            ShowToast($"{hero.Name}: {text}", ToastKind.Success);
            Remember(hero, text, async () =>
            {
                foreach (var (id, count) in given)
                    await RunAdminAsync(hero, AdminCommand.TakeItem(hero.HeroId, id, count), L["audit.undo"], refresh: false, quiet: true);
            });
        }

        ClearItemBasket();
    }

    private bool CanGiveItems() => CanAdminAct() && ServerToolsAvailable;

    /// <summary>Eklenti yoksa: sepetin değeri kadar altın (kasaba ortalaması).</summary>
    [RelayCommand(CanExecute = nameof(CanAdminAct))]
    private async Task GiveItemBasketAsGoldAsync()
    {
        var total = ItemBasketValue;
        if (total <= 0) return;

        var names = string.Join(", ", ItemBasket.Select(e => $"{(int)(e.Count ?? 0)}× {e.Card.Name}"));
        foreach (var hero in AdminTargets)
        {
            await RefreshHeroAsync(hero);
            if (hero.Gold is not { } current) continue;
            await SetHeroGoldAsync(hero, Math.Min(current + total, AdminCommand.MaxGold),
                string.Format(L["audit.itemComp"], total.ToString("N0"), names));
        }

        ClearItemBasket();
    }

    // --- envanterden alma -------------------------------------------------

    [RelayCommand]
    private async Task RefreshInventoryAsync()
    {
        InventoryLines.Clear();
        OnPropertyChanged(nameof(HasInventoryLines));
        if (CheatFocus is not { } hero || !IsServing || !ServerToolsAvailable) return;

        var lines = await _supervisor.SendAndCollectAsync(AdminCommand.PartyItems(hero.HeroId),
            l => l.StartsWith("COOPKEEP_ITEMS_END", StringComparison.Ordinal) || l.StartsWith("COOPKEEP_ERROR", StringComparison.Ordinal),
            TimeSpan.FromSeconds(4));

        foreach (var (id, count) in ServerItemCatalog.ParsePartyItems(lines).OrderBy(kv => kv.Key))
        {
            var card = _itemCardsById.GetValueOrDefault(id)
                       ?? new ItemCard(new ServerItem(id, id, "", "", 0, 0, "", false, false, false, "", 0), null, "", "");
            InventoryLines.Add(new InventoryLine(card, count));
        }

        OnPropertyChanged(nameof(HasInventoryLines));
    }

    [RelayCommand(CanExecute = nameof(CanGiveItems))]
    private async Task TakeItemsAsync()
    {
        if (CheatFocus is not { } hero) return;

        var taken = new List<(string Id, int Count)>();
        foreach (var line in InventoryLines.Where(l => (l.Remove ?? 0) > 0).ToList())
        {
            var count = (int)Math.Min(line.Remove ?? 0, line.Have);
            var (ok, lines) = await RunAdminAsync(hero, AdminCommand.TakeItem(hero.HeroId, line.Card.Id, count),
                string.Format(L["audit.itemTake"], count, line.Card.Name), refresh: false, quiet: true);
            if (ok && ServerItemCatalog.ParseItemChange(lines) is { } change) taken.Add((line.Card.Id, change.Old - change.New));
        }

        if (taken.Count > 0)
        {
            var text = string.Format(L["audit.itemsTaken"], taken.Sum(t => t.Count));
            ShowToast($"{hero.Name}: {text}", ToastKind.Success);
            Remember(hero, text, async () =>
            {
                foreach (var (id, count) in taken)
                    await RunAdminAsync(hero, AdminCommand.GiveItem(hero.HeroId, id, count), L["audit.undo"], refresh: false, quiet: true);
            });
        }

        await RefreshInventoryAsync();
    }

    [RelayCommand]
    private void EnableServerTools()
    {
        ServerToolsEnabled = true;
        ShowToast(IsRunning ? L["tools.restartNeeded"] : L["tools.enabled"], ToastKind.Info);
    }
}
