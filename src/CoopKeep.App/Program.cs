using Avalonia;
using System;

namespace CoopKeep.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Dil varsayılan olarak sistem yerelinden gelir; --lang tr|en bunu geçersiz kılar.
        // Arayüz içindeki TR/EN düğmesi her hâlükârda çalışmaya devam eder.
        var langIndex = Array.IndexOf(args, "--lang");
        if (langIndex >= 0 && langIndex + 1 < args.Length)
            Localization.Loc.Current.Language = args[langIndex + 1];

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
