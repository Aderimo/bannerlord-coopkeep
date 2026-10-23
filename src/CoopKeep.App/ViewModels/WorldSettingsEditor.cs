using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.App.Localization;
using CoopKeep.Core.Configuration;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Bir dünyanın oyun ayarlarını düzenleyen form — oyundaki "Yeni kampanya" ekranının karşılığı.
/// </summary>
/// <remarks>
/// Hem yeni dünya sihirbazında hem de mevcut bir dünyanın ayarlar sayfasında aynı sınıf
/// kullanılır; böylece iki yer asla farklı davranmaz. Hangi ayarların gösterileceğini
/// verilen liste belirler (varsayılan: Coop'un ayar dosyası).
/// </remarks>
public partial class WorldSettingsEditor : ObservableObject
{
    private readonly SettingsCatalog _catalog;
    private readonly Dictionary<string, object> _values;
    private bool _suppressPreset;

    public WorldSettingsEditor() : this(new SettingsCatalog(ModSettingsCatalog.All))
    {
    }

    public WorldSettingsEditor(SettingsCatalog catalog)
    {
        _catalog = catalog;
        _values = catalog.Defaults();

        foreach (var d in catalog.All)
        {
            var item = new WorldSettingItem(d, this);
            Items.Add(item);

            var target = d.Group switch
            {
                ModSettingGroup.Difficulty => DifficultyItems,
                ModSettingGroup.LifeAndDeath => LifeItems,
                ModSettingGroup.World => WorldItems,
                ModSettingGroup.Multiplayer => MultiplayerItems,
                _ => CheatItems,
            };
            target.Add(item);
        }

        RefreshPreset();
    }

    public Loc L => Loc.Current;

    public ObservableCollection<WorldSettingItem> Items { get; } = [];
    public ObservableCollection<WorldSettingItem> DifficultyItems { get; } = [];
    public ObservableCollection<WorldSettingItem> LifeItems { get; } = [];
    public ObservableCollection<WorldSettingItem> WorldItems { get; } = [];
    public ObservableCollection<WorldSettingItem> MultiplayerItems { get; } = [];
    public ObservableCollection<WorldSettingItem> CheatItems { get; } = [];

    public bool HasLifeItems => LifeItems.Count > 0;
    public bool HasWorldItems => WorldItems.Count > 0;
    public bool HasMultiplayerItems => MultiplayerItems.Count > 0;
    public bool HasCheatItems => CheatItems.Count > 0;

    /// <summary>Değerler kullanıcı tarafından değiştirildi mi? (kaydedilmemiş değişiklik)</summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>Herhangi bir değer değişti — kaydetme düğmesini etkinleştirmek için.</summary>
    public event Action? Changed;

    public IReadOnlyDictionary<string, object> Values => _values;

    public void Load(IReadOnlyDictionary<string, object> values)
    {
        foreach (var d in _catalog.All)
            _values[d.Id] = values.TryGetValue(d.Id, out var v) ? _catalog.Normalize(d, v) : d.Default;

        foreach (var item in Items) item.Refresh();
        RefreshPreset();
        IsDirty = false;
    }

    internal object Get(string id) => _values[id];

    internal void Set(string id, object value)
    {
        var d = _catalog.Find(id)!;
        var normalized = _catalog.Normalize(d, value);
        if (Equals(_values[id], normalized)) return;

        _values[id] = normalized;
        IsDirty = true;

        if (d.Kind == ModSettingKind.Level && !_suppressPreset) RefreshPreset();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SummaryParts));
        Changed?.Invoke();
    }

    // --- ön ayarlar ------------------------------------------------------

    /// <summary>Tüm zorluk ayarları aynı seviyedeyse o seviye; değilse null (özel).</summary>
    [ObservableProperty] private string? _preset;

    public bool PresetVeryEasy { get => Preset == "VeryEasy"; set { if (value) ApplyPreset("VeryEasy"); } }
    public bool PresetEasy { get => Preset == "Easy"; set { if (value) ApplyPreset("Easy"); } }
    public bool PresetRealistic { get => Preset == "Realistic"; set { if (value) ApplyPreset("Realistic"); } }
    public bool PresetCustom => Preset is null;

    [RelayCommand]
    private void ApplyPreset(string level)
    {
        _suppressPreset = true;
        try
        {
            foreach (var item in Items.Where(i => i.Definition.Kind == ModSettingKind.Level))
                item.SetLevel(level);
        }
        finally
        {
            _suppressPreset = false;
        }

        RefreshPreset();
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        foreach (var d in _catalog.All) Set(d.Id, d.Default);
        foreach (var item in Items) item.Refresh();
        RefreshPreset();
    }

    private void RefreshPreset()
    {
        Preset = _catalog.DetectPreset(_values);
        OnPropertyChanged(nameof(PresetVeryEasy));
        OnPropertyChanged(nameof(PresetEasy));
        OnPropertyChanged(nameof(PresetRealistic));
        OnPropertyChanged(nameof(PresetCustom));
        OnPropertyChanged(nameof(PresetText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SummaryParts));
    }

    /// <summary>Kısa özet: "Kolay · başlangıç 1000 altın · savaşta ölüm kapalı".</summary>
    public string PresetText => L["preset." + (Preset ?? "Custom")];

    /// <summary>Ana sayfadaki rozetler için özet parçaları.</summary>
    public System.Collections.Generic.IReadOnlyList<string> SummaryParts => Summary.Split("  ·  ");

    public string Summary
    {
        get
        {
            var parts = new List<string> { PresetText };
            if (_values.TryGetValue("rules.startingGold", out var gold)) parts.Add(string.Format(L["sum.gold"], gold));
            if (_values.TryGetValue("modOptions.battleSize", out var size)) parts.Add(string.Format(L["sum.battleSize"], size));
            if (_values.TryGetValue("difficulty.battleDeath", out var death)) parts.Add(L[(string)death == "Realistic" ? "sum.deathOn" : "sum.deathOff"]);
            if (_values.TryGetValue("rules.protectResources", out var protect)) parts.Add(L[protect is true ? "sum.protectOn" : "sum.protectOff"]);
            if (_values.TryGetValue("modOptions.clientsCanUseCheats", out var cheats)) parts.Add(L[cheats is true ? "sum.cheatsOn" : "sum.cheatLock"]);
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Dil değiştiğinde metinleri tazeler.</summary>
    public void RefreshTexts()
    {
        foreach (var item in Items) item.RefreshTexts();
        OnPropertyChanged(nameof(PresetText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SummaryParts));
    }
}

/// <summary>Formdaki tek bir ayar satırı.</summary>
public partial class WorldSettingItem : ObservableObject
{
    private readonly WorldSettingsEditor _owner;

    public WorldSettingItem(ModSettingDefinition definition, WorldSettingsEditor owner)
    {
        Definition = definition;
        _owner = owner;

        if (definition.Choices is { } choices)
        {
            foreach (var c in choices) Options.Add(new SettingOption(this, c));
        }
    }

    public ModSettingDefinition Definition { get; }

    private static Loc L => Loc.Current;

    public string Title => L["set." + Definition.Key];
    public string Description => L["set." + Definition.Key + ".desc"];

    public bool IsLevel => Definition.Kind == ModSettingKind.Level;
    public bool IsBool => Definition.Kind == ModSettingKind.Bool;
    public bool IsChoice => Definition.Kind == ModSettingKind.Choice;
    public bool IsNumber => Definition.Kind is ModSettingKind.Int or ModSettingKind.Number;
    public bool HasOptions => IsLevel || IsChoice;

    /// <summary>Seviye ve seçim ayarları için düğmeler.</summary>
    public ObservableCollection<SettingOption> Options { get; } = [];

    /// <summary>
    /// Arayüzde tersine gösterilen ayar. <c>clientsCanUseCheats</c> "Hile koruması" olarak
    /// sunuluyor: koruma açık = oyuncular hile kullanamaz (dosyada <c>false</c>).
    /// </summary>
    public bool IsInverted => Definition.Key == "clientsCanUseCheats";

    public bool BoolValue
    {
        get => (_owner.Get(Definition.Id) is true) != IsInverted;
        set { _owner.Set(Definition.Id, value != IsInverted); OnPropertyChanged(); }
    }

    public string? ChoiceValue => _owner.Get(Definition.Id) as string;

    /// <summary>Seçili seçeneğin oyundaki açıklaması, örn. "-75% hasar".</summary>
    public string ChoiceEffect => ChoiceValue is { } v ? L["set." + Definition.Key + "." + v] : "";

    public double NumberValue
    {
        get => Convert.ToDouble(_owner.Get(Definition.Id), CultureInfo.InvariantCulture);
        set
        {
            _owner.Set(Definition.Id, Definition.Kind == ModSettingKind.Int ? (object)(int)Math.Round(value) : Math.Round(value, 1));
            OnPropertyChanged();
            OnPropertyChanged(nameof(NumberText));
        }
    }

    public double Minimum => Definition.Min;
    public double Maximum => Definition.Max;
    public double Step => Definition.Kind == ModSettingKind.Int ? (Definition.Max - Definition.Min > 100 ? 50 : 1) : 0.1;

    public string NumberText => Definition.Kind == ModSettingKind.Int
        ? NumberValue.ToString("0", CultureInfo.InvariantCulture)
        : "×" + NumberValue.ToString("0.0", CultureInfo.InvariantCulture);

    internal void SetLevel(string level)
    {
        _owner.Set(Definition.Id, level);
        Refresh();
    }

    internal void Select(string value)
    {
        _owner.Set(Definition.Id, value);
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(ChoiceValue));
        OnPropertyChanged(nameof(ChoiceEffect));
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(NumberText));
        foreach (var o in Options) o.Refresh();
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ChoiceEffect));
        foreach (var o in Options) o.Refresh();
    }
}

/// <summary>Bir seviye/seçim düğmesi.</summary>
public partial class SettingOption(WorldSettingItem owner, string value) : ObservableObject
{
    public string Value { get; } = value;

    public string Label => Loc.Current[(owner.IsLevel ? "level." : "opt.") + Value];

    public bool IsSelected
    {
        get => owner.ChoiceValue == Value;
        set { if (value) owner.Select(Value); }
    }

    /// <summary>Aynı satırdaki düğmeler birbirini dışlasın diye grup adı.</summary>
    public string Group => owner.Definition.Id;

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(Label));
    }
}
