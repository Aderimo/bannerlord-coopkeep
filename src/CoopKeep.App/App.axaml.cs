using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CoopKeep.App.ViewModels;
using CoopKeep.App.Views;

namespace CoopKeep.App;

public partial class App : Application
{
    /// <summary>Beklenmeyen hataların yazıldığı dosya (hata raporu zip'ine de girer).</summary>
    public static string CrashLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "crash.log");

    /// <summary>Tepsi simgesi; uygulama boyunca yaşar.</summary>
    private TrayController? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var ui = Settings.UiSettings.Load();
            Theming.ThemeManager.Apply(ui.ThemeBase, Theming.ThemeManager.ParseAccent(ui.ThemeAccent));
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            HandleUnexpectedErrors(vm);
            _tray = new TrayController(this, window, vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Arayüzde beklenmeyen bir hata programı kapatmasın: program kapanırsa açık sunucu sahipsiz
    /// kalıyor (durdurmak için Görev Yöneticisi gerekiyordu). Hata dosyaya yazılır, kullanıcıya
    /// kısa bir uyarı gösterilir ve program çalışmaya devam eder.
    /// </summary>
    private static void HandleUnexpectedErrors(MainViewModel vm)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.Handled = true;
            try { vm.ShowUnexpectedError(e.Exception); } catch { /* uyarı gösterilemese de program sürsün */ }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
        };
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            // Dosya büyümesin: 512 KB'ı geçince baştan başlanır.
            if (File.Exists(CrashLogPath) && new FileInfo(CrashLogPath).Length > 512 * 1024) File.Delete(CrashLogPath);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
