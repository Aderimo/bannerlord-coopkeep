using CoopKeep.Core.Theming;
using Xunit;

namespace CoopKeep.Core.Tests.Theming;

/// <summary>Tema renkleri: ilk görünüm korunur, her kombinasyon okunur kalır.</summary>
public class ThemePaletteTests
{
    [Fact]
    public void ClassicGold_IsTheOriginalLook()
    {
        var c = ThemePalette.Colors(ThemeBase.Classic, ThemeAccent.Gold);

        Assert.Equal(0xFF14100Bu, c["BgDeep"]);
        Assert.Equal(0xFFD1A74Au, c["Accent"]);
        Assert.Equal(0xFF3A2D16u, c["AccentSoft"]);
        Assert.Equal(0xFF1A1608u, c["OnAccent"]);
        Assert.Equal([0xFFE2BD63u, 0xFFB88A2Eu], ThemePalette.Gradients(ThemeBase.Classic, ThemeAccent.Gold)["GoldButton"]);
    }

    public static IEnumerable<object[]> AllCombinations() =>
        from b in Enum.GetValues<ThemeBase>()
        from a in Enum.GetValues<ThemeAccent>()
        select new object[] { b, a };

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void EveryCombination_IsReadable(ThemeBase b, ThemeAccent a)
    {
        var c = ThemePalette.Colors(b, a);

        // Yazı zeminde rahat okunur (WCAG AA ~4.5).
        Assert.True(ThemePalette.Contrast(c["TextMain"], c["BgPanel"]) >= 7, "TextMain");
        Assert.True(ThemePalette.Contrast(c["TextDim"], c["BgPanel"]) >= 4.5, "TextDim");
        // Ana düğme yazısı vurgu renginin üstünde okunur.
        Assert.True(ThemePalette.Contrast(c["OnAccent"], c["Accent"]) >= 4.5, $"OnAccent {b}/{a}");
        // Vurgu rengi zeminden ayırt edilir (seçili öğe, odak çizgisi).
        Assert.True(ThemePalette.Contrast(c["Accent"], c["BgPanel"]) >= 3, $"Accent {b}/{a}");
        // Uyarı ve hata yazıları kendi kutularında okunur.
        Assert.True(ThemePalette.Contrast(c["DangerText"], c["DangerSoft"]) >= 4.5, "DangerText");
        Assert.True(ThemePalette.Contrast(c["TextMain"], c["WarningSoft"]) >= 7, "Warning box");
    }

    [Fact]
    public void Gradients_HaveAllKeys()
    {
        foreach (var b in Enum.GetValues<ThemeBase>())
        foreach (var key in new[] { "FrameBrush", "PanelGradient", "HeroGradient", "GoldButton", "GoldButtonHover" })
            Assert.True(ThemePalette.Gradients(b, ThemeAccent.Blue).ContainsKey(key), $"{b}/{key}");
    }

    [Fact]
    public void Mix_And_Shade()
    {
        Assert.Equal(0xFF808080u, ThemePalette.Mix(0xFFFFFFFF, 0xFF000000, 0.5020));
        Assert.Equal(0xFFFFFFFFu, ThemePalette.Shade(0xFF123456, 1));
        Assert.Equal(0xFF000000u, ThemePalette.Shade(0xFF123456, -1));
    }
}
