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

    /// <summary>Adın altındaki soluk satır: rol · kaynak · sürüm.</summary>
    [ObservableProperty] private string _metaLine = "";

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
        var source = row.IsWorkshop ? L["mm.src.workshop"] : row.IsLocal ? L["mm.src.local"] : "";
        row.MetaLine = string.Join("  ·  ", new[] { row.RoleLabel, source, row.Version }.Where(s => s.Length > 0));
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
            var skipped = new List<string>();
            var installed = ModArchiveInstaller.InstallZip(path, modules, RemovedModsDir, WorkshopModuleIds(), skipped);
            ReportSkipped(skipped);
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
