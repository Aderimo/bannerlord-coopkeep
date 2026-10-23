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

    /// <summary>Bu bilgisayarda yüklü mü? "Tüm modlar" sekmesinde yüklü olmayanlar da var.</summary>
    public bool IsInstalled { get; init; } = true;
    public bool NotInstalled => !IsInstalled;
    public string DisplayName { get; init; } = "";
    public string Title => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Id ? Id : DisplayName;
    public string? Category { get; init; }
    public string? Url { get; init; }
    public bool HasUrl => !string.IsNullOrEmpty(Url);
    public int Works { get; init; }
    public int Broken { get; init; }
    public string VotesText => Works + Broken == 0 ? "" : $"✓ {Works}  ✗ {Broken}";

    public string? WorkshopId { get; init; }
    public string? WorkshopUrl { get; init; }
    public string? NexusUrl { get; init; }
    public bool HasWorkshop => !string.IsNullOrEmpty(WorkshopUrl);
    public bool HasNexus => !string.IsNullOrEmpty(NexusUrl);
    public bool HasCommunityNote => !string.IsNullOrWhiteSpace(CommunityNote);
}

/// <summary>Sayfa düğmesi; <see cref="Number"/> 0 ise "…" boşluğu.</summary>
public sealed record ModsPageLink(int Number, bool IsCurrent)
{
    public bool IsGap => Number == 0;
    public bool IsLink => Number > 0;
    public string Label => IsGap ? "…" : Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Modlar sayfasının küçük dönüştürücüleri.</summary>
public static class ModConverters
{
    public static readonly Avalonia.Data.Converters.IValueConverter IsUnknown =
        new Avalonia.Data.Converters.FuncValueConverter<CommunityStatus, bool>(s => s == CommunityStatus.Unknown);
}

public enum ModsFilter { All, Compatible, NotOnServer, Blocked, CommunityWorks, CommunityBroken, Library }

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
    public bool FilterLibrary { get => ModsFilter == ModsFilter.Library; set { if (value) ModsFilter = ModsFilter.Library; } }

    /// <summary>Topluluk listesindeki mod sayısı ("Tüm modlar" sekmesi).</summary>
    public int LibraryCount => _community.Count;

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
        OnPropertyChanged(nameof(ShowModuleBanner));
        OnPropertyChanged(nameof(ModsHeadline));
        OnPropertyChanged(nameof(ModsCardBorder));
        OnPropertyChanged(nameof(ModsIconBackground));
    }

    partial void OnModSearchChanged(string value) => ApplyModsView(resetPage: true);

    partial void OnModsFilterChanged(ModsFilter value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterCompatible));
        OnPropertyChanged(nameof(FilterNotOnServer));
        OnPropertyChanged(nameof(FilterBlocked));
        OnPropertyChanged(nameof(FilterCommunityWorks));
        OnPropertyChanged(nameof(FilterCommunityBroken));
        OnPropertyChanged(nameof(FilterLibrary));
        ApplyModsView(resetPage: true);
    }

    private static string CommunityCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "mods-compat.json");

    /// <summary>Topluluk listesini önce önbellekten, sonra GitHub'dan yükler.</summary>
    private async Task LoadCommunityAsync()
    {
        _community = ModCompatDatabase.LoadCached(CommunityCachePath);

        try
        {
            using var http = CoopKeep.Core.Net.FastHttp.Create(TimeSpan.FromSeconds(8));
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
                try { content = ReadLogTail(file.FullName).Split('\n'); }
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

    /// <summary>Taranacak en fazla son bayt: günlükler 16 MB'a çıkabiliyor, katılma hataları sonda olur.</summary>
    private const int LogTailBytes = 2 * 1024 * 1024;

    private static string ReadLogTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > LogTailBytes) stream.Seek(-LogTailBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
                    DisplayName = c?.DisplayName ?? m.Id,
                    Category = c?.Category,
                    Url = c?.Url,
                    Works = c?.Works ?? 0,
                    Broken = c?.Broken ?? 0,
                    WorkshopId = c?.WorkshopId,
                    WorkshopUrl = c?.WorkshopUrl,
                    NexusUrl = c?.NexusUrl,
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
        OnPropertyChanged(nameof(LibraryCount));
        OnPropertyChanged(nameof(ModsHeadline));
        ApplyCoopProfileCommand.NotifyCanExecuteChanged();
        RestoreModProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Filtre ve aramayı uygular. Aramada eşleşenler en üste çıkar, diğerleri sönük kalır.
    /// </summary>
    private void ApplyModsView(bool resetPage = false)
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
            var best = 0;
            foreach (var text in new[] { m.Id, m.DisplayName })
            {
                var t = ModCompatDatabase.Normalize(text ?? "");
                best = Math.Max(best, t == query ? 3 : t.StartsWith(query, StringComparison.Ordinal) ? 2 : t.Contains(query, StringComparison.Ordinal) ? 1 : 0);
            }

            return best;
        }

        if (ModsFilter == ModsFilter.Library)
        {
            // Topluluk listesinin tamamı: yüklü olmayan modlar da aranabilir ve bildirilebilir.
            var installed = new Dictionary<ModCompatEntry, ModuleRow>(ReferenceEqualityComparer.Instance);
            foreach (var m in _allModules)
                if (_community.Find(m.Id) is { } entry) installed.TryAdd(entry, m);

            var library = _community.All
                .Select(c => installed.TryGetValue(c, out var row)
                    ? row
                    : new ModuleRow(c.Id, null, false, ModuleStatus.NotOnServer, L["mod.notInstalled"])
                    {
                        IsInstalled = false,
                        Community = c.Status,
                        CommunityNote = c.Note,
                        CommunityText = L["mod.community." + c.Status.ToString().ToLowerInvariant()],
                        DisplayName = c.DisplayName,
                        Category = c.Category,
                        Url = c.Url,
                        Works = c.Works,
                        Broken = c.Broken,
                        WorkshopId = c.WorkshopId,
                        WorkshopUrl = c.WorkshopUrl,
                        NexusUrl = c.NexusUrl,
                    })
                .Select(m => (Row: m, Score: Score(m)))
                .Where(x => query.Length == 0 || x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Row.IsInstalled)
                .ThenBy(x => x.Row.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => x.Row);

            SetModsView(library, resetPage);
            return;
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

        SetModsView(rows, resetPage);
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

    [RelayCommand]
    private void OpenModPage(ModuleRow? row)
    {
        if (row?.Url is { } url) OpenPath(url);
    }

    private void OpenReport(ModuleRow row, string status)
    {
        static string E(string s) => Uri.EscapeDataString(s);

        // Alan kimlikleri .github/ISSUE_TEMPLATE/mod-compat.yml ile aynı olmalı.
        var url = $"{IssueUrl}?template=mod-compat.yml" +
                  $"&title={E($"[Mod] {row.Id} — {status}")}" +
                  $"&mod_id={E(row.Id)}" +
                  $"&mod_name={E(row.Title)}" +
                  $"&mod_version={E(row.Version ?? "")}" +
                  $"&coop_version={E(ModVersion)}" +
                  $"&game_version={E(GameVersion)}" +
                  $"&status={E(status)}";

        OpenPath(url);
    }

    // --- sayfalama -------------------------------------------------------

    public const int ModsPageSize = 50;

    private List<ModuleRow> _modsView = [];

    [ObservableProperty] private int _modsPage = 1;

    public ObservableCollection<ModsPageLink> ModsPageLinks { get; } = [];

    public int ModsPageCount => Math.Max(1, (_modsView.Count + ModsPageSize - 1) / ModsPageSize);

    public bool ModsHasPages => ModsPageCount > 1;

    /// <summary>"51–100 / 529"</summary>
    public string ModsRangeText => _modsView.Count == 0
        ? ""
        : string.Format(L["mods.page.range"], (ModsPage - 1) * ModsPageSize + 1,
            Math.Min(ModsPage * ModsPageSize, _modsView.Count), _modsView.Count);

    partial void OnModsPageChanged(int value) => ShowModsPage();

    /// <summary>Süzülmüş listeyi saklar; arama/filtre değiştiyse 1. sayfaya döner.</summary>
    private void SetModsView(IEnumerable<ModuleRow> rows, bool resetPage)
    {
        _modsView = rows.ToList();
        var page = resetPage ? 1 : Math.Clamp(ModsPage, 1, ModsPageCount);
        if (page != ModsPage) ModsPage = page;
        else ShowModsPage();
    }

    private void ShowModsPage()
    {
        Modules.Clear();
        foreach (var r in _modsView.Skip((ModsPage - 1) * ModsPageSize).Take(ModsPageSize)) Modules.Add(r);

        // 1 … 4 5 [6] 7 8 … 11
        ModsPageLinks.Clear();
        var last = 0;
        for (var n = 1; n <= ModsPageCount; n++)
        {
            if (n != 1 && n != ModsPageCount && Math.Abs(n - ModsPage) > 2) continue;
            if (last != 0 && n - last > 1) ModsPageLinks.Add(new ModsPageLink(0, false));
            ModsPageLinks.Add(new ModsPageLink(n, n == ModsPage));
            last = n;
        }

        OnPropertyChanged(nameof(ModsPageCount));
        OnPropertyChanged(nameof(ModsHasPages));
        OnPropertyChanged(nameof(ModsRangeText));
        ModsPrevPageCommand.NotifyCanExecuteChanged();
        ModsNextPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanModsPrevPage))]
    private void ModsPrevPage() => ModsPage--;

    private bool CanModsPrevPage() => ModsPage > 1;

    [RelayCommand(CanExecute = nameof(CanModsNextPage))]
    private void ModsNextPage() => ModsPage++;

    private bool CanModsNextPage() => ModsPage < ModsPageCount;

    [RelayCommand]
    private void GoToModsPage(ModsPageLink? link)
    {
        if (link is { IsLink: true }) ModsPage = Math.Clamp(link.Number, 1, ModsPageCount);
    }

    // --- mod bilgi penceresi ---------------------------------------------

    private static readonly WorkshopDetailsService WorkshopService = new(WorkshopDetailsService.DefaultCacheDirectory);

    private static readonly Lazy<HttpClient> WorkshopHttp = new(() =>
    {
        var http = CoopKeep.Core.Net.FastHttp.Create(TimeSpan.FromSeconds(10));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CoopKeep/" + AppVersion.TrimStart('v'));
        return http;
    });

    /// <summary>Pencere hızlı açılıp kapanırsa eski isteğin sonucu yenisinin üstüne yazılmasın.</summary>
    private int _modInfoRequest;

    [ObservableProperty] private ModuleRow? _modInfo;
    [ObservableProperty] private bool _modInfoOpen;
    [ObservableProperty] private bool _modInfoLoading;
    [ObservableProperty] private WorkshopDetails? _modInfoWorkshop;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _modInfoImage;

    public bool ModInfoHasImage => ModInfoImage is not null;
    public bool ModInfoHasDescription => ModInfoWorkshop is { Description.Length: > 0 };
    public bool ModInfoNoDescription => !ModInfoLoading && !ModInfoHasDescription;

    public string ModInfoNoDescriptionText => ModInfo?.WorkshopId is null
        ? L["modinfo.noWorkshop"]
        : L["modinfo.descFailed"];

    public string ModInfoStatsText
    {
        get
        {
            if (ModInfoWorkshop is not { } w) return "";
            var culture = System.Globalization.CultureInfo.GetCultureInfo(L.IsTurkish ? "tr-TR" : "en-US");
            return string.Format(L["modinfo.stats"], w.Subscriptions.ToString("N0", culture),
                w.Updated?.LocalDateTime.ToString("d", culture) ?? "—");
        }
    }

    public bool ModInfoStale => ModInfoWorkshop?.IsStale == true;

    /// <summary>Yüklü modda sürüm ve sunucu durumu; değilse "Yüklü değil".</summary>
    public string ModInfoInstallText => ModInfo is not { } m
        ? ""
        : m.IsInstalled
            ? string.Format(L["modinfo.installed"], string.IsNullOrEmpty(m.Version) ? "—" : m.Version,
                L[m.IsSelected ? "modinfo.enabled" : "modinfo.disabled"])
            : L["mod.notInstalled"];

    partial void OnModInfoChanged(ModuleRow? value)
    {
        OnPropertyChanged(nameof(ModInfoInstallText));
        OnPropertyChanged(nameof(ModInfoNoDescriptionText));
    }

    partial void OnModInfoLoadingChanged(bool value) => OnPropertyChanged(nameof(ModInfoNoDescription));

    partial void OnModInfoWorkshopChanged(WorkshopDetails? value)
    {
        OnPropertyChanged(nameof(ModInfoHasDescription));
        OnPropertyChanged(nameof(ModInfoNoDescription));
        OnPropertyChanged(nameof(ModInfoStatsText));
        OnPropertyChanged(nameof(ModInfoStale));
    }

    partial void OnModInfoImageChanged(Avalonia.Media.Imaging.Bitmap? value) => OnPropertyChanged(nameof(ModInfoHasImage));

    /// <summary>
    /// Modun bilgi penceresini açar. Workshop kimliği varsa açıklama ve kapak görseli
    /// Steam'den (yoksa önbellekten) arka planda gelir.
    /// </summary>
    [RelayCommand]
    private async Task OpenModInfo(ModuleRow? row)
    {
        if (row is null) return;

        var request = ++_modInfoRequest;
        ModInfo = row;
        ModInfoWorkshop = null;
        ModInfoImage = null;
        ModInfoOpen = true;

        if (row.WorkshopId is not { } id)
        {
            ModInfoLoading = false;
            return;
        }

        ModInfoLoading = true;
        WorkshopDetails? details = null;
        Avalonia.Media.Imaging.Bitmap? image = null;
        try
        {
            details = await Task.Run(() => WorkshopService.GetAsync(WorkshopHttp.Value, id));
            if (details?.ImagePath is { } path) image = await Task.Run(() => GameImages.Decode(path));
        }
        catch (Exception)
        {
            // Açıklama yoksa pencere bağlantıyla devam eder.
        }

        if (request != _modInfoRequest) return;
        ModInfoWorkshop = details;
        ModInfoImage = image;
        ModInfoLoading = false;
    }

    [RelayCommand]
    private void CloseModInfo()
    {
        _modInfoRequest++;
        ModInfoOpen = false;
        ModInfoLoading = false;
    }

    [RelayCommand]
    private void OpenModWorkshop()
    {
        if (ModInfo?.WorkshopUrl is { } url) OpenPath(url);
    }

    [RelayCommand]
    private void OpenModNexus()
    {
        if (ModInfo?.NexusUrl is { } url) OpenPath(url);
    }
}
