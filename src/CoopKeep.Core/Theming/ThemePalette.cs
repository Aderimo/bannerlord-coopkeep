namespace CoopKeep.Core.Theming;

/// <summary>Zemin teması: arka planlar, yazılar, çizgiler.</summary>
public enum ThemeBase
{
    /// <summary>Bannerlord'un toprak tonları, serif başlıklar (ilk görünüm).</summary>
    Classic,

    /// <summary>Gece mavisi: sakin, soğuk koyu gri.</summary>
    Night,

    /// <summary>Antrasit: nötr koyu gri.</summary>
    Graphite,

    /// <summary>Açık tema.</summary>
    Light,
}

/// <summary>Vurgu rengi: ana düğme, seçili öğe, odak.</summary>
public enum ThemeAccent { Gold, Blue, Purple, Red, Green, Orange, Yellow, Pink, Teal }

/// <summary>Bir rengin geçişli fırçası (üstten alta).</summary>
public sealed record GradientStops(uint[] Colors);

/// <summary>
/// Seçilen zemin ve vurgu renginden programın bütün renklerini üretir (ARGB, 0xAARRGGBB).
/// </summary>
/// <remarks>
/// <para>
/// Kullanıcılar ilk görünümü (kahve zemin + her yerde altın + altın çerçeveler) göz yorucu
/// bulduğunu söyledi (2026-10-23). Artık zemin ve vurgu ayrı seçilir; Klasik + Altın ilk
/// görünümün birebir aynısıdır.
/// </para>
/// <para>
/// Saf hesap: arayüz bu sözlükteki renkleri aynı adlı fırçalara yazar. Böylece her kombinasyon
/// birim testiyle denetlenir (ör. vurgu üstündeki yazı okunur mu).
/// </para>
/// </remarks>
public static class ThemePalette
{
    public static bool IsLight(ThemeBase b) => b == ThemeBase.Light;

    /// <summary>Başlıklarda serif yazı tipi yalnızca Klasik'te; diğerleri sade.</summary>
    public static bool SerifHeadings(ThemeBase b) => b == ThemeBase.Classic;

    /// <summary>Vurgu renginin kendisi: koyu zeminlerde parlak, açık zeminde okunur koyuluk.</summary>
    public static uint AccentColor(ThemeAccent a, bool light) => (a, light) switch
    {
        (ThemeAccent.Gold, false) => 0xFFD1A74A,
        (ThemeAccent.Gold, true) => 0xFFA8791C,
        (ThemeAccent.Blue, false) => 0xFF5B8DEF,
        (ThemeAccent.Blue, true) => 0xFF2F6FDB,
        (ThemeAccent.Purple, false) => 0xFF9B7BEA,
        (ThemeAccent.Purple, true) => 0xFF6E4BD8,
        (ThemeAccent.Red, false) => 0xFFE0645F,
        (ThemeAccent.Red, true) => 0xFFC93A3A,
        (ThemeAccent.Green, false) => 0xFF4FBF7A,
        (ThemeAccent.Green, true) => 0xFF1E8E4E,
        (ThemeAccent.Orange, false) => 0xFFF08A4B,
        (ThemeAccent.Orange, true) => 0xFFD0621E,
        (ThemeAccent.Yellow, false) => 0xFFE8C547,
        (ThemeAccent.Yellow, true) => 0xFFA88600,
        (ThemeAccent.Pink, false) => 0xFFE57BB0,
        (ThemeAccent.Pink, true) => 0xFFC2457F,
        (ThemeAccent.Teal, false) => 0xFF3FBFB0,
        _ => 0xFF0F7A70,
    };

    /// <summary>Zeminin temel renkleri.</summary>
    private sealed record BaseColors(
        uint Deep, uint Nav, uint Panel, uint Raised, uint Hover, uint Border, uint BorderHover,
        uint Text, uint TextDim, uint TextFaint, uint NavHover, uint NavSelected,
        uint BgDisabled, uint BorderDisabled, uint TextDisabled, uint Console,
        uint PanelTop, uint PanelBottom, bool Framed);

    private static BaseColors Base(ThemeBase b) => b switch
    {
        ThemeBase.Night => new(0xFF0F1115, 0xFF13161B, 0xFF171A21, 0xFF1E222A, 0xFF262B35, 0xFF2A2F39, 0xFF3A4150,
            0xFFE6E8EC, 0xFFA3ABB8, 0xFF6B7383, 0xFF1A1E25, 0xFF222731,
            0xFF15181E, 0xFF1F232B, 0xFF5A6170, 0xFF0B0D10, 0xFF171A21, 0xFF171A21, false),
        ThemeBase.Graphite => new(0xFF141517, 0xFF18191C, 0xFF1C1E21, 0xFF24262A, 0xFF2D3035, 0xFF2E3135, 0xFF42464C,
            0xFFECEAE6, 0xFFA8A49C, 0xFF75726B, 0xFF1F2124, 0xFF272A2E,
            0xFF191A1D, 0xFF222427, 0xFF5E5C57, 0xFF0F1012, 0xFF1C1E21, 0xFF1C1E21, false),
        ThemeBase.Light => new(0xFFF4F5F7, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFF1F3F6, 0xFFE7EAEF, 0xFFDADDE3, 0xFFBFC5CF,
            0xFF1C2230, 0xFF4F5868, 0xFF8A93A3, 0xFFEEF1F5, 0xFFE5EAF2,
            0xFFEEF0F3, 0xFFE1E4E9, 0xFFA3AAB6, 0xFF0F1115, 0xFFFFFFFF, 0xFFFFFFFF, false),
        _ => new(0xFF14100B, 0xFF1A140E, 0xFF211A12, 0xFF2C2318, 0xFF3A2F20, 0xFF3D3121, 0xFF5A4830,
            0xFFF1E7D0, 0xFFB6A685, 0xFF7E705A, 0xFF241C13, 0xFF2E2417,
            0xFF1D1710, 0xFF2B231A, 0xFF6B5F4C, 0xFF0E0B08, 0xFF261E14, 0xFF1E1811, true),
    };

    /// <summary>Düz renkler: anahtar → ARGB.</summary>
    public static IReadOnlyDictionary<string, uint> Colors(ThemeBase b, ThemeAccent a)
    {
        var c = Base(b);
        var light = IsLight(b);
        var accent = AccentColor(a, light);
        var classicGold = b == ThemeBase.Classic && a == ThemeAccent.Gold;

        var d = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["BgDeep"] = c.Deep,
            ["BgNav"] = c.Nav,
            ["BgPanel"] = c.Panel,
            ["BgRaised"] = c.Raised,
            ["BgHover"] = c.Hover,
            ["BorderDim"] = c.Border,
            ["BorderHover"] = c.BorderHover,
            ["TextMain"] = c.Text,
            ["TextDim"] = c.TextDim,
            ["TextFaint"] = c.TextFaint,
            ["NavHover"] = c.NavHover,
            ["NavSelected"] = classicGold ? c.NavSelected : Mix(accent, c.Panel, light ? 0.10 : 0.12),
            ["BgDisabled"] = c.BgDisabled,
            ["BorderDisabled"] = c.BorderDisabled,
            ["TextDisabled"] = c.TextDisabled,
            ["ConsoleBg"] = c.Console,

            ["Accent"] = accent,
            ["AccentHover"] = classicGold ? 0xFFE4BD62 : light ? Shade(accent, -0.12) : Shade(accent, 0.14),
            ["AccentDim"] = classicGold ? 0xFF8A6F1B : Mix(accent, c.Panel, 0.55),
            ["AccentSoft"] = classicGold ? 0xFF3A2D16 : Mix(accent, c.Panel, light ? 0.12 : 0.16),
            ["AccentSoftHover"] = classicGold ? 0xFF332B18 : Mix(accent, c.Panel, light ? 0.18 : 0.22),
            ["AccentBorder"] = classicGold ? 0xFFF3D58C : light ? Shade(accent, -0.15) : Shade(accent, 0.30),
            ["AccentBorderHover"] = classicGold ? 0xFFFFE3A0 : light ? Shade(accent, -0.25) : Shade(accent, 0.45),
            ["AccentDisabled"] = classicGold ? 0xFF3A3320 : Mix(accent, c.Panel, 0.22),
            ["OnAccent"] = classicGold ? 0xFF1A1608 : OnColor(accent),

            ["Success"] = light ? 0xFF15803D : 0xFF4ADE80,
            ["SuccessSoft"] = light ? 0xFFE7F5EC : 0xFF15291D,
            ["SuccessBorder"] = light ? 0xFFA7D7B5 : 0xFF2F5A3A,
            ["Warning"] = light ? 0xFFB45309 : 0xFFFBBF24,
            ["WarningSoft"] = light ? 0xFFFDF3E1 : 0xFF2E2614,
            ["WarningBorder"] = light ? 0xFFF0CF8F : 0xFF6A5420,
            ["Danger"] = light ? 0xFFC53030 : 0xFFE06262,
            ["DangerSoft"] = light ? 0xFFFBE9E9 : 0xFF331C1E,
            ["DangerBorder"] = light ? 0xFFEDB1B1 : 0xFF6A2A2A,
            ["DangerText"] = light ? 0xFF9B1C1C : 0xFFF5C2C2,
            ["DangerHover"] = light ? 0xFFB02525 : 0xFFEC7676,
        };
        return d;
    }

    /// <summary>Geçişli fırçalar: anahtar → renkler (üstten alta).</summary>
    public static IReadOnlyDictionary<string, uint[]> Gradients(ThemeBase b, ThemeAccent a)
    {
        var c = Base(b);
        var light = IsLight(b);
        var accent = AccentColor(a, light);
        var classicGold = b == ThemeBase.Classic && a == ThemeAccent.Gold;

        return new Dictionary<string, uint[]>(StringComparer.Ordinal)
        {
            // Klasik'te işlenmiş metal hissi; diğerlerinde düz çizgi.
            ["FrameBrush"] = c.Framed
                ? classicGold ? [0xFF7A6036, 0xFF3D3121, 0xFF2E2518] : [Mix(accent, c.Border, 0.40), c.Border, Mix(c.Border, c.Panel, 0.5)]
                : [c.Border, c.Border, c.Border],
            ["PanelGradient"] = [c.PanelTop, c.PanelBottom],
            ["HeroGradient"] = classicGold
                ? [0xFF3A2B16, 0xFF241B12, 0xFF1C150E]
                : c.Framed
                    ? [Mix(accent, c.Panel, 0.18), Mix(accent, c.Panel, 0.06), c.Deep]
                    : [Mix(accent, c.Panel, light ? 0.07 : 0.10), c.Panel, c.Panel],
            // Ana düğme: Klasik'te hafif geçişli altın, diğerlerinde düz.
            ["GoldButton"] = classicGold
                ? [0xFFE2BD63, 0xFFB88A2E]
                : c.Framed ? [Shade(accent, 0.10), Shade(accent, -0.12)] : [accent, accent],
            ["GoldButtonHover"] = classicGold
                ? [0xFFF0CD78, 0xFFC99A3A]
                : c.Framed ? [Shade(accent, 0.20), Shade(accent, -0.04)]
                    : light ? [Shade(accent, -0.10), Shade(accent, -0.10)] : [Shade(accent, 0.12), Shade(accent, 0.12)],
        };
    }

    /// <summary>Kart gölgesi: koyu temalarda belirgin, açıkta hafif.</summary>
    public static string CardShadow(ThemeBase b) => b switch
    {
        ThemeBase.Classic => "0 8 26 0 #66000000",
        ThemeBase.Light => "0 2 10 0 #14000000",
        _ => "0 4 14 0 #40000000",
    };

    // --- renk hesapları ---------------------------------------------------

    /// <summary><paramref name="top"/>'u <paramref name="amount"/> oranında <paramref name="bottom"/>'un üstüne karıştırır.</summary>
    public static uint Mix(uint top, uint bottom, double amount)
    {
        static byte Ch(uint c, int shift) => (byte)((c >> shift) & 0xFF);
        byte M(int shift) => (byte)Math.Round(Ch(top, shift) * amount + Ch(bottom, shift) * (1 - amount));
        return 0xFF000000u | ((uint)M(16) << 16) | ((uint)M(8) << 8) | M(0);
    }

    /// <summary>Açar (pozitif) ya da koyulaştırır (negatif).</summary>
    public static uint Shade(uint color, double amount) =>
        amount >= 0 ? Mix(0xFFFFFFFF, color, amount) : Mix(0xFF000000, color, -amount);

    /// <summary>WCAG göreli parlaklık.</summary>
    public static double Luminance(uint color)
    {
        static double Lin(uint c, int shift)
        {
            var v = ((c >> shift) & 0xFF) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(color, 16) + 0.7152 * Lin(color, 8) + 0.0722 * Lin(color, 0);
    }

    public static double Contrast(uint a, uint b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Vurgu renginin üstündeki yazı: hangisi daha okunursa (koyu ya da beyaz).</summary>
    public static uint OnColor(uint background) =>
        Contrast(background, 0xFF14110A) >= Contrast(background, 0xFFFFFFFF) ? 0xFF14110A : 0xFFFFFFFF;
}
