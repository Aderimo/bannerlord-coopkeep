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

/// <summary>Dünya (kampanya) dosyaları: oluşturma, yeniden adlandırma, kopyalama, silme.</summary>
public partial class MainViewModel
{
    // --- kampanya dosya işlemleri ---------------------------------------
    //
    // Hepsi .sav + .json çiftini birlikte taşır ve yalnızca sunucu duruyorken
    // çalışır: açık bir kampanyanın dosyalarını değiştirmek save bozulmasına yol açar.
    // Dünyanın ayar profili de onunla birlikte taşınır.

    [RelayCommand(CanExecute = nameof(CanModifyCampaign))]
    private void RenameCampaign()
    {
        if (_saves is null || SelectedCampaign is null) return;

        var source = SelectedCampaign.Name;
        var target = CampaignNameInput.Trim();
        if (!SaveSet.IsValidSaveName(target) || _saves.Exists(target)) return;

        try
        {
            var wasActive = string.Equals(source, ActiveSaveName, StringComparison.OrdinalIgnoreCase);
            _saves.Rename(source, target);
            _profiles.Rename(source, target);

            // Aktif kampanyanın adı değiştiyse yapılandırma da güncellenmeli,
            // yoksa sunucu var olmayan bir save aramaya çalışır.
            if (wasActive) _config?.EnsureSaveName(target);

            CampaignNameInput = "";
            ReloadConfig();
            ReloadCampaigns();
            SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == target) ?? SelectedCampaign;
            ShowToast(string.Format(L["msg.renamed"], target));
        }
        catch (Exception ex) { ShowToast(ex.Message, ToastKind.Error); }
    }

    [RelayCommand(CanExecute = nameof(CanModifyCampaign))]
    private void DuplicateCampaign()
    {
        if (_saves is null || SelectedCampaign is null) return;

        var target = CampaignNameInput.Trim();
        if (!SaveSet.IsValidSaveName(target) || _saves.Exists(target)) return;

        try
        {
            _saves.Duplicate(SelectedCampaign.Name, target);
            _profiles.Duplicate(SelectedCampaign.Name, target);
            CampaignNameInput = "";
            ReloadCampaigns();
            ShowToast(string.Format(L["msg.duplicated"], target));
        }
        catch (Exception ex) { ShowToast(ex.Message, ToastKind.Error); }
    }

    /// <summary>Silmeyi başlatır — asıl silme onaydan sonra yapılır.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteCampaign))]
    private void RequestDelete() => PendingDelete = true;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = false;

    [RelayCommand(CanExecute = nameof(CanDeleteCampaign))]
    private void ConfirmDelete()
    {
        if (_saves is null || SelectedCampaign is null) return;

        try
        {
            var name = SelectedCampaign.Name;

            // Dünyanın kendisi + Coop'un yedek kuşakları + bizim yedeklerimiz + ayar profili.
            var files = _saves.Delete(name);
            var backups = _backups?.DeleteAll(name) ?? 0;
            _profiles.Delete(name);

            PendingDelete = false;
            ShowToast(string.Format(L["msg.deleted"], files, backups));

            ReloadCampaigns();
            ReloadBackups();
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
            PendingDelete = false;
        }
    }

    private bool CanModifyCampaign() =>
        !IsRunning && !IsBusy && SelectedCampaign is not null
        && SaveSet.IsValidSaveName(CampaignNameInput.Trim())
        && _saves?.Exists(CampaignNameInput.Trim()) != true;

    private bool CanDeleteCampaign() => !IsRunning && !IsBusy && SelectedCampaign is not null;
}
