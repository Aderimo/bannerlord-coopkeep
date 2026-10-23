using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.App.Localization;
using CoopKeep.Core.Configuration;
using CoopKeep.Core.Mods;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Saves;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Yedekler: elle ve otomatik yedek, geri yükleme, liste.</summary>
public partial class MainViewModel
{
    // --- yedekleme -------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanBackupNow))]
    private void BackupNow()
    {
        if (_backups is null || SelectedCampaign is null) return;

        try
        {
            _backups.Create(SelectedCampaign.Name, BackupReason.Manual);
            LastBackupAt = DateTime.Now;
            ShowToast(L["msg.backupTaken"]);
            ReloadBackups();
        }
        catch (Exception ex) { ShowToast(ex.Message, ToastKind.Error); }
    }

    private bool CanBackupNow() => _backups is not null && SelectedCampaign is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void RequestRestore() => PendingRestore = true;

    [RelayCommand]
    private void CancelRestore() => PendingRestore = false;

    /// <summary>
    /// Seçili yedeği geri yükler. Sunucu çalışırken yapılamaz.
    /// </summary>
    /// <remarks>
    /// Geri yükleme, mevcut hâlin yedeğini almakla başlar — yanlış yedeği seçen
    /// kullanıcının geri dönüş yolu olsun diye.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void ConfirmRestore()
    {
        if (_backups is null || SelectedBackup is null) return;

        try
        {
            _backups.Restore(SelectedBackup);
            ShowToast(L["msg.restored"]);
            PendingRestore = false;

            ReloadBackups();
            ReloadCampaigns();
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
            PendingRestore = false;
        }
    }

    private bool CanRestore() =>
        _backups is not null && SelectedBackup is not null && !IsRunning && !IsBusy;

    /// <summary>Yedekler sayfası tüm dünyaların yedeklerini mi gösteriyor?</summary>
    [ObservableProperty] private bool _backupsShowAll;

    public bool BackupsShowSelected { get => !BackupsShowAll; set => BackupsShowAll = !value; }

    partial void OnBackupsShowAllChanged(bool value)
    {
        OnPropertyChanged(nameof(BackupsShowSelected));
        ReloadBackups();
    }

    private void ReloadBackups()
    {
        Backups.Clear();

        if (_backups is not null)
        {
            var list = BackupsShowAll ? _backups.ListAll()
                : SelectedCampaign is not null ? _backups.List(SelectedCampaign.Name)
                : [];
            foreach (var b in list) Backups.Add(b);
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(LastBackupText));
        SelectedBackup = Backups.FirstOrDefault();
    }

    /// <summary>
    /// Sunucunun dünyayı kaydettiğini bildiren satırı yakalar ve otomatik yedek alır.
    /// </summary>
    /// <remarks>
    /// <b>Yalnızca doğrulanmış bir kayıttan sonra</b> yedek alıyoruz. Zamanlayıcıyla
    /// rastgele bir anda yedek almak, sunucu dosyayı yazarken yarım bir kopya
    /// üretebilirdi.
    /// <para>Kaynak satırlar: <c>autosave: world saved to '...'</c>,
    /// <c>operator save: ...</c>, <c>shutdown save: ...</c></para>
    /// </remarks>
    private void TryAutoBackup(string line)
    {
        if (!line.Contains("world saved to", StringComparison.Ordinal)) return;
        LastSaveAt = DateTime.Now;

        if (!AutoBackup) return;
        if (_backups is null || ActiveSaveName is not { Length: > 0 } name) return;

        // Kaynak satırlar: "autosave: …", "operator save: …", "shutdown save: …"
        var reason = line.Contains("autosave", StringComparison.Ordinal) ? BackupReason.Autosave
            : line.Contains("shutdown", StringComparison.Ordinal) ? BackupReason.Shutdown
            : BackupReason.Manual;

        try
        {
            _backups.Create(name, reason);
            LastBackupAt = DateTime.Now;

            if (SelectedCampaign?.Name == name) ReloadBackups();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
    }
}
