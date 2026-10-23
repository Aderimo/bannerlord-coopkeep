using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Coop;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Coop'un bilinen tuzaklarına karşı yol gösterme: kurulum denetimi (Ana sayfa), sunucuyu
/// açmadan önceki denetim ve sunucu kapanınca ne olduğunun açıklaması.
/// </summary>
/// <remarks>
/// Kurallar <see cref="CoopChecks"/>'te; burada yalnızca bilgisayardaki durum toplanır,
/// sonuç gösterilir ve düzeltme düğmeleri çalıştırılır.
/// </remarks>
public partial class MainViewModel
{
    private const string CoopWorkshopUrl = "steam://url/CommunityFilePage/3770450698";

    /// <summary>Kurulum denetiminin satırları (Ana sayfa).</summary>
    public ObservableCollection<CheckRow> SetupChecks { get; } = [];

    /// <summary>Düzeltilmesi gereken bir kurulum sorunu var mı?</summary>
    public bool HasSetupProblems => SetupChecks.Any(c => c.Severity is CheckSeverity.Error or CheckSeverity.Warning);

    private IReadOnlyList<string> _blockedFiles = [];

    /// <summary>Kurulumu yeniden denetler (açılışta, "Yeniden denetle"de ve bir düzeltmeden sonra).</summary>
    private void RefreshSetupChecks() => _ = RefreshSetupChecksAsync();

    private async Task RefreshSetupChecksAsync()
    {
        var game = _game;
        var coop = _coop;

        // Engellenmiş dosyalar elle (zip'ten) kurulan modlarda ve sunucu paketinde aranır.
        _blockedFiles = await Task.Run(() => BlockedFiles.Find(BlockedFileFolders(game, coop)));

        var check = ModuleCompatibility.Check();
        var facts = new SetupFacts
        {
            GameFound = game is not null,
            GameVersion = game?.GameVersion,
            CoopFound = coop is not null,
            CoopVersion = coop?.ModVersion,
            CoopRequiredGameVersion = coop?.RequiredGameVersion,
            ServerPackageFound = coop is not null && File.Exists(coop.DedicatedServerExe),
            EnabledBlockedModules = check?.All.Where(m => m.IsSelected && m.Status == ModuleStatus.Blocked).Select(m => m.Id).ToList() ?? [],
            BlockedFiles = _blockedFiles.Count,
            FirewallOk = CoopFirewallOk,
        };

        SetupChecks.Clear();
        foreach (var r in CoopChecks.Setup(facts)) SetupChecks.Add(ToRow(r));
        OnPropertyChanged(nameof(HasSetupProblems));
        OnPropertyChanged(nameof(ShowSetupProblems));
    }

    private static IEnumerable<string> BlockedFileFolders(BannerlordInstallation? game, CoopInstallation? coop)
    {
        if (coop is not null)
        {
            yield return Path.Combine(coop.ModRoot, "bin");
            yield return Path.GetDirectoryName(coop.DedicatedServerExe)!;
        }

        if (game is null || !Directory.Exists(game.ModulesDirectory)) yield break;
        foreach (var module in Directory.EnumerateDirectories(game.ModulesDirectory))
        {
            if (ModuleCompatibility.ServerModules.Contains(Path.GetFileName(module))) continue;
            yield return Path.Combine(module, "bin");
        }
    }

    private CheckRow ToRow(CheckResult r)
    {
        var text = L["check." + r.Id];
        try { text = string.Format(text, r.Args.Cast<object>().ToArray()); }
        catch (FormatException) { }

        var fixLabel = r.Fix == CheckFix.None ? "" : L["checkfix." + r.Fix];
        return new CheckRow(r.Severity, text, r.Fix, fixLabel);
    }

    /// <summary>Bir denetimin düzeltme düğmesi.</summary>
    [RelayCommand]
    private async Task RunCheckFixAsync(CheckFix fix)
    {
        switch (fix)
        {
            case CheckFix.OpenGameStore:
                OpenPath("steam://store/261550");
                break;

            case CheckFix.OpenCoopWorkshop:
                OpenPath(CoopWorkshopUrl);
                break;

            case CheckFix.DisableDlc:
                if (ModuleCompatibility.IsGameRunning())
                {
                    ShowToast(L["coop.gameRunning"], ToastKind.Warning);
                    return;
                }

                try
                {
                    var disabled = ModuleCompatibility.ApplyCoopProfile();
                    ShowToast(string.Format(L["checkfix.dlcDone"], string.Join(", ", disabled)), ToastKind.Success);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                {
                    ShowToast(ex.Message, ToastKind.Error);
                }
                break;

            case CheckFix.UnblockFiles:
                var count = await Task.Run(() => BlockedFiles.Unblock(_blockedFiles));
                ShowToast(string.Format(L["checkfix.unblockDone"], count), ToastKind.Success);
                break;

            case CheckFix.AddFirewallRule:
                await AddCoopFirewallRuleAsync();
                break;

            case CheckFix.StartSteam:
                OpenPath("steam://open/main");
                break;

            case CheckFix.OpenBackups:
                PreflightOpen = false;
                CurrentPage = AppPage.Backups;
                return;

            case CheckFix.OpenWorldMods:
                PreflightOpen = false;
                CurrentPage = AppPage.Worlds;
                return;

            case CheckFix.OpenModsPage:
                PreflightOpen = false;
                CurrentPage = AppPage.Mods;
                return;

            case CheckFix.GetMissingMods:
                PreflightOpen = false;
                GetMissingMods(_preflightMissing);
                return;
        }

        await RefreshSetupChecksAsync();
    }

    // ------------------------------------------------------------------
    // Sunucuyu açmadan önce
    // ------------------------------------------------------------------

    [ObservableProperty] private bool _preflightOpen;

    public ObservableCollection<CheckRow> PreflightChecks { get; } = [];

    /// <summary>Hata varsa "yine de başlat" gösterilmez.</summary>
    [ObservableProperty] private bool _preflightCanContinue;

    private TaskCompletionSource<bool>? _preflightAnswer;

    /// <summary>
    /// Coop'un tuzaklarını denetler. Uyarı ya da hata varsa listeyi gösterir ve kullanıcının
    /// kararını bekler; yalnızca bilgi varsa konsola yazar ve devam eder.
    /// </summary>
    private async Task<bool> ConfirmPreflightAsync(string worldName)
    {
        var results = CoopChecks.Preflight(CollectPreflightFacts(worldName));

        foreach (var info in results.Where(r => r.Severity == CheckSeverity.Info))
            AppendConsole("— " + ToRow(info).Text);

        var problems = results.Where(r => r.Severity is CheckSeverity.Warning or CheckSeverity.Error).ToList();
        if (problems.Count == 0) return true;

        PreflightChecks.Clear();
        foreach (var r in problems) PreflightChecks.Add(ToRow(r));
        PreflightCanContinue = !CoopChecks.HasErrors(problems);

        _preflightAnswer = new TaskCompletionSource<bool>();
        PreflightOpen = true;
        var answer = await _preflightAnswer.Task;
        PreflightOpen = false;
        return answer;
    }

    /// <summary>Son ön denetimde bulunamayan dünya modları (kimlikler).</summary>
    private List<string> _preflightMissing = [];

    private PreflightFacts CollectPreflightFacts(string worldName)
    {
        var cfg = _config?.Read();
        var mods = WorldModIds(worldName).ToList();
        var installed = _game is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : FreshGameModules().Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _preflightMissing = mods.Where(m => !installed.Contains(m)).ToList();

        var env = new Dictionary<string, string>();
        foreach (var name in CoopChecks.CoopEnvironmentVariables)
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) env[name] = value;

        return new PreflightFacts
        {
            WorldName = worldName,
            WorldSaveExists = _saves?.Exists(worldName) == true,
            SteamLobbyEnabled = cfg?.Steam ?? true,
            SteamRunning = IsSteamRunning(),
            DocumentsOnOneDrive = CoopChecks.IsOneDrivePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            Environment = env,
            WorldMods = mods,
            // Görünen metin: Steam'de abone olunmamışsa bunu da söyler.
            MissingWorldMods = _preflightMissing.Select(id => DescribeMissing([id])).ToList(),
            Port = cfg?.Port ?? Core.Configuration.ServerConfig.DefaultPort,
        };
    }

    private static bool IsSteamRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.GetProcessesByName("steam");
            foreach (var x in p) x.Dispose();
            return p.Length > 0;
        }
        catch (InvalidOperationException)
        {
            return true; // bilinmiyor: uyarma
        }
    }

    [RelayCommand]
    private void PreflightContinue() => _preflightAnswer?.TrySetResult(true);

    [RelayCommand]
    private void PreflightCancel() => _preflightAnswer?.TrySetResult(false);

    // ------------------------------------------------------------------
    // Sunucu kapanınca
    // ------------------------------------------------------------------

    /// <summary>Sunucunun kapanma sebebini ve ne yapılacağını ana sayfada söyler.</summary>
    private void ExplainExit(ServerExitInfo info)
    {
        switch (info.Kind)
        {
            case ServerExitKind.LoadFailure:
                AlertText = L["exit.LoadFailure"];
                break;

            case ServerExitKind.ModuleVerificationFailed:
                AlertText = ServerToolsEnabled ? L["exit.ModuleVerificationFailed.tools"] : L["exit.ModuleVerificationFailed"];
                break;
        }
    }

    /// <summary>Konsola yazılan, iki dilli kapanış açıklaması.</summary>
    private string DescribeExit(ServerExitKind kind) => L["exit." + kind];
}

/// <summary>Bir denetim satırı: önemi, metni, düzeltme düğmesi.</summary>
public sealed record CheckRow(CheckSeverity Severity, string Text, CheckFix Fix, string FixLabel)
{
    public bool HasFix => FixLabel.Length > 0;
    public bool IsOk => Severity == CheckSeverity.Ok;
    public bool IsInfo => Severity == CheckSeverity.Info;
    public bool IsWarning => Severity == CheckSeverity.Warning;
    public bool IsError => Severity == CheckSeverity.Error;
}

/// <summary>Rehber sayfasındaki düğmelerin kullandığı düzeltmeler (XAML'den <c>x:Static</c> ile).</summary>
public static class GuideFixes
{
    public static readonly CheckFix Workshop = CheckFix.OpenCoopWorkshop;
}
