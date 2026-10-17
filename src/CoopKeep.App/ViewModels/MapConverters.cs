using Avalonia.Data.Converters;

namespace CoopKeep.App.ViewModels;

/// <summary>Harita sayfasının küçük dönüştürücüleri.</summary>
public static class MapConverters
{
    /// <summary>Öncelikli oyuncular diğerlerinin üstünde çizilir.</summary>
    public static readonly IValueConverter PriorityZ =
        new FuncValueConverter<bool, int>(p => p ? 20 : 10);

    /// <summary>Çevrimdışı oyuncu soluk görünür.</summary>
    public static readonly IValueConverter OnlineOpacity =
        new FuncValueConverter<bool, double>(online => online ? 1.0 : 0.55);

    private static readonly Avalonia.Media.IBrush AccentFill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D1A74A"));

    /// <summary>Dolu odak noktası altın, boşu şeffaf.</summary>
    public static readonly IValueConverter BoolToAccent =
        new FuncValueConverter<bool, Avalonia.Media.IBrush>(on => on ? AccentFill : Avalonia.Media.Brushes.Transparent);
}

/// <summary>Asker kimliğinden ada (portre aramak için). Uygulama açılınca doldurulur.</summary>
public static class TroopNames
{
    public static System.Func<string, string>? Resolve { get; set; }
}
