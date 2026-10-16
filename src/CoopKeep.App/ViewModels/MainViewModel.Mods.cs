using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Installation;

namespace CoopKeep.App.ViewModels;

/// <summary>Modlar sayfasındaki tek satır.</summary>
public sealed record ModuleRow(string Id, string? Version, bool IsSelected, ModuleStatus Status, string StatusText)
{
    public bool IsOk => Status == ModuleStatus.Supported;
    public bool IsBlocked => Status == ModuleStatus.Blocked;
    public bool IsRisky => Status == ModuleStatus.NotOnServer;

    /// <summary>Topluluk listesindeki durum.</summary>
    public CommunityStatus Community { get; init; }
    public string? CommunityNote { get; init; }
    public string CommunityText { get; init; } = "";
    public bool CommunityWorks => Community is CommunityStatus.Works or CommunityStatus.ClientOnly;
    public bool CommunityBroken => Community == CommunityStatus.Broken;

    /// <summary>Bu bilgisayardaki son Coop oturumunda bu mod hata satırında geçti.</summary>
    public bool HadLocalError { get; init; }

    /// <summary>Arama eşleşmesi yokken satır sönük gösterilir.</summary>
    public bool Dimmed { get; init; }

    public double RowOpacity => Dimmed ? 0.35 : IsSelected ? 1.0 : 0.6;

    /// <summary>Resmî modüller için topluluk bildirimi anlamsız.</summary>
    public bool CanReport => !IsOk;
}

public enum ModsFilter { All, Compatible, NotOnServer, Blocked, CommunityWorks, CommunityBroken }

/// <summary>
/// Mod uyumluluğu sayfası, topluluk listesi ve "Coop profili".
/// </summary>
/// <remarks>
/// Arkadaşların bağlanamamasının en sık sebebi fazladan açık modlar ve oyunun hata metni
/// bunu açıkça söylemiyor. Topluluk listesi GitHub'daki <c>compat/mods.json</c>'dan gelir;
/// kullanıcı bir modu bildirmek isterse hazır doldurulmuş bir GitHub issue açılır, kendisi
/// görüp gönderir — otomatik hiçbir veri gönderilmez.
/// </remarks>
public partial class MainViewModel
{
    private const string IssueUrl = "https://github.com/Aderimo/bannerlord-coopkeep/issues/new";

    private readonly List<ModuleRow> _allModules = [];
    private ModCompatDatabase _community = ModCompatDatabase.Empty;
    private HashSet<string> _localModuleErrors = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ModuleRow> Modules { get; } = [];

    [ObservableProperty] private bool _hasProfileBackup;
    [ObservableProperty] private string _modSearch = "";
    [ObservableProperty] private ModsFilter _modsFilter = ModsFilter.All;

    public bool FilterAll { get => ModsFilter == ModsFilter.All; set { if (value) ModsFilter = ModsFilter.All; } }
    public bool FilterCompatible { get => ModsFilter == ModsFilter.Compatible; set { if (value) ModsFilter = ModsFilter.Compatible; } }
    public bool FilterNotOnServer { get => ModsFilter == ModsFilter.NotOnServer; set { if (value) ModsFilter = ModsFilter.NotOnServer; } }
    public bool FilterBlocked { get => ModsFilter == ModsFilter.Blocked; set { if (value) ModsFilter = ModsFilter.Blocked; } }
    public bool FilterCommunityWorks { get => ModsFilter == ModsFilter.CommunityWorks; set { if (value) ModsFilter = ModsFilter.CommunityWorks; } }
    public bool FilterCommunityBroken { get => ModsFilter == ModsFilter.CommunityBroken; set { if (value) ModsFilter = ModsFilter.CommunityBroken; } }

    public bool HasModules => _allModules.Count > 0;

    public int ProblemModuleCount => _allModules.Count(m => m.IsSelected && !m.IsOk);

    public bool HasModuleProblems => ProblemModuleCount > 0;

    public int CompatibleCount => _allModules.Count(m => m.IsOk);
    public int NotOnServerCount => _allModules.Count(m => m.IsRisky);
    public int BlockedCount => _allModules.Count(m => m.IsBlocked);

    public string CommunityStatusText => _community.Count > 0
        ? string.Format(L["mods.communityCount"], _community.Count)
        : L["mods.communityNone"];

    public string ModsHeadline => ModuleProblem
        ? string.Format(L["mods.headline.problem"], ProblemModuleCount)
        : L["mods.headline.ok"];

    public Avalonia.Media.IBrush ModsCardBorder => new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(ModuleProblem ? "#6A2A2A" : "#3D3121"));

    public Avalonia.Media.IBrush ModsIconBackground => new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(ModuleProblem ? "#331C1E" : "#15291D"));

    partial void OnModuleProblemChanged(bool value)
    {
        OnPropertyChanged(nameof(ModsHeadline));
        OnPropertyChanged(nameof(ModsCardBorder));
        OnPropertyChanged(nameof(ModsIconBackground));
    }

    partial void OnModSearchChanged(string value) => ApplyModsView();

    partial void OnModsFilterChanged(ModsFilter value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterCompatible));
        OnPropertyChanged(nameof(FilterNotOnServer));
        OnPropertyChanged(nameof(FilterBlocked));
        OnPropertyChanged(nameof(FilterCommunityWorks));
        OnPropertyChanged(nameof(FilterCommunityBroken));
        ApplyModsView();
    }

    private static string CommunityCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "mods-compat.json");

    /// <summary>Topluluk listesini önce önbellekten, sonra GitHub'dan yükler.</summary>
    private async Task LoadCommunityAsync()
    {
        _community = ModCompatDatabase.LoadCached(CommunityCachePath);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoopKeep/" + AppVersion.TrimStart('v'));
            _community = await ModCompatDatabase.FetchAsync(http, CommunityCachePath);
        }
        catch (Exception)
        {
            // Önbellekle devam.
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(CheckModules);
    }

    /// <summary>
    /// Son Coop oturumunun loglarında adı hata satırında geçen modları bulur.
    /// </summary>
    /// <remarks>
    /// Kaynak satırlar (canlı oturumlardan): <c>module mismatch: "X"</c>,
    /// <c>Server does not support module X</c>, <c>Please disable the following module(s): X</c>.
    /// </remarks>
    private void ScanLocalModuleErrors()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new[]
        {
            new Regex("module mismatch: \"(?<id>[^\"]+)\""),
            new Regex(@"does not support module (?<id>[\w.]+)"),
            new Regex(@"disable the following module\(s\): (?<id>[\w., ]+)"),
        };

        IEnumerable<string> Lines()
        {
            foreach (var l in Console) yield return l;

            var logs = Path.Combine(DataDirectory, "logs");
            if (!Directory.Exists(logs)) yield break;

            var latest = new DirectoryInfo(logs).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(2);
            foreach (var file in latest)
            {
                string[] content;
                try
                {
                    using var reader = new StreamReader(new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete));
                    content = reader.ReadToEnd().Split('\n');
                }
                catch (IOException) { continue; }

                foreach (var l in content) yield return l;
            }
        }

        foreach (var line in Lines())
        foreach (var p in patterns)
        foreach (Match m in p.Matches(line))
        foreach (var id in m.Groups["id"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            found.Add(id);

        _localModuleErrors = found;
    }

    private void ReloadModules(ModuleCheckResult? result)
    {
        ScanLocalModuleErrors();
        _allModules.Clear();

        if (result is not null)
        {
            foreach (var m in result.All)
            {
                var c = _community.Find(m.Id);
                _allModules.Add(new ModuleRow(m.Id, m.Version, m.IsSelected, m.Status,
                    L["mod.status." + m.Status.ToString().ToLowerInvariant()])
                {
                    Community = c?.Status ?? CommunityStatus.Unknown,
                    CommunityNote = c?.Note,
                    CommunityText = c is null ? "" : L["mod.community." + c.Status.ToString().ToLowerInvariant()],
                    HadLocalError = _localModuleErrors.Contains(m.Id),
                });
            }
        }

        HasProfileBackup = ModuleCompatibility.HasProfileBackup();
        ApplyModsView();

        OnPropertyChanged(nameof(HasModules));
        OnPropertyChanged(nameof(ProblemModuleCount));
        OnPropertyChanged(nameof(HasModuleProblems));
        OnPropertyChanged(nameof(CompatibleCount));
        OnPropertyChanged(nameof(NotOnServerCount));
        OnPropertyChanged(nameof(BlockedCount));
        OnPropertyChanged(nameof(CommunityStatusText));
        OnPropertyChanged(nameof(ModsHeadline));
        ApplyCoopProfileCommand.NotifyCanExecuteChanged();
        RestoreModProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Filtre ve aramayı uygular. Aramada eşleşenler en üste çıkar, diğerleri sönük kalır.
    /// </summary>
    private void ApplyModsView()
    {
        var query = ModCompatDatabase.Normalize(ModSearch ?? "");

        bool PassesFilter(ModuleRow m) => ModsFilter switch
        {
            ModsFilter.Compatible => m.IsOk,
            ModsFilter.NotOnServer => m.IsRisky,
            ModsFilter.Blocked => m.IsBlocked,
            ModsFilter.CommunityWorks => m.CommunityWorks,
            ModsFilter.CommunityBroken => m.CommunityBroken,
            _ => true,
        };

        int Score(ModuleRow m)
        {
            if (query.Length == 0) return 0;
            var id = ModCompatDatabase.Normalize(m.Id);
            return id == query ? 3 : id.StartsWith(query, StringComparison.Ordinal) ? 2 : id.Contains(query, StringComparison.Ordinal) ? 1 : 0;
        }

        var rows = _allModules
            .Where(PassesFilter)
            .Select(m => (Row: m, Score: Score(m)))
            .OrderByDescending(x => x.Score)
            // Varsayılan sıra: sorunlu ve açık olanlar üstte, sonra açık olanlar, sonra ada göre.
            .ThenByDescending(x => x.Row.IsSelected && !x.Row.IsOk)
            .ThenByDescending(x => x.Row.IsSelected)
            .ThenBy(x => x.Row.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Row with { Dimmed = query.Length > 0 && x.Score == 0 });

        Modules.Clear();
        foreach (var r in rows) Modules.Add(r);
    }

    /// <summary>
    /// Sunucuyla uyumsuz modları oyun başlatıcısında kapatır.
    /// </summary>
    /// <remarks>
    /// Oyun açıkken yapılmaz: başlatıcı kapanırken kendi listesini dosyaya geri yazar
    /// ve değişiklik kaybolur.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanApplyCoopProfile))]
    private void ApplyCoopProfile()
    {
        if (ModuleCompatibility.IsGameRunning())
        {
            ShowToast(L["msg.closeGameFirst"], ToastKind.Warning);
            return;
        }

        try
        {
            var disabled = ModuleCompatibility.ApplyCoopProfile();
            CheckModules();
            ShowToast(disabled.Count == 0
                ? L["msg.profileNothing"]
                : string.Format(L["msg.profileApplied"], disabled.Count));
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    private bool CanApplyCoopProfile() => HasModuleProblems;

    [RelayCommand(CanExecute = nameof(CanRestoreModProfile))]
    private void RestoreModProfile()
    {
        if (ModuleCompatibility.IsGameRunning())
        {
            ShowToast(L["msg.closeGameFirst"], ToastKind.Warning);
            return;
        }

        try
        {
            if (ModuleCompatibility.RestoreProfile()) ShowToast(L["msg.profileRestored"]);
            CheckModules();
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    private bool CanRestoreModProfile() => HasProfileBackup;

    /// <summary>Bir modun Coop ile çalıştığını bildirir (GitHub issue formu açılır).</summary>
    [RelayCommand]
    private void ReportModWorks(ModuleRow row) => OpenReport(row, "works");

    [RelayCommand]
    private void ReportModBroken(ModuleRow row) => OpenReport(row, "broken");

    private void OpenReport(ModuleRow row, string status)
    {
        static string E(string s) => Uri.EscapeDataString(s);

        // Alan kimlikleri .github/ISSUE_TEMPLATE/mod-compat.yml ile aynı olmalı.
        var url = $"{IssueUrl}?template=mod-compat.yml" +
                  $"&title={E($"[Mod] {row.Id} — {status}")}" +
                  $"&mod_id={E(row.Id)}" +
                  $"&mod_version={E(row.Version ?? "")}" +
                  $"&coop_version={E(ModVersion)}" +
                  $"&game_version={E(GameVersion)}" +
                  $"&status={E(status)}";

        OpenPath(url);
    }
}
