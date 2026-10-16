using System.Globalization;
using Avalonia.Data.Converters;

namespace CoopKeep.App.ViewModels;

/// <summary>XAML'de kullanılan küçük biçimlendiriciler.</summary>
public static class Formatters
{
    /// <summary>Bayt → "3,4 MB".</summary>
    public static readonly IValueConverter FileSize = new FuncValueConverter<long, string>(bytes =>
        bytes switch
        {
            >= 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " GB",
            >= 1024L * 1024 => (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB",
            >= 1024 => (bytes / 1024.0).ToString("0", CultureInfo.CurrentCulture) + " KB",
            _ => bytes + " B",
        });

    /// <summary>Sihirbaz adım çubuğu: geçerli adım parametreye ulaştıysa dolu (1), değilse boş (0).</summary>
    public static readonly IValueConverter StepFill = new StepFillConverter();

    /// <summary>Geçerli adım parametreye ulaştı mı? (sınıf bağlamaları için bool)</summary>
    public static readonly IValueConverter StepReached = new FuncValueConverter<int, object?, bool>(
        (step, parameter) => int.TryParse(parameter?.ToString(), out var index) && step >= index);

    private sealed class StepFillConverter : IValueConverter
    {
        public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture) =>
            value is int step && int.TryParse(parameter?.ToString(), out var index) && step >= index ? 1.0 : 0.0;

        public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture) =>
            throw new System.NotSupportedException();
    }
}
