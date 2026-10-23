using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using CoopKeep.App.Theming;
using CoopKeep.Core.Theming;

namespace CoopKeep.App.Tests;

/// <summary>Tema: programın kaynaklarındaki fırçalar yerinde boyanır, açık/koyu mod ve "Sistemle aynı".</summary>
public class ThemeManagerTests
{
    private static Color BrushColor(string key) =>
        ((SolidColorBrush)Application.Current!.Resources[key]!).Color;

    [AvaloniaFact]
    public void Apply_paints_the_shared_brushes()
    {
        var accent = Application.Current!.Resources["Accent"];

        ThemeManager.Apply(ThemeBase.Night, ThemeAccent.Blue);
        var colors = ThemePalette.Colors(ThemeBase.Night, ThemeAccent.Blue);

        Assert.Same(accent, Application.Current.Resources["Accent"]); // aynı nesne: bağlı yerler güncellenir
        Assert.Equal(Color.FromUInt32(colors["Accent"]), BrushColor("Accent"));
        Assert.Equal(Color.FromUInt32(colors["BgDeep"]), BrushColor("BgDeep"));
        Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);

        ThemeManager.Apply(ThemeBase.Classic, ThemeAccent.Gold);
    }

    [AvaloniaFact]
    public void Light_base_switches_fluent_controls_to_light()
    {
        ThemeManager.Apply(ThemeBase.Light, ThemeAccent.Teal);
        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        ThemeManager.Apply(ThemeBase.Classic, ThemeAccent.Gold);
        Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void System_key_follows_the_platform_and_is_remembered()
    {
        ThemeManager.Apply(ThemeManager.SystemKey, ThemeAccent.Purple);

        Assert.True(ThemeManager.FollowSystem);
        Assert.Equal(ThemeManager.SystemKey, ThemeManager.CurrentBaseKey);
        Assert.Equal(ThemeManager.SystemBase(), ThemeManager.CurrentBase);
        Assert.Contains(ThemeManager.CurrentBase, new[] { ThemeBase.Light, ThemeBase.Night });

        ThemeManager.Apply("Graphite", ThemeAccent.Gold);
        Assert.False(ThemeManager.FollowSystem);
        Assert.Equal("Graphite", ThemeManager.CurrentBaseKey);

        ThemeManager.Apply(ThemeBase.Classic, ThemeAccent.Gold);
    }

    [Fact]
    public void Unknown_saved_values_fall_back_to_the_original_look()
    {
        Assert.Equal(ThemeBase.Classic, ThemeManager.ParseBase(null));
        Assert.Equal(ThemeBase.Classic, ThemeManager.ParseBase("nope"));
        Assert.Equal(ThemeAccent.Gold, ThemeManager.ParseAccent(""));
        Assert.Equal(ThemeAccent.Teal, ThemeManager.ParseAccent("teal"));
    }
}
