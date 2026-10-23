using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Mods;
using CoopKeep.Core.Saves;

namespace CoopKeep.App.ViewModels;

/// <summary>Mod bul sekmesi: Steam Atölyesi ve Nexus araması.</summary>
public partial class MainViewModel
{
    // =====================================================================
    // Mod bul
    // =====================================================================

    public ObservableCollection<BrowseCard> BrowseCards { get; } = [];

    [ObservableProperty] private ModSite _browseSite = ModSite.Workshop;
    [ObservableProperty] private string _browseQuery = "";
    [ObservableProperty] private bool _browseBusy;
    [ObservableProperty] private string _browseMessage = "";
    [ObservableProperty] private int _browsePage = 1;

    public bool BrowseWorkshop { get => BrowseSite == ModSite.Workshop; set { if (value) BrowseSite = ModSite.Workshop; } }
    public bool BrowseNexus { get => BrowseSite == ModSite.Nexus; set { if (value) BrowseSite = ModSite.Nexus; } }
    public bool HasBrowseMessage => BrowseMessage.Length > 0;

    private CancellationTokenSource? _browseCts;

    partial void OnBrowseSiteChanged(ModSite value)
    {
        OnPropertyChanged(nameof(BrowseWorkshop));
        OnPropertyChanged(nameof(BrowseNexus));
        BrowsePage = 1;
        _ = SearchModsAsync();
    }

    partial void OnBrowseMessageChanged(string value) => OnPropertyChanged(nameof(HasBrowseMessage));

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        _browseCts?.Cancel();
        var cts = _browseCts = new CancellationTokenSource();
        BrowseBusy = true;
        BrowseMessage = "";
        BrowseCards.Clear();

        try
        {
            var results = BrowseSite == ModSite.Workshop
                ? await WorkshopSearch.SearchAsync(ModsHttp.Value, BrowseQuery, BrowsePage, cts.Token)
                : await NexusSearch.SearchAsync(ModsHttp.Value, BrowseQuery, BrowsePage, cts.Token);
            if (cts.IsCancellationRequested) return;

            var culture = System.Globalization.CultureInfo.GetCultureInfo(L.IsTurkish ? "tr-TR" : "en-US");
            foreach (var r in results)
            {
                var meta = r.Site == ModSite.Workshop
                    ? string.Format(L["mm.meta.workshop"], r.Popularity.ToString("N0", culture), r.Updated?.LocalDateTime.ToString("d", culture) ?? "—")
                    : string.Format(L["mm.meta.nexus"], r.Author ?? "?", r.Popularity.ToString("N0", culture), r.Endorsements.ToString("N0", culture));
                BrowseCards.Add(new BrowseCard(r) { Meta = meta });
            }

            RefreshBrowseInstalled();
            if (BrowseCards.Count == 0) BrowseMessage = L["mm.noResults"];
            _ = LoadCardImagesAsync(BrowseCards.ToList(), cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            BrowseMessage = string.Format(L["mm.searchFailed"], ex.Message);
        }
        finally
        {
            if (ReferenceEquals(cts, _browseCts)) BrowseBusy = false;
        }
    }

    [RelayCommand]
    private Task BrowseNextPage()
    {
        BrowsePage++;
        return SearchModsAsync();
    }

    [RelayCommand]
    private Task BrowsePrevPage()
    {
        if (BrowsePage > 1) BrowsePage--;
        return SearchModsAsync();
    }

    /// <summary>Kart görselleri sonuçlar göründükten sonra, en fazla 6'sı aynı anda iner.</summary>
    private static async Task LoadCardImagesAsync(IReadOnlyList<BrowseCard> cards, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(cards.Select(async card =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var key = (card.IsWorkshop ? "ws-" : "nx-") + card.Result.Id;
                var path = await ModImageCache.GetAsync(ModsHttp.Value, card.Result.ImageUrl, ModImagesDir, key, ct).ConfigureAwait(false);
                var bitmap = LoadBitmap(path);
                if (bitmap is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() => card.Image = bitmap);
            }
            catch (OperationCanceledException) { }
            finally { gate.Release(); }
        })).ConfigureAwait(false);
    }

    private static Bitmap? LoadBitmap(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 320);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Kurulu Atölye öğelerini işaretler (kartta "Kurulu").</summary>
    private void RefreshBrowseInstalled()
    {
        var workshop = new HashSet<string>(_installedModules.Where(m => m.WorkshopId is not null).Select(m => m.WorkshopId!), StringComparer.Ordinal);
        foreach (var card in BrowseCards)
            if (card.IsWorkshop && workshop.Contains(card.Result.Id)) { card.Installed = true; card.Waiting = false; }
        if (BrowseCards.Count > 0) MarkWorldMods();
    }

    [RelayCommand]
    private void OpenBrowsePage(BrowseCard? card)
    {
        if (card is not null) OpenPath(card.Result.PageUrl);
    }

    /// <summary>
    /// Modu edin: Atölye'de Steam'deki sayfası açılır (abone ol), Nexus'ta anahtar varsa
    /// doğrudan iner ve kurulur, yoksa sayfası açılır.
    /// </summary>
    [RelayCommand]
    private async Task GetBrowseModAsync(BrowseCard? card)
    {
        if (card is null) return;

        if (card.IsWorkshop)
        {
            SubscribeWorkshop(card.Result.Id, card);
            return;
        }

        if (string.IsNullOrWhiteSpace(_ui.NexusApiKey) || GameModulesDir is not { } modules)
        {
            OpenPath(card.Result.PageUrl + "?tab=files");
            ShowToast(L["mm.nexusManual"], ToastKind.Info);
            return;
        }

        card.Waiting = true;
        try
        {
            var zip = await NexusSearch.DownloadMainFileAsync(ModsHttp.Value, _ui.NexusApiKey!, card.Result.Id, Path.Combine(CoopKeepData, "downloads"));
            if (zip is null)
            {
                OpenPath(card.Result.PageUrl + "?tab=files");
                ShowToast(L["mm.nexusNotPremium"], ToastKind.Info);
                return;
            }

            var skipped = new List<string>();
            var installed = ModArchiveInstaller.InstallZip(zip, modules, RemovedModsDir, WorkshopModuleIds(), skipped);
            ReportSkipped(skipped);
            LoadManagedMods();
            card.Installed = true;
            ShowToast(string.Format(L["mm.installedFromZip"], string.Join(", ", installed.Select(m => m.Name))), ToastKind.Success);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowToast(string.Format(L["mm.installFailed"], ex.Message), ToastKind.Error);
        }
        finally
        {
            card.Waiting = false;
        }
    }

    /// <summary>Atölye öğesinin Steam sayfasını açar ve inene kadar (en çok 15 dk) bekler.</summary>
    private void SubscribeWorkshop(string workshopId, BrowseCard? card = null)
    {
        OpenPath(WorkshopSearch.SteamClientUrl(workshopId));
        ShowToast(L["mm.subscribeHint"], ToastKind.Info);
        if (card is not null) card.Waiting = true;
        _ = WaitForWorkshopAsync(workshopId, card);
    }

    private async Task WaitForWorkshopAsync(string workshopId, BrowseCard? card)
    {
        var started = DateTime.UtcNow;
        var until = started.AddMinutes(15);
        var hinted = false;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            var folder = WorkshopDirs.Select(d => Path.Combine(d, workshopId)).FirstOrDefault(Directory.Exists);
            if (folder is null || !Directory.EnumerateFiles(folder, "SubModule.xml", SearchOption.AllDirectories).Any())
            {
                // Klasörü elle silinmiş öğeyi Steam "kurulu" sanır ve indirmez.
                if (!hinted && DateTime.UtcNow - started > TimeSpan.FromSeconds(60) &&
                    WorkshopSubscriptions.RecordedButMissing(WorkshopDirs).Contains(workshopId))
                {
                    hinted = true;
                    ShowToast(L["mm.workshopStuck"], ToastKind.Warning);
                }

                continue;
            }
            // Klasör başka hesaptan kalmış olabilir: bu hesapta abone olunana kadar bekle.
            if (!WorkshopSubscriptions.Load(WorkshopDirs).IsAvailable(workshopId)) continue;

            LoadManagedMods();
            var added = _installedModules.FirstOrDefault(m => m.WorkshopId == workshopId);
            if (added is not null && ManagedMods.FirstOrDefault(m => m.Id == added.Id) is { } row) row.IsSelected = true;
            if (card is not null) { card.Waiting = false; card.Installed = true; }
            ShowToast(string.Format(L["mm.workshopReady"], added?.Name ?? workshopId), ToastKind.Success);
            if (SelectedPack is not null) SelectPack(SelectedPack);
            return;
        }

        if (card is not null) card.Waiting = false;
    }
}
