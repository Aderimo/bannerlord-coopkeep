using Avalonia;
using Avalonia.Headless;
using CoopKeep.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace CoopKeep.App.Tests;

/// <summary>
/// Ekransız Avalonia: programın kendi <c>App</c>'i (kaynaklar, stiller) yüklenir, pencere açılmaz.
/// Masaüstü yaşam döngüsü olmadığı için ana pencere ve sunucu yöneticisi kurulmaz.
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>Programın kaynak klasörleri (XAML taraması için).</summary>
internal static class Repo
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CoopKeep.sln"))) return dir.FullName;
        throw new InvalidOperationException("CoopKeep.sln bulunamadı");
    }

    public static string AppSource => Path.Combine(Root, "src", "CoopKeep.App");
}
