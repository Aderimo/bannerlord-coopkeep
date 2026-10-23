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

public enum ModsTab { Installed, Browse, Packs }

public enum ModFilter { All, On, Off, Problems }

/// <summary>Yüklü modlar listesinde bir satır.</summary>
public sealed partial class ManagedMod(InstalledModule? module, string id, bool selected, Action changed) : ObservableObject
{
    /// <summary>Kapatılamayan modüller: oyunun temeli. Coop ayrı (açılabilir, kapatılamaz).</summary>
    private static readonly HashSet<string> Required = new(StringComparer.OrdinalIgnoreCase) { "Native", "SandBoxCore", "Sandbox" };

    public InstalledModule? Module { get; } = module;
    public string Id { get; } = id;
    public string Name => Module?.Name is { Length: > 0 } n && n != Id ? n : Id;
    public string Version => Module?.Version ?? "";
    public bool IsInstalled => Module is not null;
    public bool IsOfficial => Module?.IsOfficial == true || LoadOrder.Official.Contains(Id, StringComparer.OrdinalIgnoreCase);
    public bool IsCoop => Id.Equals("Coop", StringComparison.OrdinalIgnoreCase) || Id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase);
    public bool IsWorkshop => Module?.Source == ModuleSource.Workshop;
    public bool IsLocal => Module is { Source: ModuleSource.GameFolder, IsOfficial: false };
    public bool IsBlocked => ModuleCompatibility.Classify(Id) == ModuleStatus.Blocked;
    public bool IsLocked => Required.Contains(Id);

    /// <summary>Kullanıcının modu (oyunun modülü, Coop ya da Coop'un reddettiği değil).</summary>
    public bool IsUserMod => !IsOfficial && !IsCoop && Role != CoopModRole.Incompatible;

    /// <summary>
    /// Açılıp kapatılabilir mi? Coop'un reddettikleri yalnızca kapatılabilir; Coop yalnızca
    /// açılabilir; oyunun temeli kilitli.
    /// </summary>
    public bool CanToggle => IsInstalled && !IsLocked &&
                             (Role != CoopModRole.Incompatible || IsSelected) &&
                             (!IsCoop || !IsSelected);

    /// <summary>Yalnızca kullanıcının modları sıralanır; oyunun modülleri ve Coop sabit.</summary>
    public bool CanMove => IsUserMod;

    public bool CanRemove => IsLocal && !Id.Equals(LoadOrder.CoopKeepId, StringComparison.OrdinalIgnoreCase);

    [ObservableProperty] private bool _isSelected = selected;

    /// <summary>Eksik bağımlılık ya da çakışma; yoksa boş.</summary>
    [ObservableProperty] private string _problem = "";

    /// <summary>Coop'taki rolü (etiket).</summary>
    [ObservableProperty] private CoopModRole _role = CoopModRole.Code;
    [ObservableProperty] private string _roleLabel = "";
    [ObservableProperty] private string _roleTip = "";

    /// <summary>Topluluk "Coop ile çalışıyor" dedi mi?</summary>
    [ObservableProperty] private bool _communityWorks;

    /// <summary>Oyunun başka bir sürümü için yapılmışsa uyarı; yoksa boş.</summary>
    [ObservableProperty] private string _versionWarning = "";

    /// <summary>Atölye'deki kısa açıklaması (varsa).</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>Modlar sayfasında seçili sunucu dünyasının mod listesinde mi?</summary>
    [ObservableProperty] private bool _inWorld;

    /// <summary>
    /// Atölye modu diskte ama Steam'de açık hesap abone değil: oyun onu görmez (bilgisayarda
    /// başka bir Steam hesabının indirdiği mod).
    /// </summary>
    [ObservableProperty] private bool _notSubscribed;

    public bool NotInWorld => !InWorld;

    partial void OnInWorldChanged(bool value) => OnPropertyChanged(nameof(NotInWorld));

    public bool HasProblem => Problem.Length > 0 || NotSubscribed;
    public bool HasVersionWarning => VersionWarning.Length > 0;
    public bool HasDescription => Description.Length > 0;
    public bool RoleIsIncompatible => Role == CoopModRole.Incompatible;
    public bool RoleIsNormal => !RoleIsIncompatible && RoleLabel.Length > 0;
    public double RowOpacity => IsSelected ? 1.0 : 0.78;

    /// <summary>Kullanıcı modu açınca: gerektirdiği modlar da açılsın diye.</summary>
    public Action<ManagedMod>? TurnedOn { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowOpacity));
        OnPropertyChanged(nameof(CanToggle));
        if (value) TurnedOn?.Invoke(this);
        changed();
    }

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));
    partial void OnNotSubscribedChanged(bool value) => OnPropertyChanged(nameof(HasProblem));
    partial void OnVersionWarningChanged(string value) => OnPropertyChanged(nameof(HasVersionWarning));
    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(HasDescription));

    partial void OnRoleChanged(CoopModRole value)
    {
        OnPropertyChanged(nameof(IsUserMod));
        OnPropertyChanged(nameof(CanMove));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(RoleIsIncompatible));
        OnPropertyChanged(nameof(RoleIsNormal));
    }

    partial void OnRoleLabelChanged(string value) => OnPropertyChanged(nameof(RoleIsNormal));
}

/// <summary>Mod bul sekmesinde bir sonuç kartı.</summary>
public sealed partial class BrowseCard(ModSearchResult result) : ObservableObject
{
    public ModSearchResult Result { get; } = result;
    public string Title => Result.Title;
    public string Summary => Result.Summary ?? "";
    public string Meta { get; init; } = "";
    public bool IsWorkshop => Result.Site == ModSite.Workshop;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _installed;

    /// <summary>Kurulu ve seçili sunucu dünyasının mod listesinde.</summary>
    [ObservableProperty] private bool _inWorld;

    /// <summary>Abone olundu, Steam'in indirmesi bekleniyor.</summary>
    [ObservableProperty] private bool _waiting;

    public bool HasImage => Image is not null;
    public bool CanGet => !Installed && !Waiting;

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnInstalledChanged(bool value) => OnPropertyChanged(nameof(CanGet));
    partial void OnWaitingChanged(bool value) => OnPropertyChanged(nameof(CanGet));
}

/// <summary>Kayıtlı bir mod paketi.</summary>
public sealed record PackRow(string Path, ModPack Pack, string? FilesFolder, bool IsActive = false)
{
    public string Name => Pack.Name;
    public int Count => Pack.Mods.Count;
}

/// <summary>"Paketi hangi sunucu dünyalarına kuralım?" sorusundaki bir dünya.</summary>
public sealed partial class WorldChoice(string name, bool isChecked) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _isChecked = isChecked;
}

/// <summary>Seçili paketteki bir modun durumu.</summary>
public sealed record PackItemRow(ModPackItemStatus Status, string StateText)
{
    public string Name => Status.Entry.Name;
    public string Version => Status.Entry.Version ?? "";
    public bool Ready => Status.State == ModPackItemState.Ready;
    public bool NotReady => !Ready;
    public bool CanSubscribe => Status.State is ModPackItemState.MissingWorkshop || (Status.State == ModPackItemState.WrongVersion && Status.Entry.WorkshopId is { Length: > 0 });
    public bool CanOpenPage => Status.State == ModPackItemState.MissingManual && Status.Entry.NexusId is { Length: > 0 };
}

/// <summary>
/// Mod yöneticisi: yüklü modlar (aç/kapat, sıra, kur, kaldır), mod bulma (Steam Atölyesi,
/// Nexus) ve mod paketleri.
/// </summary>
/// <remarks>
/// <para>
/// Yüklü modlar ekranı oyunun başlatıcısının listesini düzenler. Değişiklikler "Uygula" ile
/// yazılır; oyun açıkken yazılmaz (başlatıcı kapanırken üstüne yazardı).
/// </para>
/// <para>
/// Atölye modu Steam'den abone olunarak iner: CoopKeep, Steam'de modun sayfasını açar ve
/// klasör belirince modu listeye ekler. Nexus'tan doğrudan indirme kullanıcının kendi Premium
/// anahtarıyla olur; yoksa sayfa açılır ve indirilen zip "Zip'ten kur" ile kurulur.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    // =====================================================================
    // Sekmeler
    // =====================================================================

    [ObservableProperty] private ModsTab _modsTab = ModsTab.Installed;

    public bool TabInstalled { get => ModsTab == ModsTab.Installed; set { if (value) ModsTab = ModsTab.Installed; } }
    public bool TabBrowse { get => ModsTab == ModsTab.Browse; set { if (value) ModsTab = ModsTab.Browse; } }
    public bool TabPacks { get => ModsTab == ModsTab.Packs; set { if (value) ModsTab = ModsTab.Packs; } }

    partial void OnModsTabChanged(ModsTab value)
    {
        OnPropertyChanged(nameof(TabInstalled));
        OnPropertyChanged(nameof(TabBrowse));
        OnPropertyChanged(nameof(TabPacks));
        if (value == ModsTab.Browse && BrowseCards.Count == 0) _ = SearchModsAsync();
        if (value == ModsTab.Packs) LoadPacks();
    }

    private static readonly Lazy<HttpClient> ModsHttp = new(() =>
    {
        var http = CoopKeep.Core.Net.FastHttp.Create(TimeSpan.FromSeconds(20));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CoopKeep/" + AppVersion.TrimStart('v'));
        return http;
    });

    private static string CoopKeepData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoopKeep");
    private static string RemovedModsDir => Path.Combine(CoopKeepData, "removed-mods");
    private static string PacksDir => Path.Combine(CoopKeepData, "packs");
    private static string ModImagesDir => Path.Combine(CoopKeepData, "mod-images");

    private string? GameModulesDir => _game?.ModulesDirectory;

    private static IEnumerable<string> WorkshopDirs =>
        GameLocator.FindSteamLibraries().Select(l => Path.Combine(l, "steamapps", "workshop", "content", WorkshopSearch.AppId));

    private List<InstalledModule> _installedModules = [];

    /// <summary>Diskte olup açık Steam hesabının abone olmadığı Atölye modları (oyun görmez).</summary>
    private HashSet<string> _unsubscribed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Oyunun gerçekten yükleyebileceği modlar. Planlar (gereken modlar, paket durumu, sunucu)
    /// bununla yapılır; yoksa diskte duran ama oyunun görmediği mod "hazır" sanılıyordu.
    /// </summary>
    private List<InstalledModule> GameModules => _installedModules.Where(m => !_unsubscribed.Contains(m.Id)).ToList();

    /// <summary>Modları yeniden tarar (satırları yeniden kurmadan): yeni inen ya da abone olunan modlar görülsün.</summary>
    private void RescanModules()
    {
        _installedModules = ModuleScanner.Scan(GameModulesDir, WorkshopDirs).ToList();
        var subscriptions = WorkshopSubscriptions.Load(WorkshopDirs);
        _unsubscribed = _installedModules
            .Where(m => m.Source == ModuleSource.Workshop && m.WorkshopId is { } w && !subscriptions.IsAvailable(w))
            .Select(m => m.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ManagedMods) row.NotSubscribed = _unsubscribed.Contains(row.Id);
        WorkshopNeedsVerify = WorkshopSubscriptions.RecordedButMissing(WorkshopDirs).Count > 0;
    }

    private string _modFoldersStamp = "";

    /// <summary>Mod klasörlerinin ve Steam abonelik kaydının kısa izi: değişmediyse tarama gerekmez.</summary>
    private string ModFoldersStamp()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            foreach (var dir in WorkshopDirs.Append(GameModulesDir ?? "").Where(Directory.Exists))
            {
                foreach (var sub in Directory.EnumerateDirectories(dir)) sb.Append(sub).Append(Directory.GetLastWriteTimeUtc(sub).Ticks).Append('|');
                var acf = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(dir)!) ?? "", "appworkshop_" + GameLocator.BannerlordAppId + ".acf");
                if (File.Exists(acf)) sb.Append(File.GetLastWriteTimeUtc(acf).Ticks);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return sb.ToString();
    }

    /// <summary>
    /// Steam'in kaydında kurulu görünen ama klasörü olmayan Atölye modları: abone olunsa da
    /// Steam bunları indirmez (Paketler'de açıklama kartı çıkar).
    /// </summary>
    [ObservableProperty] private bool _workshopNeedsVerify;

    /// <summary>Modlar sayfasındaki "Yenile": diski ve Steam aboneliklerini yeniden okur.</summary>
    [RelayCommand]
    private void RefreshMods()
    {
        if (ModsDirty) RescanModules();
        else LoadManagedMods();
        UpdateModProblems();
        _modFoldersStamp = ModFoldersStamp();
        LoadPacks();
        if (SelectedPack is { } pack) { SelectedPack = Packs.FirstOrDefault(p => p.Path == pack.Path); if (SelectedPack is { } again) SelectPack(again); }
        CheckModules();
        ShowToast(L[WorkshopNeedsVerify ? "mm.refreshedVerify" : "mm.refreshed"], WorkshopNeedsVerify ? ToastKind.Warning : ToastKind.Success);
    }

    /// <summary>Steam'e Bannerlord'un dosya bütünlüğünü doğrulatır (eksik Atölye modları yeniden iner).</summary>
    [RelayCommand]
    private void OpenSteamVerify() => OpenPath("steam://validate/" + GameLocator.BannerlordAppId);

    /// <summary>Taze tarama sonrası oyunun gördüğü modlar (sunucu ve "Oyuna gir" için).</summary>
    private List<InstalledModule> FreshGameModules()
    {
        RescanModules();
        return GameModules;
    }

    /// <summary>Eksik mod adları; Steam'de abone olunmamışsa bunu da söyler.</summary>
    private string DescribeMissing(IEnumerable<string> ids) => string.Join(", ", ids.Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(id => _unsubscribed.Contains(id) ? string.Format(L["mm.missing.notSubscribed"], NameFor(id)) : NameFor(id)));

    /// <summary>Kullanıcının tanıyacağı ad: diskteki kopyadan ya da kayıtlı paketlerden; yoksa kimlik.</summary>
    private string NameFor(string id)
    {
        // Ana sayfa Modlar sayfasından önce açılır: liste henüz taranmamış olabilir.
        if (_installedModules.Count == 0 && _game is not null) RescanModules();
        if (_installedModules.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name is { Length: > 0 } name && name != id)
            return name;
        if (Packs.SelectMany(p => p.Pack.Mods).FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name is { Length: > 0 } packName && packName != id)
            return packName;
        return id;
    }

    /// <summary>
    /// Pencereye dönülünce (Steam'den abone olup gelince) modlar tazelenir. Kaydedilmemiş
    /// değişiklik varsa satırlar bozulmasın diye yalnızca tarama yapılır.
    /// </summary>
    public void OnWindowActivated()
    {
        if (_game is null || ManagedMods.Count == 0) return;
        var stamp = ModFoldersStamp();
        if (stamp == _modFoldersStamp) return;
        _modFoldersStamp = stamp;
        if (ModsDirty) RescanModules();
        else LoadManagedMods();
        UpdateModProblems();
        if (SelectedPack is not null) SelectPack(SelectedPack);
    }

    // =====================================================================
    // Yüklü modlar
    // =====================================================================

    /// <summary>Başlatıcının bütün listesi, yazılacak sırayla.</summary>
    public ObservableCollection<ManagedMod> ManagedMods { get; } = [];

    /// <summary>Kullanıcının modları (arama ve süzgeçle).</summary>
    public ObservableCollection<ManagedMod> VisibleManagedMods { get; } = [];

    /// <summary>Oyunun modülleri (kapalı başlık altında).</summary>
    public ObservableCollection<ManagedMod> OfficialMods { get; } = [];

    /// <summary>Coop'un reddettikleri ve Coop'la çalışmadığı bildirilenler.</summary>
    public ObservableCollection<ManagedMod> IncompatibleMods { get; } = [];

    /// <summary>Coop'un kendisi (sayfanın üstünde, sabit).</summary>
    [ObservableProperty] private ManagedMod? _coopRow;

    [ObservableProperty] private string _managedModSearch = "";
    [ObservableProperty] private ModFilter _modFilter = ModFilter.All;
    [ObservableProperty] private bool _modsDirty;
    [ObservableProperty] private string _modChangesText = "";
    [ObservableProperty] private ManagedMod? _pendingRemoveMod;

    /// <summary>Uygulandığı (ya da okunduğu) andaki liste: değişiklik buna göre hesaplanır.</summary>
    private List<(string Id, bool On)> _savedMods = [];

    /// <summary>Kompakt görünüm: satırda yalnızca ad, etiketler, aç/kapat ve sunucu düğmesi.</summary>
    public bool CompactMods
    {
        get => _ui.CompactMods;
        set
        {
            if (_ui.CompactMods == value) return;
            _ui.CompactMods = value;
            _ui.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowModDetails));
        }
    }

    public bool ShowModDetails => !CompactMods;

    public bool ModFilterAll { get => ModFilter == ModFilter.All; set { if (value) ModFilter = ModFilter.All; } }
    public bool ModFilterOn { get => ModFilter == ModFilter.On; set { if (value) ModFilter = ModFilter.On; } }
    public bool ModFilterOff { get => ModFilter == ModFilter.Off; set { if (value) ModFilter = ModFilter.Off; } }
    public bool ModFilterProblems { get => ModFilter == ModFilter.Problems; set { if (value) ModFilter = ModFilter.Problems; } }

    public bool HasCoopRow => CoopRow is not null;
    public bool CoopRowOn => CoopRow?.IsSelected == true;
    public bool CoopRowOff => CoopRow is { IsSelected: false };
    public bool HasIncompatibleMods => IncompatibleMods.Count > 0;
    public string OfficialGroupTitle => $"{L["mm.group.official"]} ({OfficialMods.Count})";
    public string IncompatibleGroupTitle => $"{L["mm.group.incompatible"]} ({IncompatibleMods.Count})";
    public bool HasIncompatibleOn => IncompatibleMods.Any(m => m.IsSelected);
    public bool HasNoVisibleMods => VisibleManagedMods.Count == 0;

    public int EnabledModCount => ManagedMods.Count(m => m.IsSelected && m.IsUserMod);

    /// <summary>"Ek mod: 3 açık / 15 yüklü · Coop: açık"</summary>
    public string ModsSummaryText => string.Format(L["mm.summary"], EnabledModCount, ManagedMods.Count(m => m.IsUserMod),
        L[CoopRow is null ? "mm.coop.missing" : CoopRow.IsSelected ? "mm.coop.on" : "mm.coop.off"]);

    public string CoopRowText => CoopRow is null ? L["mm.coop.notInstalled"]
        : string.Format(L[CoopRow.IsSelected ? "mm.coop.rowOn" : "mm.coop.rowOff"], CoopRow.Version);

    public string IncompatibleOnText => string.Format(L["mm.incompatible.on"],
        string.Join(", ", IncompatibleMods.Where(m => m.IsSelected).Select(m => m.Name)));

    public bool HasModProblems => ManagedMods.Any(m => m.IsSelected && m.HasProblem);

    /// <summary>Oyun açıkken başlatıcıya yazılmaz (kapanırken üstüne yazardı).</summary>
    public bool GameRunningForMods => ModuleCompatibility.IsGameRunning(_supervisor.ProcessId);

    partial void OnManagedModSearchChanged(string value) => ApplyManagedFilter();

    partial void OnModFilterChanged(ModFilter value)
    {
        OnPropertyChanged(nameof(ModFilterAll));
        OnPropertyChanged(nameof(ModFilterOn));
        OnPropertyChanged(nameof(ModFilterOff));
        OnPropertyChanged(nameof(ModFilterProblems));
        ApplyManagedFilter();
    }

    /// <summary>Diskteki modları ve başlatıcının listesini okur.</summary>
    private void LoadManagedMods()
    {
        _modFoldersStamp = ModFoldersStamp();
        RescanModules();
        var byId = _installedModules.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var launcher = LauncherProfile.Read();

        ManagedMods.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in launcher)
        {
            if (!byId.TryGetValue(entry.Id, out var module) || module.IsMultiplayer || !seen.Add(entry.Id)) continue;
            ManagedMods.Add(CreateRow(module, entry.Id, entry.IsSelected));
        }

        // Başlatıcının henüz görmediği yeni modlar sona, kapalı.
        foreach (var module in _installedModules.Where(m => !m.IsMultiplayer && seen.Add(m.Id)))
            ManagedMods.Add(CreateRow(module, module.Id, false));

        _savedMods = Snapshot();
        RegroupMods();
        UpdateModChanges();
        RefreshBrowseInstalled();
        ModsWorld ??= SelectedCampaign;
        MarkWorldMods();
        _ = LoadModDescriptionsAsync();
    }

    private ManagedMod CreateRow(InstalledModule module, string id, bool selected)
    {
        var row = new ManagedMod(module, id, selected, OnManagedModChanged) { TurnedOn = EnableDependencies };
        var community = _community.Find(id);
        var role = CoopModRoles.Classify(module, community?.Status ?? CommunityStatus.Unknown);
        row.Role = role;
        if (!row.IsOfficial && !row.IsCoop)
        {
            row.RoleLabel = L["mm.role." + role];
            row.RoleTip = L["mm.role." + role + ".tip"];
        }
        else if (role == CoopModRole.Incompatible)
        {
            row.RoleLabel = L["mm.role.Incompatible"];
            row.RoleTip = L["mm.role.Incompatible.tip"];
        }

        row.CommunityWorks = community?.Status == CommunityStatus.Works;
        row.NotSubscribed = _unsubscribed.Contains(id);
        if (!row.IsOfficial && CoopModRoles.IsForOtherGameVersion(module.GameVersion, _game?.GameVersion))
            row.VersionWarning = string.Format(L["mm.versionWarning"], module.GameVersion, _game?.GameVersion);
        return row;
    }

    private List<(string, bool)> Snapshot() => ManagedMods.Select(m => (m.Id, m.IsSelected)).ToList();

    /// <summary>Satırları gruplara dağıtır: Coop, oyunun modülleri, kullanıcının modları, Coop'la çalışmayanlar.</summary>
    private void RegroupMods()
    {
        CoopRow = ManagedMods.FirstOrDefault(m => m.IsCoop);
        OfficialMods.Clear();
        IncompatibleMods.Clear();
        foreach (var m in ManagedMods)
        {
            if (m.IsCoop) continue;
            if (m.Role == CoopModRole.Incompatible) IncompatibleMods.Add(m);
            else if (m.IsOfficial) OfficialMods.Add(m);
        }

        UpdateModProblems();
        ApplyManagedFilter();
    }

    /// <summary>
    /// Açılan modun gerektirdiği modları da açar (Bannerloot → ButterLib, Harmony…). Yoksa oyun
    /// açılışta "gerekli modül eksik" deyip durur. Kapatırken dokunulmaz; kullanıcının seçimi.
    /// </summary>
    private bool _enablingDependencies;

    private void EnableDependencies(ManagedMod mod)
    {
        // İç içe çağrı: dıştaki plan dolaylı bağımlılıkları zaten kapsıyor (tek bildirim).
        if (mod.Module is null || _enablingDependencies) return;
        var plan = ModPlanner.ForMod(mod.Id, GameModules, _coop?.ModuleId ?? "Coop");
        var turnedOn = new List<string>();
        _enablingDependencies = true;
        try
        {
            foreach (var planned in plan.Mods)
            {
                if (planned.Module.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase)) continue;
                var row = ManagedMods.FirstOrDefault(m => m.Id.Equals(planned.Module.Id, StringComparison.OrdinalIgnoreCase));
                if (row is null || row.IsSelected || !row.CanToggle) continue;
                row.IsSelected = true;
                turnedOn.Add(row.Name);
            }
        }
        finally
        {
            _enablingDependencies = false;
        }

        if (turnedOn.Count > 0)
            ShowToast(string.Format(L["mm.depsTurnedOn"], mod.Name, string.Join(", ", turnedOn)), ToastKind.Info);
    }

    private void OnManagedModChanged()
    {
        UpdateModProblems();
        UpdateModChanges();
        ApplyManagedFilter();
    }

    /// <summary>Kayıtlı listeyle farkı hesaplar: kaç mod açılacak, kapanacak, sıra değişti mi.</summary>
    private void UpdateModChanges()
    {
        var now = Snapshot();
        var before = _savedMods.ToDictionary(x => x.Id, x => x.On, StringComparer.OrdinalIgnoreCase);
        var turnOn = now.Count(x => x.Item2 && !(before.TryGetValue(x.Item1, out var b) && b));
        var turnOff = now.Count(x => !x.Item2 && before.TryGetValue(x.Item1, out var b) && b);

        var savedOrder = _savedMods.Where(x => x.On).Select(x => x.Id).ToList();
        var nowOrder = now.Where(x => x.Item2).Select(x => x.Item1).Where(id => savedOrder.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var reordered = !savedOrder.Where(id => nowOrder.Contains(id, StringComparer.OrdinalIgnoreCase)).SequenceEqual(nowOrder, StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        if (turnOn > 0) parts.Add(string.Format(L["mm.changes.on"], turnOn));
        if (turnOff > 0) parts.Add(string.Format(L["mm.changes.off"], turnOff));
        if (reordered) parts.Add(L["mm.changes.order"]);

        ModsDirty = parts.Count > 0;
        ModChangesText = parts.Count == 0 ? ""
            : string.Join(", ", parts) + ". " + L[GameRunningForMods ? "mm.changes.gameRunning" : "mm.changes.apply"];
        OnPropertyChanged(nameof(GameRunningForMods));
    }

    /// <summary>Açık modların eksik bağımlılıkları ve Coop'un reddettikleri.</summary>
    private void UpdateModProblems()
    {
        var on = new HashSet<string>(ManagedMods.Where(m => m.IsSelected).Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var index = ManagedMods.Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in ManagedMods)
        {
            if (!mod.IsSelected || mod.Module is null) { mod.Problem = ""; continue; }
            if (mod.Role == CoopModRole.Incompatible) { mod.Problem = L["mm.problem.blocked"]; continue; }

            var missing = mod.Module.Dependencies.Where(d => !d.Optional && !on.Contains(d.Id)).Select(d => d.Id).ToList();
            if (missing.Count > 0) { mod.Problem = string.Format(L["mm.problem.missing"], string.Join(", ", missing)); continue; }

            var late = mod.Module.LoadAfter.Where(d => on.Contains(d) && index[d] > index[mod.Id]).ToList();
            // Harmony/ButterLib gibi: resmî modüllerden (ya da başka modlardan) önce olmalıydı.
            var early = mod.Module.LoadBefore.Where(d => on.Contains(d) && index.TryGetValue(d, out var i) && i < index[mod.Id]).ToList();
            mod.Problem = late.Count > 0 ? string.Format(L["mm.problem.order"], string.Join(", ", late))
                : early.Count > 0 ? string.Format(L["mm.problem.orderBefore"], string.Join(", ", early))
                : "";
        }

        OnPropertyChanged(nameof(EnabledModCount));
        OnPropertyChanged(nameof(ModsSummaryText));
        OnPropertyChanged(nameof(HasModProblems));
        OnPropertyChanged(nameof(HasCoopRow));
        OnPropertyChanged(nameof(CoopRowOn));
        OnPropertyChanged(nameof(CoopRowOff));
        OnPropertyChanged(nameof(CoopRowText));
        OnPropertyChanged(nameof(HasIncompatibleMods));
        OnPropertyChanged(nameof(OfficialGroupTitle));
        OnPropertyChanged(nameof(IncompatibleGroupTitle));
        OnPropertyChanged(nameof(HasIncompatibleOn));
        OnPropertyChanged(nameof(IncompatibleOnText));
    }

    private void ApplyManagedFilter()
    {
        var q = ManagedModSearch.Trim();
        VisibleManagedMods.Clear();
        foreach (var m in ManagedMods.Where(m => m.IsUserMod))
        {
            if (q.Length > 0 && !m.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) && !m.Id.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var keep = ModFilter switch
            {
                ModFilter.On => m.IsSelected,
                ModFilter.Off => !m.IsSelected,
                ModFilter.Problems => m.HasProblem || m.HasVersionWarning,
                _ => true,
            };
            if (keep) VisibleManagedMods.Add(m);
        }

        OnPropertyChanged(nameof(HasNoVisibleMods));
    }

    // --- sunucu dünyası: modlar hangi sunucuya kurulur -------------------

    /// <summary>
    /// Modlar sayfasında seçili sunucu dünyası. Satırdaki "Sunucuya ekle" modu bu dünyanın mod
    /// listesine ekler; sunucu açılınca o modlar sunucuya kurulur.
    /// </summary>
    [ObservableProperty] private SaveSet? _modsWorld;

    public bool HasModsWorld => ModsWorld is not null;

    /// <summary>Mod listesinin sağ sütun başlığı: "Sunucu: dünya adı".</summary>
    public string ModsServerColumnText => ModsWorld is { } w ? string.Format(L["mm.col.server"], w.Name) : L["mm.col.noServer"];

    /// <summary>"Bu dünyada 3 mod (modlu)" ya da "Bu dünya modsuz".</summary>
    public string ModsWorldText
    {
        get
        {
            if (ModsWorld is null) return L["mm.world.none"];
            var count = ManagedMods.Count(m => m.InWorld);
            return count == 0 ? L["mm.world.noMods"] : string.Format(L["mm.world.count"], count);
        }
    }

    partial void OnModsWorldChanged(SaveSet? value)
    {
        MarkWorldMods();
        OnPropertyChanged(nameof(HasModsWorld));
        OnPropertyChanged(nameof(ModsServerColumnText));
    }

    /// <summary>Satırlara ve arama kartlarına "bu sunucuda" işaretini koyar.</summary>
    private void MarkWorldMods()
    {
        var ids = ModsWorld is { } w ? LoadWorldModsSafe(w.Name).Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        foreach (var m in ManagedMods) m.InWorld = ids.Contains(m.Id);

        var workshopInWorld = _installedModules.Where(m => m.WorkshopId is not null && ids.Contains(m.Id)).Select(m => m.WorkshopId!).ToHashSet(StringComparer.Ordinal);
        foreach (var card in BrowseCards) card.InWorld = card.IsWorkshop && workshopInWorld.Contains(card.Result.Id);
        OnPropertyChanged(nameof(ModsWorldText));
    }

    /// <summary>Modu seçili sunucu dünyasına ekler ya da çıkarır (dünyanın profiline hemen yazılır).</summary>
    [RelayCommand]
    private void ToggleModInWorld(ManagedMod? mod)
    {
        if (mod is null || ModsWorld is not { } world) return;
        if (IsRunning && string.Equals(world.Name, ActiveSaveName, StringComparison.OrdinalIgnoreCase))
        {
            ShowToast(L["mm.world.running"], ToastKind.Warning);
            return;
        }

        var mods = LoadWorldModsSafe(world.Name).ToList();
        var add = !mods.Any(m => m.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
        var coopId = _coop?.ModuleId ?? "Coop";
        var alsoAdded = new List<string>();

        if (add)
        {
            // Mod tek başına çalışmaz: gerektirdikleri (ButterLib, Harmony…) de sunucuya girer;
            // Coop oyuncudan da hepsini ister.
            if (_unsubscribed.Contains(mod.Id))
            {
                ShowToast(string.Format(L["mm.world.notSubscribed"], mod.Name), ToastKind.Warning);
                return;
            }

            var plan = ModPlanner.ForMod(mod.Id, FreshGameModules(), coopId);
            if (plan.MissingDependencies.Count > 0)
            {
                ShowToast(string.Format(L["mm.world.missingDeps"], mod.Name,
                    DescribeMissing(plan.MissingDependencies.Select(d => d.Dependency))), ToastKind.Warning);
                return;
            }

            foreach (var planned in plan.Mods)
            {
                if (mods.Any(m => m.Id.Equals(planned.Module.Id, StringComparison.OrdinalIgnoreCase))) continue;
                mods.Add(new WorldMod(planned.Module.Id));
                if (!planned.Module.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase)) alsoAdded.Add(planned.Module.Name);
            }
        }
        else
        {
            var neededBy = ModPlanner.NeededBy(mod.Id, mods.Select(m => m.Id), GameModules, coopId);
            if (neededBy.Count > 0)
            {
                var names = neededBy.Select(id => ManagedMods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id);
                ShowToast(string.Format(L["mm.world.neededBy"], mod.Name, string.Join(", ", names)), ToastKind.Warning);
                return;
            }

            mods.RemoveAll(m => m.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            _profiles.SaveMods(world.Name, mods);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast(ex.Message, ToastKind.Error);
            return;
        }

        MarkWorldMods();
        if (SelectedCampaign?.Name == world.Name && !WorldEditor.IsDirty) LoadWorldEditor();
        ShowToast(alsoAdded.Count > 0
                ? string.Format(L["mm.world.addedWithDeps"], mod.Name, world.Name, string.Join(", ", alsoAdded))
                : string.Format(L[add ? "mm.world.added" : "mm.world.removed"], mod.Name, world.Name),
            add ? ToastKind.Success : ToastKind.Info);
    }

    /// <summary>Coop'un satırındaki "Aç" düğmesi.</summary>
    [RelayCommand]
    private void EnableCoop()
    {
        if (CoopRow is { } coop) coop.IsSelected = true;
    }

    /// <summary>Coop'un reddettiklerinin hepsini kapatır.</summary>
    [RelayCommand]
    private void DisableIncompatibleMods()
    {
        foreach (var m in IncompatibleMods.Where(m => m.IsSelected).ToList()) m.IsSelected = false;
    }

    [RelayCommand]
    private void MoveModUp(ManagedMod? mod) => MoveMod(mod, -1);

    [RelayCommand]
    private void MoveModDown(ManagedMod? mod) => MoveMod(mod, +1);

    /// <summary>Kullanıcının modları arasında bir sıra yukarı/aşağı (gizli satırlar atlanır).</summary>
    private void MoveMod(ManagedMod? mod, int delta)
    {
        if (mod is not { CanMove: true }) return;
        var i = ManagedMods.IndexOf(mod);
        var j = i + delta;
        while (j >= 0 && j < ManagedMods.Count && !ManagedMods[j].IsUserMod) j += delta;
        if (i < 0 || j < 0 || j >= ManagedMods.Count) return;
        ManagedMods.Move(i, j);
        OnManagedModChanged();
    }

    /// <summary>Sürükle-bırak: modu hedef modun yerine taşır.</summary>
    public void MoveModTo(ManagedMod mod, ManagedMod target)
    {
        if (mod == target || !mod.CanMove || !target.CanMove) return;
        var i = ManagedMods.IndexOf(mod);
        var j = ManagedMods.IndexOf(target);
        if (i < 0 || j < 0) return;
        ManagedMods.Move(i, j);
        OnManagedModChanged();
    }

    /// <summary>Açık modları bağımlılıklarına göre dizer (oyunun modülleri başta, Coop en sonda).</summary>
    [RelayCommand]
    private void AutoSortMods()
    {
        var on = ManagedMods.Where(m => m.IsSelected).Select(m => m.Id).ToList();
        var sorted = LoadOrder.Sort(on, _installedModules, out var conflict);
        var byId = ManagedMods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var rest = ManagedMods.Where(m => !m.IsSelected).ToList();

        ManagedMods.Clear();
        foreach (var id in sorted) ManagedMods.Add(byId[id]);
        foreach (var m in rest) ManagedMods.Add(m);

        OnManagedModChanged();
        ShowToast(L[conflict ? "mm.sortConflict" : "mm.sorted"], conflict ? ToastKind.Warning : ToastKind.Success);
    }

    /// <summary>
    /// Listeyi oyunun başlatıcısına yazar. Yazılan sıra her zaman: oyunun modülleri (kendi
    /// sırasıyla) → kullanıcının modları → Coop en sonda → kapalı olanlar.
    /// </summary>
    [RelayCommand]
    private void ApplyModChanges()
    {
        if (GameRunningForMods)
        {
            ShowToast(L["coop.gameRunning"], ToastKind.Warning);
            UpdateModChanges();
            return;
        }

        try
        {
            var comparer = StringComparer.OrdinalIgnoreCase;

            // Açık modlar yükleme sırasına dizilir: kullanıcının sırası korunur, yalnızca modların
            // kendi istekleri (gereken mod önce, Harmony/ButterLib gibi kütüphaneler resmî
            // modüllerden önce, Coop en sonda) uygulanır. Yanlış sırada oyun açılışta durur.
            var onIds = ManagedMods.Where(m => m.IsSelected).Select(m => m.Id).ToList();
            var sortedIds = LoadOrder.Sort(onIds, _installedModules, out _);
            var reordered = !sortedIds.SequenceEqual(onIds, comparer);
            var byId = ManagedMods.GroupBy(m => m.Id, comparer).ToDictionary(g => g.Key, g => g.First(), comparer);
            var ordered = sortedIds.Select(id => byId[id]).Concat(ManagedMods.Where(m => !m.IsSelected)).ToList();

            LauncherProfile.Write(ordered.Select(m => new LauncherMod(m.Id, m.IsSelected)).ToList());

            ManagedMods.Clear();
            foreach (var m in ordered) ManagedMods.Add(m);
            _savedMods = Snapshot();
            RegroupMods();
            UpdateModChanges();
            CheckModules();
            RefreshSetupChecks();
            ShowToast(L[reordered ? "mm.appliedSorted" : "mm.applied"], ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private void RevertModChanges() => LoadManagedMods();

    /// <summary>Atölye modlarının kısa açıklamaları (önbellekten; yoksa arka planda indirilir).</summary>
    private async Task LoadModDescriptionsAsync()
    {
        var service = new WorkshopDetailsService(WorkshopDetailsService.DefaultCacheDirectory);
        foreach (var row in ManagedMods.Where(m => m.IsUserMod && m.Module?.WorkshopId is { Length: > 0 }).ToList())
        {
            try
            {
                var details = service.TryGetCached(row.Module!.WorkshopId!) ?? await service.GetAsync(ModsHttp.Value, row.Module.WorkshopId!);
                if (details is not null) row.Description = CoopModRoles.ShortDescription(details.Description);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return; // çevrimdışı: açıklamasız devam
            }
        }
    }

    [RelayCommand]
    private void OpenGameModsFolder()
    {
        if (GameModulesDir is { } dir) OpenPath(dir);
    }

    /// <summary>Zip ya da klasörden mod kurar (Nexus'tan indirilenler, arkadaştan gelenler).</summary>
    [RelayCommand]
    private async Task InstallModFromZipAsync()
    {
        if (GameModulesDir is not { } modules) return;
        var path = await PickOpenFileAsync(L["mm.pickZip"], ["*.zip"]);
        if (path is null) return;

        try
        {
            var installed = ModArchiveInstaller.InstallZip(path, modules, RemovedModsDir);
            LoadManagedMods();
            foreach (var m in installed)
                if (ManagedMods.FirstOrDefault(x => x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)) is { } row && row.CanToggle) row.IsSelected = true;
            ShowToast(string.Format(L["mm.installedFromZip"], string.Join(", ", installed.Select(m => m.Name))), ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowToast(string.Format(L["mm.installFailed"], ex.Message), ToastKind.Error);
        }
    }

    [RelayCommand]
    private void RequestRemoveMod(ManagedMod? mod) => PendingRemoveMod = mod;

    [RelayCommand]
    private void CancelRemoveMod() => PendingRemoveMod = null;

    /// <summary>Modu kaldırır: klasörü yedek klasörüne taşınır, geri getirilebilir.</summary>
    [RelayCommand]
    private void ConfirmRemoveMod()
    {
        if (PendingRemoveMod is not { Module: { } module } mod) return;
        PendingRemoveMod = null;
        if (ModuleCompatibility.IsGameRunning(_supervisor.ProcessId))
        {
            ShowToast(L["coop.gameRunning"], ToastKind.Warning);
            return;
        }

        try
        {
            ModArchiveInstaller.Remove(module, RemovedModsDir);
            LoadManagedMods();
            ShowToast(string.Format(L["mm.removed"], mod.Name), ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Modun bilgi penceresi (açıklama, görsel, Atölye sayfası).</summary>
    [RelayCommand]
    private Task ShowManagedModInfo(ManagedMod? mod) =>
        mod is null
            ? Task.CompletedTask
            : OpenModInfo(new ModuleRow(mod.Id, mod.Version, mod.IsSelected, ModuleCompatibility.Classify(mod.Id), "")
            {
                DisplayName = mod.Name,
                WorkshopId = mod.Module?.WorkshopId,
                WorkshopUrl = mod.Module?.WorkshopId is { } w ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + w : null,
            });

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

            var installed = ModArchiveInstaller.InstallZip(zip, modules, RemovedModsDir);
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
            PackItems.Add(new PackItemRow(s, _unsubscribed.Contains(s.Entry.Id) ? L["mm.state.notSubscribed"] : L["mm.state." + s.State]));
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
            var bundledMissing = ModPackFile.Compare(pack.Pack, GameModules)
                .Where(s => s.State is ModPackItemState.MissingBundled or ModPackItemState.WrongVersion && s.Entry.Bundled).ToList();
            if (bundledMissing.Count > 0 && pack.FilesFolder is { } files)
            {
                foreach (var s in bundledMissing)
                {
                    var folder = Path.Combine(files, s.Entry.Id);
                    if (Directory.Exists(folder)) installedFromPack += ModArchiveInstaller.InstallFolder(folder, modules, RemovedModsDir).Count;
                }
            }

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

    /// <summary>Pakette yazmayan ama gereken mod için liste girdisi.</summary>
    private ModPackEntry DependencyEntry(string id)
    {
        var module = _installedModules.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return new ModPackEntry(id, module?.Name ?? id, module?.Version,
            module?.Source == ModuleSource.Workshop ? ModPackSource.Workshop : ModPackSource.Local, module?.WorkshopId);
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
        foreach (var id in plan.Mods.Select(m => m.Module.Id).Concat(list))
            if (!result.Contains(id, comparer)) result.Add(id);
        return result;
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
            ModPackFile.ExportZip(pack.Pack, _installedModules, path, PackIncludeWorkshop);
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
            if (modulesFolder is not null) Directory.Move(modulesFolder, files);

            LoadPacks();
            SelectedPack = Packs.FirstOrDefault(p => p.Path == target);
            ShowToast(string.Format(L[pack.Invite is null ? "mm.pack.imported" : "invite.pack.imported"], pack.Name), ToastKind.Success);
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

    // =====================================================================
    // Dosya seçme pencereleri
    // =====================================================================

    private static IStorageProvider? Storage =>
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow?.StorageProvider;

    private static async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string> patterns)
    {
        if (Storage is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns.ToList() }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static async Task<string?> PickSaveFileAsync(string title, string suggestedName, string pattern)
    {
        if (Storage is not { } storage) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType(title) { Patterns = [pattern] }],
        });
        return file?.TryGetLocalPath();
    }
}
