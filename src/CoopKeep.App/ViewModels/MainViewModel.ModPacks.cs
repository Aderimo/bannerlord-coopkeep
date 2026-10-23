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

/// <summary>Paketler sekmesi: oluşturma, içe aktarma, uygulama, gönderme.</summary>
public partial class MainViewModel
{
    // =====================================================================
    // Paketler
    // =====================================================================

    public ObservableCollection<PackRow> Packs { get; } = [];
    public ObservableCollection<PackItemRow> PackItems { get; } = [];

    [ObservableProperty] private PackRow? _selectedPack;
    [ObservableProperty] private string _newPackName = "";
    [ObservableProperty] private bool _packIncludeWorkshop;

    public bool HasPacks => Packs.Count > 0;
    public bool HasSelectedPack => SelectedPack is not null;
    public string PackStatusText => SelectedPack is null
        ? ""
        : string.Format(L["mm.pack.status"], PackItems.Count(i => i.Ready), PackItems.Count);

    partial void OnSelectedPackChanged(PackRow? value)
    {
        if (value is not null) SelectPack(value);
        else PackItems.Clear();
        OnPropertyChanged(nameof(HasSelectedPack));
        OnPropertyChanged(nameof(SelectedInvite));
        OnPropertyChanged(nameof(HasSelectedInvite));
        OnPropertyChanged(nameof(SelectedInviteText));
        OnPropertyChanged(nameof(PackStatusText));
    }

    private void LoadPacks()
    {
        if (_installedModules.Count == 0) LoadManagedMods();
        Packs.Clear();
        if (Directory.Exists(PacksDir))
        {
            foreach (var file in Directory.GetFiles(PacksDir, "*" + ModPackFile.ListExtension).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var files = Path.ChangeExtension(file, null) + "-files";
                    Packs.Add(new PackRow(file, ModPackFile.LoadList(file), Directory.Exists(files) ? files : null,
                        string.Equals(file, _ui.LastAppliedPack, StringComparison.OrdinalIgnoreCase)));
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { }
            }
        }

        OnPropertyChanged(nameof(HasPacks));
    }

    private void SelectPack(PackRow pack)
    {
        PackItems.Clear();
        foreach (var s in ModPackFile.Compare(pack.Pack, GameModules))
            PackItems.Add(new PackItemRow(s, PackStateText(s)));

        // Paketteki bir modun ihtiyaç duyduğu ama pakette de bilgisayarda da olmayan mod
        // (ör. Better Time → MCM): "hazır" sanılmasın, abone olunabilsin.
        foreach (var dep in MissingDependenciesOf(pack.Pack.Mods.Select(m => m.Id)))
        {
            var entry = DependencyEntry(dep);
            var state = entry.WorkshopId is { Length: > 0 } ? ModPackItemState.MissingWorkshop : ModPackItemState.MissingManual;
            PackItems.Add(new PackItemRow(new ModPackItemStatus(entry, state, null),
                _unsubscribed.Contains(dep) ? L["mm.state.notSubscribed"] : L["mm.state.depMissing"]));
        }

        OnPropertyChanged(nameof(PackStatusText));
    }

    private static string SafeFileName(string name)
    {
        var clean = string.Concat(name.Trim().Split(Path.GetInvalidFileNameChars()));
        return clean.Length == 0 ? "pack" : clean;
    }

    /// <summary>
    /// Seçili dünyanın modlarından paket oluşturur ve Paketler sekmesini açar: arkadaşlar o
    /// paketi uygulayınca sunucuyla birebir aynı modlarla girer (Coop farkı reddediyor).
    /// </summary>
    [RelayCommand]
    private void CreateWorldPack()
    {
        if (SelectedCampaign is not { } world) return;
        SaveWorldEditorFor(world.Name);

        var mods = WorldModIds(world.Name).ToList();
        if (mods.Count == 0)
        {
            ShowToast(L["worlds.pack.empty"], ToastKind.Info);
            return;
        }

        LoadManagedMods();
        var name = string.Format(L["worlds.pack.name"], world.Name);
        var pack = ModPackFile.Create(name, mods, _installedModules, _game?.GameVersion);
        Directory.CreateDirectory(PacksDir);
        var path = Path.Combine(PacksDir, SafeFileName(name) + ModPackFile.ListExtension);
        ModPackFile.SaveList(pack, path);

        CurrentPage = AppPage.Mods;
        ModsTab = ModsTab.Packs;
        LoadPacks();
        SelectedPack = Packs.FirstOrDefault(p => p.Path == path);
        ShowToast(string.Format(L["worlds.pack.created"], name), ToastKind.Success);
    }

    /// <summary>Şu an açık modlardan, şu anki sırayla paket oluşturur.</summary>
    [RelayCommand]
    private void CreatePackFromCurrent()
    {
        var name = NewPackName.Trim();
        if (name.Length == 0) name = string.Format(L["mm.pack.defaultName"], DateTime.Now);

        var pack = ModPackFile.Create(name, WithDependencies(ManagedMods.Where(m => m.IsSelected).Select(m => m.Id)), _installedModules, _game?.GameVersion);
        Directory.CreateDirectory(PacksDir);
        var path = Path.Combine(PacksDir, SafeFileName(name) + ModPackFile.ListExtension);
        ModPackFile.SaveList(pack, path);

        NewPackName = "";
        LoadPacks();
        SelectedPack = Packs.FirstOrDefault(p => p.Path == path);
        ShowToast(string.Format(L["mm.pack.created"], name, pack.Mods.Count), ToastKind.Success);
    }

    /// <summary>
    /// Paketi uygular: pakette gelen modları kurar, başlatıcıyı paketin listesine ve sırasına
    /// göre ayarlar. Atölye'den eksikler için abone olma sayfaları açılabilir.
    /// </summary>
    [RelayCommand]
    private void ApplyPack()
    {
        if (SelectedPack is not { } pack || GameModulesDir is not { } modules) return;
        if (ModuleCompatibility.IsGameRunning(_supervisor.ProcessId))
        {
            ShowToast(L["coop.gameRunning"], ToastKind.Warning);
            return;
        }

        try
        {
            var installedFromPack = 0;
            var skippedFromPack = new List<string>();
            var bundledMissing = ModPackFile.Compare(pack.Pack, GameModules)
                .Where(s => s.State is ModPackItemState.MissingBundled or ModPackItemState.WrongVersion && s.Entry.Bundled).ToList();
            if (bundledMissing.Count > 0 && pack.FilesFolder is { } files)
            {
                foreach (var s in bundledMissing)
                {
                    var folder = Path.Combine(files, s.Entry.Id);
                    if (Directory.Exists(folder)) installedFromPack += ModArchiveInstaller.InstallFolder(folder, modules, RemovedModsDir, WorkshopModuleVersions(), skippedFromPack).Count;
                }
            }

            ReportSkipped(skippedFromPack);
            if (installedFromPack > 0) RescanModules();

            // Paketin modları ve onların gerektirdikleri (paket eksik oluşturulmuş olsa bile).
            var ids = WithDependencies(pack.Pack.Mods.Select(m => m.Id));
            var full = pack.Pack with { Mods = ids.Select(id => pack.Pack.Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                                                                 ?? DependencyEntry(id)).ToList() };
            WriteLauncherSorted(ModPackFile.LauncherList(full, LauncherProfile.Read()));
            _ui.LastAppliedPack = pack.Path;
            _ui.Save();
            LoadManagedMods();
            CheckModules();
            LoadPacks();
            SelectedPack = Packs.FirstOrDefault(p => p.Path == pack.Path);
            if (SelectedPack is { } again) SelectPack(again);
            OpenPackWorldPrompt();

            var missing = PackItems.Count(i => i.NotReady);
            pack = SelectedPack ?? pack;
            ShowToast(missing == 0
                    ? string.Format(L["mm.pack.applied"], pack.Name)
                    : string.Format(L["mm.pack.appliedMissing"], pack.Name, missing),
                missing == 0 ? ToastKind.Success : ToastKind.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>
    /// Başlatıcıya yazar; açık modlar modların kendi sıralama isteklerine göre dizilir
    /// (Harmony/ButterLib gibi kütüphaneler resmî modüllerden önce, Coop en sonda).
    /// </summary>
    private void WriteLauncherSorted(IReadOnlyList<LauncherMod> list)
    {
        var on = list.Where(m => m.IsSelected).Select(m => m.Id).ToList();
        var sorted = LoadOrder.Sort(on, _installedModules, out _);
        LauncherProfile.Write(sorted.Select(id => new LauncherMod(id, true)).Concat(list.Where(m => !m.IsSelected)).ToList());
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    /// <summary>Atölye'den kurulu modlar ve sürümleri (aynı sürümün kopyası Modules'e kurulmasın).</summary>
    private IReadOnlyDictionary<string, string?> WorkshopModuleVersions() =>
        _installedModules.Where(m => m.Source == ModuleSource.Workshop)
            .ToDictionary(m => m.Id, m => m.Version, StringComparer.OrdinalIgnoreCase);

    /// <summary>Aynı modun kopyalarından birinin Atölye sayfası (aboneliğinden çıkmak için).</summary>
    [RelayCommand]
    private void OpenWorkshopCopy(DuplicateCopy? copy)
    {
        if (copy is not null) OpenPath("steam://url/CommunityFilePage/" + copy.WorkshopId);
    }

    /// <summary>
    /// Paketteki satırın durumu. Sürüm farklıysa ve bu bilgisayardaki kopya başka bir Atölye
    /// sayfasından geliyorsa bu söylenir: aynı mod Atölye'de birden çok sayfada olabiliyor.
    /// </summary>
    private string PackStateText(ModPackItemStatus s)
    {
        if (_unsubscribed.Contains(s.Entry.Id)) return L["mm.state.notSubscribed"];
        if (s.State == ModPackItemState.WrongVersion && s.Entry.WorkshopId is { Length: > 0 } wanted &&
            _installedModules.FirstOrDefault(m => m.Id.Equals(s.Entry.Id, StringComparison.OrdinalIgnoreCase)) is { } mine &&
            mine.WorkshopId != wanted)
            return string.Format(L["mm.state.otherItem"], mine.Version ?? "?");
        return L["mm.state." + s.State];
    }

    private void ReportSkipped(IReadOnlyCollection<string> skipped)
    {
        if (skipped.Count > 0)
            ShowToast(string.Format(L["mm.install.skipped"], string.Join(", ", skipped.Distinct(StringComparer.OrdinalIgnoreCase))), ToastKind.Warning);
    }

    /// <summary>Pakette yazmayan ama gereken mod için liste girdisi.</summary>
    private ModPackEntry DependencyEntry(string id)
    {
        var module = _installedModules.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var workshopId = module?.WorkshopId ?? KnownMods.WorkshopId(id) ?? WorkshopIdFor(id);
        return new ModPackEntry(id, module?.Name ?? KnownMods.Name(id) ?? NameFor(id), module?.Version,
            module?.Source == ModuleSource.Workshop || workshopId is not null ? ModPackSource.Workshop : ModPackSource.Local, workshopId);
    }

    /// <summary>Kimlikler ve onların gerektirdikleri (oyunun gördüğü modlarla), bağımlılık önce.</summary>
    private List<string> WithDependencies(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        var comparer = StringComparer.OrdinalIgnoreCase;
        var fixedIds = list.Where(id => LoadOrder.Official.Contains(id, comparer) || LoadOrder.LoadLast.Contains(id, comparer)
                                        || ModuleCompatibility.ServerModules.Contains(id)).ToList();
        var plan = ModPlanner.Plan(list.Except(fixedIds, comparer).Select(id => new WorldMod(id)).ToList(), GameModules, _coop?.ModuleId ?? "Coop");
        var result = new List<string>(fixedIds);
        // Bilgisayarda olmayan bağımlılıklar da girer (önce onlar): eskiden sessizce atlanıyordu,
        // oyun ör. MCM olmadan açılıp Better Time "dependency conflict" veriyor, ardından çöküyordu.
        foreach (var id in plan.MissingDependencies.Select(d => d.Dependency).Concat(plan.Mods.Select(m => m.Module.Id)).Concat(list))
            if (!result.Contains(id, comparer)) result.Add(id);
        return result;
    }

    /// <summary>Kimliklerin gerektirdiği ama bu bilgisayarda (oyunun görebileceği şekilde) olmayan modlar.</summary>
    private List<string> MissingDependenciesOf(IEnumerable<string> ids)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var list = ids.Where(id => !LoadOrder.Official.Contains(id, comparer) && !LoadOrder.LoadLast.Contains(id, comparer)
                                   && !ModuleCompatibility.ServerModules.Contains(id)).ToList();
        var plan = ModPlanner.Plan(list.Select(id => new WorldMod(id)).ToList(), GameModules, _coop?.ModuleId ?? "Coop");
        return plan.MissingDependencies.Select(d => d.Dependency).Distinct(comparer)
            .Where(id => !list.Contains(id, comparer)).ToList();
    }

    /// <summary>
    /// Ana sayfa: oyunun başlatıcısını seçili (ya da açık) dünyanın modlarına eşitler. Oyunu
    /// Steam'den ya da başlatıcıdan açanlar da sunucuyla aynı modlarla girer.
    /// </summary>
    [RelayCommand]
    private void MatchLauncherToWorld()
    {
        var world = IsRunning ? ActiveSaveName : SelectedCampaign?.Name;
        if (string.IsNullOrEmpty(world)) return;
        if (ModuleCompatibility.IsGameRunning(_supervisor.ProcessId))
        {
            ShowToast(L["coop.gameRunning"], ToastKind.Warning);
            return;
        }

        try
        {
            var ids = WorldModIds(world);
            var pack = new ModPack { Name = world, Mods = ids.Select(DependencyEntry).ToList() };
            WriteLauncherSorted(ModPackFile.LauncherList(pack, LauncherProfile.Read()));
            LoadManagedMods();
            CheckModules();
            ShowToast(string.Format(L["home.mods.matched"], world), ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>
    /// Oyunu seçili paketin modlarıyla (gerektirdikleri dahil, doğru sırayla) ve Coop ile açar.
    /// Başlatıcıda tek tek işaretlemek gerekmez; oyun doğrudan bu listeyle başlar.
    /// </summary>
    [RelayCommand]
    private void PlayWithPack()
    {
        if (SelectedPack is not { } pack) return;
        if (ModuleCompatibility.IsGameRunning(_supervisor.ProcessId))
        {
            ShowToast(L["coop.gameRunning"], ToastKind.Warning);
            return;
        }

        RescanModules();
        var comparer = StringComparer.OrdinalIgnoreCase;
        var mods = WithDependencies(pack.Pack.Mods.Select(m => m.Id))
            .Where(id => !LoadOrder.Official.Contains(id, comparer) && !LoadOrder.LoadLast.Contains(id, comparer)
                         && !ModuleCompatibility.ServerModules.Contains(id) && ModuleCompatibility.Classify(id) != ModuleStatus.Blocked)
            .ToList();

        // Oyunun göremediği mod varsa oyun "modül bulunamadı" deyip kapanır: önce söyle.
        var visible = GameModules.Select(m => m.Id).ToHashSet(comparer);
        var missing = mods.Where(id => !visible.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            ShowToast(string.Format(L["mm.pack.playMissing"], DescribeMissing(missing)), ToastKind.Warning);
            return;
        }

        LaunchCoopGame(mods, string.Format(L["mm.pack.playing"], pack.Name, mods.Count));
    }

    // --- paketi sunucu dünyalarına kurma ---------------------------------

    /// <summary>"Bu paketi hangi sunucu dünyalarına kuralım?" sorusu açık mı?</summary>
    [ObservableProperty] private bool _packWorldPromptOpen;

    public ObservableCollection<WorldChoice> PackWorldChoices { get; } = [];

    public bool HasPackWorldChoices => PackWorldChoices.Count > 0;

    [RelayCommand]
    private void OpenPackWorldPrompt()
    {
        PackWorldChoices.Clear();
        foreach (var w in Campaigns)
            PackWorldChoices.Add(new WorldChoice(w.Name, Campaigns.Count == 1 || w.Name == (ModsWorld ?? SelectedCampaign)?.Name));
        OnPropertyChanged(nameof(HasPackWorldChoices));
        PackWorldPromptOpen = SelectedPack is not null;
    }

    [RelayCommand]
    private void ClosePackWorldPrompt() => PackWorldPromptOpen = false;

    /// <summary>
    /// Seçili paketi işaretli dünyaların sunucusuna kurar: dünyanın mod listesi paketin modları
    /// (ve gerektirdikleri) olur. Sunucu bir sonraki açılışta bu modlarla açılır.
    /// </summary>
    [RelayCommand]
    private void InstallPackToWorlds()
    {
        if (SelectedPack is not { } pack) return;
        var comparer = StringComparer.OrdinalIgnoreCase;
        var mods = WithDependencies(pack.Pack.Mods.Select(m => m.Id))
            .Where(id => !LoadOrder.Official.Contains(id, comparer) && !LoadOrder.LoadLast.Contains(id, comparer)
                         && !ModuleCompatibility.ServerModules.Contains(id) && ModuleCompatibility.Classify(id) != ModuleStatus.Blocked)
            .Select(id => new WorldMod(id))
            .ToList();

        var done = new List<string>();
        foreach (var choice in PackWorldChoices.Where(c => c.IsChecked))
        {
            if (IsRunning && string.Equals(choice.Name, ActiveSaveName, StringComparison.OrdinalIgnoreCase))
            {
                ShowToast(L["mm.world.running"], ToastKind.Warning);
                continue;
            }

            try
            {
                _profiles.SaveMods(choice.Name, mods);
                done.Add(choice.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowToast(ex.Message, ToastKind.Error);
            }
        }

        PackWorldPromptOpen = false;
        if (done.Count == 0) return;

        MarkWorldMods();
        if (SelectedCampaign is { } selected && done.Contains(selected.Name) && !WorldEditor.IsDirty) LoadWorldEditor();
        CheckModules();

        // Sunucu modları bu bilgisayardan kurar: burada olmayan mod varsa sunucu açılmaz.
        var visible = GameModules.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notHere = mods.Select(m => m.Id).Where(id => !visible.Contains(id)).ToList();
        if (notHere.Count > 0)
            ShowToast(string.Format(L["mm.pack.installedMissing"], pack.Name, string.Join(", ", done), DescribeMissing(notHere)), ToastKind.Warning);
        else
            ShowToast(string.Format(L["mm.pack.installedToWorlds"], pack.Name, string.Join(", ", done)), ToastKind.Success);
    }

    /// <summary>
    /// Modun Steam Atölyesi kimliği: diskteki kopyasından (başka hesabın indirdiği dahil) ya da
    /// kayıtlı paketlerden. Bilinmiyorsa <c>null</c>.
    /// </summary>
    private string? WorkshopIdFor(string modId)
    {
        if (KnownMods.WorkshopId(modId) is { } known) return known;
        if (_installedModules.FirstOrDefault(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))?.WorkshopId is { Length: > 0 } id)
            return id;

        if (!Directory.Exists(PacksDir)) return null;
        foreach (var file in Directory.GetFiles(PacksDir, "*" + ModPackFile.ListExtension))
        {
            try
            {
                if (ModPackFile.LoadList(file).Mods.FirstOrDefault(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))?.WorkshopId is { Length: > 0 } fromPack)
                    return fromPack;
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException) { }
        }

        return null;
    }

    /// <summary>
    /// Eksik modları getirir: Atölye kimliği bilinenler için Steam'in abone olma sayfası açılır
    /// (inince liste kendiliğinden yenilenir); bilinmeyenler Mod bul'da aranır.
    /// </summary>
    private void GetMissingMods(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        if (ManagedMods.Count == 0) LoadManagedMods();

        var workshop = ids.Select(id => (Id: id, Workshop: WorkshopIdFor(id))).ToList();
        foreach (var (_, ws) in workshop.Where(w => w.Workshop is not null)) SubscribeWorkshop(ws!);

        var unknown = workshop.Where(w => w.Workshop is null).Select(w => w.Id).ToList();
        CurrentPage = AppPage.Mods;
        if (unknown.Count > 0)
        {
            ModsTab = ModsTab.Browse;
            BrowseSite = ModSite.Workshop;
            BrowseQuery = unknown[0];
            if (SearchModsCommand.CanExecute(null)) SearchModsCommand.Execute(null);
        }

        var known = workshop.Where(w => w.Workshop is not null).Select(w => w.Id).ToList();
        ShowToast(unknown.Count == 0
                ? string.Format(L["mm.getMissing.subscribing"], string.Join(", ", known))
                : string.Format(L["mm.getMissing.search"], string.Join(", ", unknown)),
            ToastKind.Info);
    }

    // --- elle kurulum ----------------------------------------------------

    /// <summary>Oyunun mod klasörü (elle kurulum).</summary>
    public string ManualModsFolderText => GameModulesDir ?? "-";

    /// <summary>Steam Atölyesi'nin indirdiği klasör.</summary>
    public string WorkshopFolderText => WorkshopDirs.FirstOrDefault(Directory.Exists) ?? "-";

    [RelayCommand]
    private void OpenWorkshopFolder()
    {
        if (WorkshopDirs.FirstOrDefault(Directory.Exists) is { } dir) OpenPath(dir);
    }

    [RelayCommand]
    private void SubscribeManagedMod(ManagedMod? mod)
    {
        if (mod?.Module?.WorkshopId is { Length: > 0 } id) SubscribeWorkshop(id);
    }

    [RelayCommand]
    private void SubscribePackItem(PackItemRow? item)
    {
        if (item?.Status.Entry.WorkshopId is { Length: > 0 } id) SubscribeWorkshop(id);
    }

    [RelayCommand]
    private void OpenPackItemPage(PackItemRow? item)
    {
        if (item?.Status.Entry.NexusId is { Length: > 0 } id) OpenPath("https://www.nexusmods.com/mountandblade2bannerlord/mods/" + id + "?tab=files");
    }

    /// <summary>Paketteki eksik Atölye modlarının hepsine abone olma sayfalarını açar.</summary>
    [RelayCommand]
    private void SubscribeAllMissing()
    {
        foreach (var item in PackItems.Where(i => i.CanSubscribe)) SubscribePackItem(item);
    }

    /// <summary>
    /// Tek dosyalık davet: dünyanın modları (gerektirdikleriyle) ve katılma bilgileri bir zip'te.
    /// Elle kurulmuş modlar dosyaya girer; Atölye modları için abone olma bağlantıları yazılır.
    /// </summary>
    [RelayCommand]
    private async Task CreateInviteFileAsync()
    {
        var world = IsRunning ? ActiveSaveName : SelectedCampaign?.Name;
        if (string.IsNullOrEmpty(world))
        {
            ShowToast(L["invite.file.noWorld"], ToastKind.Info);
            return;
        }

        var path = await PickSaveFileAsync(L["invite.file.title"], SafeFileName(string.Format(L["invite.file.name"], world)) + ".zip", "*.zip");
        if (path is null) return;

        try
        {
            if (ManagedMods.Count == 0) LoadManagedMods();
            var ids = WorldModIds(world).ToList();
            var cfg = _config?.Read();
            var invite = new ModPackInvite(
                SteamHostName.Length > 0 ? SteamHostName : null,
                HasPublicAddress ? PublicAddress : $"{LocalIp}:{(cfg?.Port ?? 4200).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                InviteIncludePassword && cfg?.Password is { Length: > 0 } pw ? pw : null,
                world,
                _coop?.ModVersion);
            var pack = ModPackFile.Create(string.Format(L["invite.file.name"], world), ids, _installedModules, _game?.GameVersion) with { Invite = invite };
            ModPackFile.ExportZip(pack, _installedModules, path, PackIncludeWorkshop);
            ShowToast(string.Format(L["invite.file.created"], Path.GetFileName(path)), ToastKind.Success);
            OpenPath(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Davet dosyasına şifre de yazılsın mı (varsayılan evet: arkadaş ayrıca sormasın).</summary>
    [ObservableProperty] private bool _inviteIncludePassword = true;

    /// <summary>Seçili paket bir davetse sunucunun bilgisi.</summary>
    public ModPackInvite? SelectedInvite => SelectedPack?.Pack.Invite;

    public bool HasSelectedInvite => SelectedInvite is not null;

    public string SelectedInviteText => SelectedInvite is not { } i ? "" : string.Join("  ·  ", new[]
    {
        i.World is { Length: > 0 } w ? string.Format(L["invite.pack.world"], w) : null,
        i.HostSteamName is { Length: > 0 } h ? string.Format(L["invite.pack.steam"], h) : null,
        i.Address is { Length: > 0 } a ? string.Format(L["invite.pack.address"], a) : null,
        i.CoopVersion is { Length: > 0 } v ? string.Format(L["invite.pack.coop"], v) : null,
    }.Where(x => x is not null));

    /// <summary>Davet: paketi uygular, adresi ve şifreyi hazırlar, oyunu Coop ile açar.</summary>
    [RelayCommand]
    private async Task ApplyInviteAndJoinAsync()
    {
        if (SelectedInvite is not { } invite) return;
        ApplyPack();
        PackWorldPromptOpen = false; // davet alan kişi sunucu açmıyor
        if (PackItems.Any(i => i.NotReady))
        {
            ShowToast(L["invite.pack.notReady"], ToastKind.Warning);
            return;
        }

        if (invite.CoopVersion is { Length: > 0 } v && _coop?.ModVersion is { Length: > 0 } mine && !string.Equals(v, mine, StringComparison.OrdinalIgnoreCase))
            ShowToast(string.Format(L["invite.pack.coopMismatch"], mine, v), ToastKind.Warning);

        if (invite.Address is { Length: > 0 } address) CoopJoinAddress = address;
        if (invite.Password is { Length: > 0 } password) await CopyToClipboardAsync(password);
        await JoinCoopAsync();
    }

    /// <summary>Paketi arkadaşlara gönderilecek zip olarak kaydeder.</summary>
    [RelayCommand]
    private async Task ExportPackZipAsync()
    {
        if (SelectedPack is not { } pack) return;
        var path = await PickSaveFileAsync(L["mm.pack.exportTitle"], SafeFileName(pack.Name) + ".zip", "*.zip");
        if (path is null) return;

        try
        {
            // Eski paketlerde gereken modlar eksik olabilir (ör. Better Time için MCM): gönderirken eklenir.
            var full = pack.Pack with
            {
                Mods = WithDependencies(pack.Pack.Mods.Select(m => m.Id))
                    .Select(id => pack.Pack.Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? DependencyEntry(id))
                    .ToList(),
            };
            ModPackFile.ExportZip(full, _installedModules, path, PackIncludeWorkshop);
            ShowToast(string.Format(L["mm.pack.exported"], Path.GetFileName(path)), ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Arkadaştan gelen paketi (zip ya da .ckpack) içe aktarır.</summary>
    [RelayCommand]
    private async Task ImportPackAsync()
    {
        var path = await PickOpenFileAsync(L["mm.pack.importTitle"], ["*.zip", "*" + ModPackFile.ListExtension]);
        if (path is null) return;

        try
        {
            var (pack, modulesFolder) = ModPackFile.Open(path);
            Directory.CreateDirectory(PacksDir);
            var target = Path.Combine(PacksDir, SafeFileName(pack.Name) + ModPackFile.ListExtension);
            ModPackFile.SaveList(pack, target);

            var files = Path.ChangeExtension(target, null) + "-files";
            if (Directory.Exists(files)) Directory.Delete(files, recursive: true);
            if (modulesFolder is not null)
            {
                // Farklı sürücüler arasında Move çalışmaz: önce taşımayı dene, olmazsa kopyala.
                try { Directory.Move(modulesFolder, files); }
                catch (IOException) { CopyFolder(modulesFolder, files); }
                ModPackFile.DeleteQuietly(ModPackFile.TempRootOf(modulesFolder));
            }

            LoadPacks();
            SelectedPack = Packs.FirstOrDefault(p => p.Path == target);
            ShowToast(string.Format(L[pack.Invite is null ? "mm.pack.imported" : "invite.pack.imported"], pack.Name), ToastKind.Success);
            if (pack.Mods.Any(m => m.Bundled))
                ShowToast(string.Format(L["mm.pack.bundledWarning"], pack.Mods.Count(m => m.Bundled)), ToastKind.Warning);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            ShowToast(string.Format(L["mm.installFailed"], ex.Message), ToastKind.Error);
        }
    }

    [RelayCommand]
    private void DeletePack()
    {
        if (SelectedPack is not { } pack) return;
        try
        {
            File.Delete(pack.Path);
            if (pack.FilesFolder is { } files && Directory.Exists(files)) Directory.Delete(files, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast(ex.Message, ToastKind.Error); }

        SelectedPack = null;
        LoadPacks();
    }

    /// <summary>Ayarlar'daki Nexus anahtarı (yalnızca bu bilgisayarda saklanır).</summary>
    public string NexusApiKey
    {
        get => _ui.NexusApiKey ?? "";
        set
        {
            var clean = value.Trim();
            if (clean == (_ui.NexusApiKey ?? "")) return;
            _ui.NexusApiKey = clean.Length == 0 ? null : clean;
            _ui.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Anahtarı denetler: geçerli mi, Premium mu?</summary>
    [RelayCommand]
    private async Task CheckNexusKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(_ui.NexusApiKey)) return;
        try
        {
            var premium = await NexusSearch.IsPremiumAsync(ModsHttp.Value, _ui.NexusApiKey!);
            ShowToast(L[premium switch { null => "mm.nexusKeyInvalid", true => "mm.nexusKeyPremium", false => "mm.nexusKeyFree" }],
                premium is null ? ToastKind.Error : premium == true ? ToastKind.Success : ToastKind.Info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }
}
