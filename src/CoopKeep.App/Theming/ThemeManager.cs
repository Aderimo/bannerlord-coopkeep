using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CoopKeep.Core.Theming;

namespace CoopKeep.App.Theming;

/// <summary>
/// Seçilen zemin ve vurgu rengini uygular.
/// </summary>
/// <remarks>
/// <para>
/// Görünümler renklere <c>StaticResource</c> ile bağlı. Fırçaları değiştirmek yerine aynı
/// fırçaların <c>Color</c>'ı yazılır: bütün bağlı yerler anında güncellenir, görünümlere
/// dokunmak gerekmez. Yazı tipi ve gölge değer türü olduğu için <c>DynamicResource</c> ile
/// bağlı; onların kaynağı yenisiyle değiştirilir.
/// </para>
/// <para>Renkler <see cref="ThemePalette"/>'te hesaplanır (birim testli).</para>
/// </remarks>
public static class ThemeManager
{
    private const string SerifHeadings = "Palatino Linotype, Book Antiqua, Georgia, serif";
    private const string SansHeadings = "Segoe UI Variable Display, Segoe UI, Inter, sans-serif";

    public static ThemeBase CurrentBase { get; private set; } = ThemeBase.Classic;
    public static ThemeAccent CurrentAccent { get; private set; } = ThemeAccent.Gold;

    /// <summary>Tema değişince (kodda üretilen fırçalar kendini yenilesin diye).</summary>
    public static event Action? Changed;

    /// <summary>Ayarlarda "Sistemle aynı": Windows açıksa Açık, koyuysa Gece zemini.</summary>
    public const string SystemKey = "System";

    public static bool FollowSystem { get; private set; }

    /// <summary>Kaydedilen zemin: "System" ya da zeminin adı.</summary>
    public static string CurrentBaseKey => FollowSystem ? SystemKey : CurrentBase.ToString();

    /// <summary>Windows'un uygulama renk modu (açık/koyu) için zemin.</summary>
    public static ThemeBase SystemBase() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light
            ? ThemeBase.Light
            : ThemeBase.Night;

    /// <summary>Kayıttaki zemini uygular; "System" ise Windows'u izler.</summary>
    public static void Apply(string? baseKey, ThemeAccent a)
    {
        FollowSystem = string.Equals(baseKey, SystemKey, StringComparison.OrdinalIgnoreCase);
        WatchSystem();
        Apply(FollowSystem ? SystemBase() : ParseBase(baseKey), a);
    }

    private static bool _watching;

    private static void WatchSystem()
    {
        if (_watching || Application.Current?.PlatformSettings is not { } settings) return;
        _watching = true;
        settings.ColorValuesChanged += (_, _) =>
        {
            if (FollowSystem) Dispatcher.UIThread.Post(() => Apply(SystemBase(), CurrentAccent));
        };
    }

    public static void Apply(ThemeBase b, ThemeAccent a)
    {
        if (Application.Current is not { } app) return;
        CurrentBase = b;
        CurrentAccent = a;

        foreach (var (key, argb) in ThemePalette.Colors(b, a))
            if (app.Resources.TryGetResource(key, null, out var res) && res is SolidColorBrush brush)
                brush.Color = Color.FromUInt32(argb);

        foreach (var (key, stops) in ThemePalette.Gradients(b, a))
        {
            if (!app.Resources.TryGetResource(key, null, out var res) || res is not GradientBrush gradient) continue;
            for (var i = 0; i < gradient.GradientStops.Count; i++)
                gradient.GradientStops[i].Color = Color.FromUInt32(stops[Math.Min(i, stops.Length - 1)]);
        }

        app.Resources["HeadingFont"] = new FontFamily(ThemePalette.SerifHeadings(b) ? SerifHeadings : SansHeadings);
        app.Resources["CardShadow"] = BoxShadows.Parse(ThemePalette.CardShadow(b));

        // Fluent kontrolleri (onay kutusu, kaydırıcı, açılır liste) de aynı vurgu ve açık/koyu zemin.
        var light = ThemePalette.IsLight(b);
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            var colors = ThemePalette.Colors(b, a);
            var variant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            if (fluent.Palettes.TryGetValue(variant, out var palette))
            {
                palette.Accent = Color.FromUInt32(colors["Accent"]);
                palette.AltHigh = Color.FromUInt32(colors["BgDeep"]);
                palette.RegionColor = Color.FromUInt32(colors["BgDeep"]);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Tema fırçası (aynı nesne: tema değişince kendiliğinden güncellenir).</summary>
    public static IBrush Brush(string key) =>
        Application.Current?.Resources.TryGetResource(key, null, out var res) == true && res is IBrush brush
            ? brush
            : Brushes.Transparent;

    public static ThemeBase ParseBase(string? value) =>
        Enum.TryParse<ThemeBase>(value, ignoreCase: true, out var b) ? b : ThemeBase.Classic;

    public static ThemeAccent ParseAccent(string? value) =>
        Enum.TryParse<ThemeAccent>(value, ignoreCase: true, out var a) ? a : ThemeAccent.Gold;
}
