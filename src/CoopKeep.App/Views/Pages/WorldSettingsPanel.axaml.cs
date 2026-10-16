using Avalonia;
using Avalonia.Controls;

namespace CoopKeep.App.Views.Pages;

public partial class WorldSettingsPanel : UserControl
{
    public static readonly StyledProperty<bool> ShowDifficultyProperty =
        AvaloniaProperty.Register<WorldSettingsPanel, bool>(nameof(ShowDifficulty), true);

    public static readonly StyledProperty<bool> ShowRulesProperty =
        AvaloniaProperty.Register<WorldSettingsPanel, bool>(nameof(ShowRules), true);

    public static readonly StyledProperty<bool> ExpandDetailsProperty =
        AvaloniaProperty.Register<WorldSettingsPanel, bool>(nameof(ExpandDetails));

    /// <summary>
    /// Ön ayar düğmelerinin grup adı. Aynı anda iki form ekranda olabildiği için
    /// (sihirbaz + Dünyalar sayfası) her biri kendi grubunu kullanmalı.
    /// </summary>
    public static readonly StyledProperty<string> PresetGroupProperty =
        AvaloniaProperty.Register<WorldSettingsPanel, string>(nameof(PresetGroup), "preset");

    public bool ShowDifficulty
    {
        get => GetValue(ShowDifficultyProperty);
        set => SetValue(ShowDifficultyProperty, value);
    }

    public bool ShowRules
    {
        get => GetValue(ShowRulesProperty);
        set => SetValue(ShowRulesProperty, value);
    }

    public bool ExpandDetails
    {
        get => GetValue(ExpandDetailsProperty);
        set => SetValue(ExpandDetailsProperty, value);
    }

    public string PresetGroup
    {
        get => GetValue(PresetGroupProperty);
        set => SetValue(PresetGroupProperty, value);
    }

    public WorldSettingsPanel() => InitializeComponent();
}
