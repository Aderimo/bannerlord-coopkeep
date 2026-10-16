using System;
using System.Collections.ObjectModel;
using System.Linq;
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

    /// <summary>Kapalı ve sorunsuz modlar sönük gösterilir.</summary>
    public double RowOpacity => IsSelected ? 1.0 : 0.5;
}

/// <summary>
/// Mod uyumluluğu sayfası ve "Coop profili".
/// </summary>
/// <remarks>
/// Arkadaşların bağlanamamasının en sık sebebi fazladan açık modlar ve oyunun hata
/// metni bunu açıkça söylemiyor. Bu sayfa her modu tek tek işaretliyor ve
/// sorunlu olanları tek tıkla kapatıp sonra geri açmayı sağlıyor.
/// </remarks>
public partial class MainViewModel
{
    public ObservableCollection<ModuleRow> Modules { get; } = [];

    [ObservableProperty] private bool _hasProfileBackup;

    public bool HasModules => Modules.Count > 0;

    public int ProblemModuleCount => Modules.Count(m => m.IsSelected && !m.IsOk);

    public bool HasModuleProblems => ProblemModuleCount > 0;

    public string ModsHeadline => ModuleProblem
        ? string.Format(L["mods.headline.problem"], ProblemModuleCount)
        : L["mods.headline.ok"];

    public Avalonia.Media.IBrush ModsCardBorder => new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(ModuleProblem ? "#5A2A2A" : "#2A2F39"));

    public Avalonia.Media.IBrush ModsIconBackground => new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(ModuleProblem ? "#331C1E" : "#15291D"));

    partial void OnModuleProblemChanged(bool value)
    {
        OnPropertyChanged(nameof(ModsHeadline));
        OnPropertyChanged(nameof(ModsCardBorder));
        OnPropertyChanged(nameof(ModsIconBackground));
    }

    private void ReloadModules(ModuleCheckResult? result)
    {
        Modules.Clear();

        if (result is not null)
        {
            var rows = result.All
                .OrderByDescending(m => m.IsSelected && m.Status != ModuleStatus.Supported)
                .ThenByDescending(m => m.IsSelected)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var m in rows)
            {
                var text = L["mod.status." + m.Status.ToString().ToLowerInvariant()];
                Modules.Add(new ModuleRow(m.Id, m.Version, m.IsSelected, m.Status, text));
            }
        }

        HasProfileBackup = ModuleCompatibility.HasProfileBackup();
        OnPropertyChanged(nameof(HasModules));
        OnPropertyChanged(nameof(ProblemModuleCount));
        OnPropertyChanged(nameof(HasModuleProblems));
        OnPropertyChanged(nameof(ModsHeadline));
        ApplyCoopProfileCommand.NotifyCanExecuteChanged();
        RestoreModProfileCommand.NotifyCanExecuteChanged();
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
}
