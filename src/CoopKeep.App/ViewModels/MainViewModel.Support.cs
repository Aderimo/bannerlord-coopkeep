using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Installation;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Destek: hata raporu paketi ve güncelleme denetimi.
/// </summary>
public partial class MainViewModel
{
    private const string ReleasesApi = "https://api.github.com/repos/Aderimo/bannerlord-coopkeep/releases/latest";
    private const string ReleasesPage = "https://github.com/Aderimo/bannerlord-coopkeep/releases/latest";

    [ObservableProperty] private bool _checkUpdates = true;

    /// <summary>Sunucu süreçlerine "normalin üstü" öncelik.</summary>
    [ObservableProperty] private bool _highPriority;

    /// <summary>Bellek uyarı sınırı (GB). 0 = kapalı.</summary>
    [ObservableProperty] private int _memoryWarningGb = 3;

    /// <summary>Sunucu belleği sınırı aştıysa uyarı metni; yoksa boş.</summary>
    [ObservableProperty] private string _memoryWarning = "";

    public bool HasMemoryWarning => MemoryWarning.Length > 0;

    public IReadOnlyList<int> MemoryWarningOptions { get; } = [0, 2, 3, 4, 6, 8];

    partial void OnMemoryWarningChanged(string value) => OnPropertyChanged(nameof(HasMemoryWarning));

    partial void OnHighPriorityChanged(bool value)
    {
        _ui.HighPriority = value;
        _ui.Save();
    }

    partial void OnMemoryWarningGbChanged(int value)
    {
        _ui.MemoryWarningGb = value;
        _ui.Save();
    }

    /// <summary>Yeni sürüm varsa onun etiketi (örn. <c>v0.2.1</c>); yoksa boş.</summary>
    [ObservableProperty] private string _updateVersion = "";

    public bool HasUpdate => UpdateVersion.Length > 0;

    public string UpdateText => string.Format(L["update.available"], UpdateVersion);

    partial void OnUpdateVersionChanged(string value)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateText));
    }

    partial void OnCheckUpdatesChanged(bool value)
    {
        _ui.CheckUpdates = value;
        _ui.Save();
    }

    /// <summary>
    /// GitHub'daki son sürümü sorar. Yalnızca sürüm etiketi okunur; hiçbir kullanıcı
    /// bilgisi gönderilmez. Ayarlardan kapatılabilir.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            using var http = CoopKeep.Core.Net.FastHttp.Create(TimeSpan.FromSeconds(8));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoopKeep/" + AppVersion.TrimStart('v'));

            var release = CoopKeep.Core.Net.ReleaseInfo.Parse(await http.GetStringAsync(ReleasesApi));

            if (release is not null && IsNewer(release.Tag, AppVersion))
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _latestRelease = release;
                    UpdateVersion = release.Tag;
                    OnPropertyChanged(nameof(CanSelfUpdate));
                });
        }
        catch (Exception)
        {
            // Çevrimdışı, oran sınırı vb. — güncelleme denetimi kritik değil.
        }
    }

    internal static bool IsNewer(string candidate, string current)
    {
        static Version? Parse(string s) =>
            Version.TryParse(Regex.Match(s, @"\d+(\.\d+){1,3}").Value, out var v) ? v : null;

        return Parse(candidate) is { } c && Parse(current) is { } cur && c > cur;
    }

    [RelayCommand]
    private void OpenReleasePage() => OpenPath(ReleasesPage);

    // --- hata raporu ------------------------------------------------------

    /// <summary>
    /// Destek için tek bir zip hazırlar: son sunucu logları, yapılandırma (şifre gizlenmiş),
    /// oyun ayarları, mod listesi ve sürüm bilgisi.
    /// </summary>
    /// <remarks>
    /// "Bende çalışmıyor" bildirimlerinin ilk soruları hep aynı: hangi sürüm, hangi modlar,
    /// log nerede. Kullanıcıdan bunları tek tek istemek yerine tek tıkla toplanır.
    /// Şifre ve SteamID'ler paketlenmeden önce gizlenir.
    /// </remarks>
    [RelayCommand]
    private async Task CreateBugReportAsync()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zipPath = Path.Combine(desktop, $"CoopKeep-rapor-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            // Ekrandan okunanlar UI thread'inde alınır; dosya işi (logların her biri
            // megabaytlarca) arka planda yapılır ki arayüz donmasın.
            var info = BuildInfoText();
            var console = string.Join(Environment.NewLine, Console);
            var dataDirectory = DataDirectory;
            var worldProfiles = _profiles.Directory;
            var activeWorld = ActiveSaveName;

            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                AddText(zip, "info.txt", info);

                var config = Path.Combine(dataDirectory, "server-config.json");
                if (File.Exists(config))
                    AddText(zip, "server-config.json", MaskSecrets(File.ReadAllText(config)));

                if (File.Exists(CoopInstallation.DefaultModConfigPath))
                    AddText(zip, "mod-config.json", File.ReadAllText(CoopInstallation.DefaultModConfigPath));

                // "Ayarlar sıfırlanıyor" bildirimlerinde dünyanın kendi profili de gerekli.
                if (!string.IsNullOrEmpty(activeWorld) && Directory.Exists(worldProfiles))
                {
                    foreach (var profile in Directory.GetFiles(worldProfiles, activeWorld + ".*.json"))
                        AddText(zip, "world/" + Path.GetFileName(profile), File.ReadAllText(profile));
                }

                var logs = Path.Combine(dataDirectory, "logs");
                if (Directory.Exists(logs))
                {
                    foreach (var log in new DirectoryInfo(logs).GetFiles("*.log")
                                 .OrderByDescending(f => f.LastWriteTimeUtc).Take(3))
                    {
                        using var reader = new StreamReader(new FileStream(log.FullName, FileMode.Open,
                            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                        AddText(zip, "logs/" + log.Name, MaskSecrets(reader.ReadToEnd()));
                    }
                }

                AddText(zip, "console.txt", MaskSecrets(console));

                if (File.Exists(App.CrashLogPath))
                    AddText(zip, "crash.log", MaskSecrets(File.ReadAllText(App.CrashLogPath)));
            });

            ShowToast(L["msg.reportCreated"]);
            OpenPath(desktop);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    private string BuildInfoText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CoopKeep {AppVersion}");
        sb.AppendLine($"Bannerlord {GameVersion}");
        sb.AppendLine($"Coop {ModVersion}");
        sb.AppendLine($"OS {Environment.OSVersion}");
        sb.AppendLine($"Active world: {ActiveSaveName}");
        sb.AppendLine();
        sb.AppendLine("Launcher modules:");
        // Ekrandaki sayfa değil, tüm liste (görünüm en fazla 50 satır gösteriyor).
        foreach (var m in _allModules)
            sb.AppendLine($"  [{(m.IsSelected ? "x" : " ")}] {m.Id} {m.Version} ({m.Status})");
        return sb.ToString();
    }

    /// <summary>Şifreyi ve SteamID64'leri maskeler.</summary>
    internal static string MaskSecrets(string text)
    {
        text = Regex.Replace(text, "(\"password\"\\s*:\\s*\")[^\"]*(\")", "$1***$2");
        text = Regex.Replace(text, @"\b7656119\d{10}\b", "STEAMID");
        return text;
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private DateTime _lastUnexpectedErrorAt;

    /// <summary>
    /// Beklenmeyen hata: program kapanmaz, kısa bir uyarı çıkar. Art arda gelen hatalar
    /// ekranı doldurmasın diye 10 saniyede bir gösterilir.
    /// </summary>
    public void ShowUnexpectedError(Exception ex)
    {
        if (DateTime.UtcNow - _lastUnexpectedErrorAt < TimeSpan.FromSeconds(10)) return;
        _lastUnexpectedErrorAt = DateTime.UtcNow;
        ShowToast(string.Format(L["msg.unexpectedError"], ex.Message), ToastKind.Error);
    }

    [RelayCommand]
    private void OpenLogsFolder() => OpenPath(Path.Combine(DataDirectory, "logs"));
}
