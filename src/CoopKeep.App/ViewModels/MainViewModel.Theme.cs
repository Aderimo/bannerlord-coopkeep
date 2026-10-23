using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CoopKeep.App.Theming;
using CoopKeep.Core.Theming;

namespace CoopKeep.App.ViewModels;

/// <summary>Ayarlar'daki bir tema ya da vurgu seçeneği.</summary>
public sealed partial class ThemeOption(string key, string name, Action<ThemeOption> selected) : ObservableObject
{
    public string Key { get; } = key;
    public string Name { get; } = name;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Önizleme renkleri: zeminde arka plan / kart / yazı; vurguda tek renk.</summary>
    [ObservableProperty] private IBrush _preview = Brushes.Transparent;
    [ObservableProperty] private IBrush _preview2 = Brushes.Transparent;
    [ObservableProperty] private IBrush _preview3 = Brushes.Transparent;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) selected(this);
    }
}

/// <summary>
/// Görünüm: zemin teması ve vurgu rengi (kullanıcılar ilk görünümü göz yorucu buldu).
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<ThemeOption> ThemeBases { get; } = [];
    public ObservableCollection<ThemeOption> ThemeAccents { get; } = [];

    private bool _buildingThemeOptions;

    private void BuildThemeOptions()
    {
        _buildingThemeOptions = true;
        ThemeBases.Clear();
        ThemeAccents.Clear();
        var current = ThemeManager.CurrentBaseKey;
        ThemeBases.Add(new ThemeOption(ThemeManager.SystemKey, L["theme.base.System"], OnThemeOptionSelected) { IsSelected = current == ThemeManager.SystemKey });
        foreach (var b in Enum.GetValues<ThemeBase>())
            ThemeBases.Add(new ThemeOption(b.ToString(), L["theme.base." + b], OnThemeOptionSelected) { IsSelected = current == b.ToString() });
        foreach (var a in Enum.GetValues<ThemeAccent>())
            ThemeAccents.Add(new ThemeOption(a.ToString(), L["theme.accent." + a], OnThemeOptionSelected) { IsSelected = a == ThemeManager.CurrentAccent });
        RefreshThemePreviews();
        _buildingThemeOptions = false;
    }

    private void OnThemeOptionSelected(ThemeOption option)
    {
        if (_buildingThemeOptions) return;
        var isBase = ThemeBases.Contains(option);
        foreach (var other in (isBase ? ThemeBases : ThemeAccents).Where(o => o != option)) other.IsSelected = false;

        var b = isBase ? option.Key : ThemeManager.CurrentBaseKey;
        var a = isBase ? ThemeManager.CurrentAccent : ThemeManager.ParseAccent(option.Key);
        ThemeManager.Apply(b, a);
        _ui.ThemeBase = b;
        _ui.ThemeAccent = a.ToString();
        _ui.Save();
        RefreshThemePreviews();
    }

    /// <summary>Önizlemeler seçili vurguyla (zemin) ve seçili zeminin tonuyla (vurgu) çizilir.</summary>
    private void RefreshThemePreviews()
    {
        static IBrush B(uint argb) => new SolidColorBrush(Color.FromUInt32(argb));
        foreach (var o in ThemeBases)
        {
            var b = o.Key == ThemeManager.SystemKey ? ThemeManager.SystemBase() : ThemeManager.ParseBase(o.Key);
            var c = ThemePalette.Colors(b, ThemeManager.CurrentAccent);
            o.Preview = B(c["BgDeep"]);
            o.Preview2 = B(c["BgRaised"]);
            o.Preview3 = B(c["Accent"]);
        }

        var light = ThemePalette.IsLight(ThemeManager.CurrentBase);
        foreach (var o in ThemeAccents) o.Preview = B(ThemePalette.AccentColor(ThemeManager.ParseAccent(o.Key), light));
    }
}
