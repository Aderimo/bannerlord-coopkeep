using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Net;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Program içi güncelleme: yeni exe indirilir, CoopKeep kapanınca eskisinin yerine konur ve
/// yeniden açılır. YouTube'dan gelen kullanıcı eski sürümde kalmasın.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Son sürümün exe'si (güncelleme denetiminde bulunduysa).</summary>
    private ReleaseInfo? _latestRelease;

    [ObservableProperty] private bool _updating;

    /// <summary>Kendini güncelleyebilir mi: exe adresi var ve program tek exe olarak çalışıyor.</summary>
    public bool CanSelfUpdate => _latestRelease?.ExeUrl is not null && CurrentExe is not null;

    /// <summary>
    /// Çalışan exe. Adı tarayıcının verdiği gibi olabilir ("CoopKeep (1).exe"); yalnızca .NET
    /// ana programıyla (geliştirme) çalışırken kendini güncelleyemez.
    /// </summary>
    private static string? CurrentExe =>
        Environment.ProcessPath is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ? p : null;

    partial void OnUpdatingChanged(bool value) => InstallUpdateCommand.NotifyCanExecuteChanged();

    private bool CanInstallUpdate() => !Updating;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (!CanSelfUpdate || _latestRelease?.ExeUrl is not { } url || CurrentExe is not { } exe)
        {
            OpenPath(ReleasesPage);
            return;
        }

        // Sunucu açıkken program kapanırsa sunucu da durur: önce kullanıcı durdursun.
        if (IsRunning)
        {
            ShowToast(L["update.stopServer"], ToastKind.Warning);
            return;
        }

        var target = Path.Combine(Path.GetDirectoryName(exe)!, "CoopKeep.new.exe");
        Updating = true;
        try
        {
            ShowToast(L["update.downloading"], ToastKind.Info);
            using var http = FastHttp.Create(TimeSpan.FromMinutes(5), "CoopKeep/" + AppVersion.TrimStart('v'));
            await using (var source = await http.GetStreamAsync(url))
            await using (var file = File.Create(target))
                await source.CopyToAsync(file);

            // Yarım ya da bozuk indirme exe'nin yerine konmasın.
            var info = new FileInfo(target);
            var header = new byte[2];
            await using (var check = File.OpenRead(target)) _ = await check.ReadAsync(header);
            if (header[0] != 'M' || header[1] != 'Z' || info.Length < 1_000_000 ||
                (_latestRelease.ExeSize > 0 && info.Length != _latestRelease.ExeSize))
            {
                File.Delete(target);
                ShowToast(L["update.badDownload"], ToastKind.Error);
                return;
            }

            var script = Path.Combine(Path.GetTempPath(), "coopkeep-update.cmd");
            await File.WriteAllTextAsync(script, ReleaseInfo.UpdaterScript(Environment.ProcessId, target, exe));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // Program Files gibi yazılamayan bir klasördeyse: sürüm sayfası açılır.
            ShowToast(string.Format(L["update.failed"], ex.Message), ToastKind.Error);
            OpenPath(ReleasesPage);
        }
        finally
        {
            Updating = false;
        }
    }
}
