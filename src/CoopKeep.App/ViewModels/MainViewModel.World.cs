using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Configuration;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Mods;
using CoopKeep.Core.Saves;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Dünya ayarları, dünyanın modları ve "Yeni dünya" sihirbazı.
/// </summary>
/// <remarks>
/// <para>
/// Oyunda yeni bir sandbox açarken zorluk ve kurallar seçiliyor; CoopKeep'te de öyle. Coop'un
/// ayar dosyası (<c>mod-config.json</c>) tek ve ortak; her dünyanın ayarları kendi profilinde
/// durur (<see cref="WorldProfileStore"/>) ve sunucu açılmadan hemen önce o dosyaya yazılır.
/// </para>
/// <para>
/// Dünyanın modları: "modlu dünya" seçilmişse sunucu o modlarla açılır ve oyuncuların da aynı
/// modlarla girmesi gerekir (Coop modları iki yönde birebir karşılaştırır). Seçilmemişse dünya
/// modsuz açılır (Coop'un önerdiği).
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private readonly WorldProfileStore _profiles = new(WorldProfileStore.DefaultDirectory);

    /// <summary>Coop'un oyun ayarları (<c>mod-config.json</c>).</summary>
    private static readonly SettingsCatalog CoopSettings = new(ModSettingsCatalog.All);

    /// <summary>Seçili dünyanın ayar formu.</summary>
    public WorldSettingsEditor WorldEditor { get; } = new(CoopSettings);

    /// <summary>Seçili dünyanın kaydedilmiş ayarları var mı? Yoksa varsayılanlar gösterilir.</summary>
    [ObservableProperty] private bool _worldHasProfile;

    /// <summary>Editörde şu an hangi dünyanın ayarları açık.</summary>
    private string? _editorWorldName;

    /// <summary>Seçili dünyanın kullanabileceği modlar (Dünyalar sayfası).</summary>
    public ObservableCollection<WorldModOption> WorldMods { get; } = [];

    /// <summary>Dünya modlu mu? Değilse yalnızca oyunun kendi modülleri ve Coop ile açılır.</summary>
    [ObservableProperty] private bool _worldModsCustom;

    public bool HasWorldMods => WorldMods.Count > 0;

    private bool _loadingWorldMods;

    private Dictionary<string, object> LoadWorldValuesSafe(string worldName)
    {
        try { return _profiles.Load(worldName) ?? CoopSettings.Defaults(); }
        catch (Exception) { return CoopSettings.Defaults(); }
    }

    private IReadOnlyList<WorldMod> LoadWorldModsSafe(string worldName)
    {
        try { return _profiles.LoadMods(worldName); }
        catch (Exception) { return []; }
    }

    private void LoadWorldEditor()
    {
        if (SelectedCampaign is null) return;

        _editorWorldName = SelectedCampaign.Name;
        WorldHasProfile = _profiles.Exists(SelectedCampaign.Name);
        WorldEditor.Load(LoadWorldValuesSafe(SelectedCampaign.Name));

        _loadingWorldMods = true;
        try
        {
            var mods = LoadWorldModsSafe(SelectedCampaign.Name);
            WorldModsCustom = mods.Count > 0;
            FillModOptions(WorldMods, mods.Select(m => m.Id).ToList(), MarkWorldModsDirty);
        }
        finally
        {
            _loadingWorldMods = false;
        }

        OnPropertyChanged(nameof(HasWorldMods));
        SaveWorldSettingsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Dünyada seçilebilecek modlar: oyun başlatıcısının listesindekiler; oyunun resmî
    /// modülleri, Coop'un kendisi ve Coop ile çalışmadığı bilinenler hariç.
    /// </summary>
    private static void FillModOptions(ObservableCollection<WorldModOption> target, IReadOnlyList<string> selected, Action changed)
    {
        target.Clear();
        var check = ModuleCompatibility.Check();
        if (check is null) return;

        foreach (var entry in check.All)
        {
            if (!IsWorldMod(entry.Id)) continue;
            var isSelected = selected.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);
            target.Add(new WorldModOption(entry.Id, entry.Version, isSelected, changed));
        }
    }

    private static bool IsWorldMod(string id) =>
        !ModuleCompatibility.ServerModules.Contains(id) &&
        !id.Equals("Coop", StringComparison.OrdinalIgnoreCase) &&
        !id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase) &&
        ModuleCompatibility.Classify(id) != ModuleStatus.Blocked;

    private void MarkWorldModsDirty()
    {
        if (_loadingWorldMods) return;
        WorldEditor.IsDirty = true;
        SaveWorldSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnWorldModsCustomChanged(bool value) => MarkWorldModsDirty();

    private IReadOnlyList<WorldMod> CurrentWorldMods() =>
        WorldModsCustom ? WorldMods.Where(m => m.IsSelected).Select(m => new WorldMod(m.Id)).ToList() : [];

    private void SaveWorldProfile(string worldName, IReadOnlyDictionary<string, object> values, IReadOnlyList<WorldMod> mods)
    {
        _profiles.Save(worldName, values);
        _profiles.SaveMods(worldName, mods);
    }

    [RelayCommand(CanExecute = nameof(CanSaveWorldSettings))]
    private void SaveWorldSettings()
    {
        if (SelectedCampaign is null) return;

        try
        {
            SaveWorldProfile(SelectedCampaign.Name, WorldEditor.Values, CurrentWorldMods());
            WorldEditor.IsDirty = false;
            WorldHasProfile = true;
            SaveWorldSettingsCommand.NotifyCanExecuteChanged();

            var runningThis = IsRunning && string.Equals(SelectedCampaign.Name, ActiveSaveName, StringComparison.OrdinalIgnoreCase);
            ShowToast(L[runningThis ? "msg.worldSavedRestart" : "msg.worldSaved"]);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>
    /// Başlatmadan önce: editör bu dünyayı gösteriyorsa ve kaydedilmemiş değişiklik ya da hiç
    /// ayar dosyası yoksa ekrandakini kaydeder (ekranda ne varsa dünya onunla açılsın).
    /// </summary>
    private void SaveWorldEditorFor(string saveName)
    {
        if (!string.Equals(_editorWorldName, saveName, StringComparison.OrdinalIgnoreCase)) return;
        if (!WorldEditor.IsDirty && WorldHasProfile) return;

        SaveWorldProfile(saveName, WorldEditor.Values, CurrentWorldMods());
        WorldEditor.IsDirty = false;
        WorldHasProfile = true;
        SaveWorldSettingsCommand.NotifyCanExecuteChanged();
    }

    private bool CanSaveWorldSettings() => SelectedCampaign is not null && WorldEditor.IsDirty;

    [RelayCommand]
    private void RevertWorldSettings() => LoadWorldEditor();

    /// <summary>Dünyanın modları, oyun başlatıcısının sırasıyla (modsuz dünyada boş).</summary>
    private IReadOnlyList<WorldMod> ModsForWorld(string worldName)
    {
        var mods = LoadWorldModsSafe(worldName);
        var order = ModuleCompatibility.Check()?.All.Select(e => e.Id).ToList() ?? [];
        int Position(string id)
        {
            var i = order.FindIndex(o => o.Equals(id, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }

        return mods.OrderBy(m => Position(m.Id)).ToList();
    }

    /// <summary>
    /// Dünyanın sunucusunda olacak bütün modlar: seçilenler ve onların gerektirdikleri, yükleme
    /// sırasıyla. Sunucu bu listeyle açılır; Coop oyuncuda da birebir aynısını ister. Bilgisayarda
    /// bulunmayanlar sona eklenir ki uyarılarda görünsünler.
    /// </summary>
    private IReadOnlyList<string> WorldModIds(string worldName)
    {
        var mods = ModsForWorld(worldName);
        if (mods.Count == 0) return [];

        var plan = ModPlanner.Plan(mods, FreshGameModules(), _coop?.ModuleId ?? "Coop");
        var ids = plan.Mods.Select(m => m.Module.Id).ToList();
        foreach (var id in plan.MissingMods.Concat(plan.MissingDependencies.Select(d => d.Dependency)))
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);
        return ids;
    }

    private ModConfigStore? _modConfig;

    /// <summary>
    /// Dünyanın oyun ayarlarını Coop'un ayar dosyasına yazar (sunucu açılırken okur).
    /// Dosya yoksa Coop'un kendi şablonundan oluşturulur.
    /// </summary>
    private void ApplyWorldProfile(string saveName)
    {
        if (_modConfig is null) return;

        var values = _profiles.Load(saveName);
        if (values is null) return;

        _modConfig.EnsureExists(ModConfigTemplatePath);
        _modConfig.Write(values);
    }

    private string? ModConfigTemplatePath =>
        _coop is null ? null : System.IO.Path.Combine(_coop.ModRoot, "mod-config.default.json");

    // =====================================================================
    // Yeni dünya sihirbazı: ad → zorluk → kurallar → modlar → sunucu ve özet
    // =====================================================================

    public const int WizardLastStep = 4;

    [ObservableProperty] private bool _wizardOpen;
    [ObservableProperty] private int _wizardStep;
    [ObservableProperty] private string _wizardName = "";
    [ObservableProperty] private bool _wizardCreating;

    public WorldSettingsEditor WizardSettings { get; } = new(CoopSettings);

    // --- tek oyunculu bir kampanyayı içe aktarma (v0.5'teki gibi) ---------

    /// <summary>Yeni dünya mı, oyundaki tek oyunculu bir kampanyayı sunucuda sürdürmek mi?</summary>
    [ObservableProperty] private bool _wizardImport;

    public bool WizardNew { get => !WizardImport; set => WizardImport = !value; }

    /// <summary>Oyunun kendi kayıt klasöründeki kampanyalar.</summary>
    public ObservableCollection<SaveSet> SinglePlayerSaves { get; } = [];

    [ObservableProperty] private SaveSet? _selectedImportSave;

    public bool HasSinglePlayerSaves => SinglePlayerSaves.Count > 0;

    /// <summary>Seçili kaydın başlığı (karakter, seviye, modlar).</summary>
    private SaveMetadata? _importMeta;

    public bool HasImportInfo => WizardImport && _importMeta is not null;

    public string ImportSummary => _importMeta is not { } m ? "" : string.Format(L["wiz.import.summary"],
        m.CharacterName ?? "?", m.HeroLevel?.ToString() ?? "?", m.Day is { } d ? ((int)d).ToString() : "?",
        m.Gold?.ToString("N0") ?? "?");

    /// <summary>
    /// Başka modlarla oluşturulmuş kayıt: Coop sunucusu açarken çöküyor (canlı testte ve Coop'un
    /// hata bildirimlerinde görüldü), bu yüzden içe aktarma engellenir.
    /// </summary>
    public bool ImportBlocked => WizardImport && _importMeta is { IsServerCompatible: false };

    public string ImportProblem => _importMeta is not { } m ? "" : string.Format(L["wiz.import.blocked"],
        string.Join(", ", m.ThirdPartyModules.Take(8)) + (m.ThirdPartyModules.Count > 8 ? $" (+{m.ThirdPartyModules.Count - 8})" : ""));

    public string ImportWarning => _importMeta is { UnsupportedOfficialModules.Count: > 0 } m && m.IsServerCompatible
        ? string.Format(L["wiz.import.warnOfficial"], string.Join(", ", m.UnsupportedOfficialModules))
        : "";

    public bool HasImportWarning => ImportWarning.Length > 0;

    private void ReloadSinglePlayerSaves()
    {
        SinglePlayerSaves.Clear();
        try
        {
            var repo = new SaveRepository(BannerlordInstallation.SinglePlayerSavesDirectory);
            foreach (var s in repo.Discover().Where(s => !s.IsCoopBackup)) SinglePlayerSaves.Add(s);
        }
        catch (Exception)
        {
            // Klasör okunamadı; liste boş kalır ve arayüz bunu söyler.
        }

        SelectedImportSave = SinglePlayerSaves.FirstOrDefault();
        OnPropertyChanged(nameof(HasSinglePlayerSaves));
    }

    private void RaiseImportChanged()
    {
        _importMeta = WizardImport && SelectedImportSave is { } s ? SaveMetadata.TryRead(s.SavePath) : null;
        OnPropertyChanged(nameof(HasImportInfo));
        OnPropertyChanged(nameof(ImportSummary));
        OnPropertyChanged(nameof(ImportBlocked));
        OnPropertyChanged(nameof(ImportProblem));
        OnPropertyChanged(nameof(ImportWarning));
        OnPropertyChanged(nameof(HasImportWarning));
        WizardNextCommand.NotifyCanExecuteChanged();
    }

    partial void OnWizardImportChanged(bool value)
    {
        OnPropertyChanged(nameof(WizardNew));
        RaiseImportChanged();
        RaiseWizardChanged();
    }

    partial void OnSelectedImportSaveChanged(SaveSet? value)
    {
        // Ad boş ya da öneriyse kaydın adını öner.
        if (value is not null && (WizardName.Length == 0 || WizardName.StartsWith("Calradia", StringComparison.Ordinal)) &&
            _saves?.Exists(value.Name) != true)
            WizardName = value.Name;
        RaiseImportChanged();
    }

    public ObservableCollection<WorldModOption> WizardMods { get; } = [];

    public bool HasWizardMods => WizardMods.Count > 0;

    public bool WizardStep0 => WizardStep == 0;
    public bool WizardStep1 => WizardStep == 1;
    public bool WizardStep2 => WizardStep == 2;
    public bool WizardStep3 => WizardStep == 3;
    public bool WizardStep4 => WizardStep == 4;
    public bool WizardCanGoBack => WizardStep > 0 && !WizardCreating;
    public bool WizardIsLastStep => WizardStep == WizardLastStep;
    public bool WizardNotLastStep => !WizardIsLastStep;

    /// <summary>Adım göstergesi: "ADIM 2 / 5".</summary>
    public string WizardStepText => string.Format(L["wiz.stepOf"], WizardStep + 1, WizardLastStep + 1);

    public double WizardProgress => (WizardStep + 1) * 100.0 / (WizardLastStep + 1);

    /// <summary>Ad kutusunun altındaki doğrulama mesajı; sorun yoksa boş.</summary>
    public string WizardNameError
    {
        get
        {
            var name = WizardName.Trim();
            if (name.Length == 0) return "";
            if (!SaveSet.IsValidSaveName(name)) return L["wiz.nameInvalid"];
            if (_saves?.Exists(name) == true) return L["wiz.nameTaken"];
            return "";
        }
    }

    public bool WizardHasNameError => WizardNameError.Length > 0;

    [RelayCommand(CanExecute = nameof(CanOpenWizard))]
    private void OpenWizard()
    {
        WizardStep = 0;
        WizardName = SuggestWorldName();
        WizardCreating = false;
        WizardSettings.Load(CoopSettings.Defaults());
        WizardModded = false;
        WizardImport = false;
        ReloadSinglePlayerSaves();
        FillModOptions(WizardMods, [], () => { });
        OnPropertyChanged(nameof(HasWizardMods));
        WizardOpen = true;
    }

    private bool CanOpenWizard() => InstallationFound && !IsRunning && !IsBusy;

    [RelayCommand]
    private void CloseWizard()
    {
        if (WizardCreating) return;
        WizardOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanWizardNext))]
    private void WizardNext()
    {
        if (WizardStep < WizardLastStep) WizardStep++;
    }

    private bool CanWizardNext() => WizardStep == 0
        ? IsWizardNameUsable() && (!WizardImport || (SelectedImportSave is not null && !ImportBlocked))
        : WizardStep < WizardLastStep;

    [RelayCommand]
    private void WizardBack()
    {
        if (WizardStep > 0 && !WizardCreating) WizardStep--;
    }

    private bool IsWizardNameUsable()
    {
        var name = WizardName.Trim();
        return SaveSet.IsValidSaveName(name) && _saves?.Exists(name) != true;
    }

    /// <summary>Sihirbazda modlu dünya seçildi mi?</summary>
    [ObservableProperty] private bool _wizardModded;

    /// <summary>Sihirbazda seçilenler: dünyanın ayarları ve (modluysa) mod listesi.</summary>
    private void SaveWizardSettings(string name) =>
        SaveWorldProfile(name, WizardSettings.Values,
            WizardModded ? WizardMods.Where(m => m.IsSelected).Select(m => new WorldMod(m.Id)).ToList() : []);

    /// <summary>Oluştur ve başlat: dünya kurulur, sonra sunucu hemen açılır.</summary>
    [RelayCommand(CanExecute = nameof(CanWizardCreate))]
    private async Task WizardCreateAndPlayAsync()
    {
        if (await CreateFromWizardAsync()) await StartAsync();
    }

    /// <summary>Yalnızca oluştur: dünya kurulup kaydedilir; sunucu açılmaz.</summary>
    [RelayCommand(CanExecute = nameof(CanWizardCreate))]
    private Task WizardCreateOnlyAsync() => CreateFromWizardAsync();

    private async Task<bool> CreateFromWizardAsync()
    {
        if (_saves is null || _config is null) return false;

        var name = WizardName.Trim();
        WizardCreating = true;

        try
        {
            SaveWizardSettings(name);
            if (WizardImport)
            {
                // Tek oyunculu kampanya sunucunun kayıt klasörüne kopyalanır ve seçilir.
                if (SelectedImportSave is not { } source || ImportBlocked) return false;
                _saves.Import(source.SavePath, name);
                _config.EnsureSaveName(name);
                ApplyEditedSettings();
                ReloadConfig();
                ReloadCampaigns();
            }
            else if (!await CreateWorldAsync(name)) return false;

            SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == name) ?? SelectedCampaign;
            WizardOpen = false;
            CurrentPage = AppPage.Home;
            ShowToast(string.Format(L["msg.worldCreated"], name));
            return true;
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
            return false;
        }
        finally
        {
            WizardCreating = false;
        }
    }

    partial void OnWizardModdedChanged(bool value) => OnPropertyChanged(nameof(HasWizardMods));

    private bool CanWizardCreate() => WizardIsLastStep && !WizardCreating && !IsRunning && IsWizardNameUsable() &&
                                      (!WizardImport || (SelectedImportSave is not null && !ImportBlocked));

    private string SuggestWorldName()
    {
        for (var i = 1; i < 100; i++)
        {
            var candidate = i == 1 ? "Calradia" : "Calradia " + i;
            if (_saves?.Exists(candidate) != true) return candidate;
        }
        return "";
    }

    partial void OnWizardStepChanged(int value) => RaiseWizardChanged();

    partial void OnWizardCreatingChanged(bool value) => RaiseWizardChanged();

    partial void OnWizardNameChanged(string value)
    {
        OnPropertyChanged(nameof(WizardNameError));
        OnPropertyChanged(nameof(WizardHasNameError));
        RaiseWizardChanged();
    }

    private void RaiseWizardChanged()
    {
        OnPropertyChanged(nameof(WizardStep0));
        OnPropertyChanged(nameof(WizardStep1));
        OnPropertyChanged(nameof(WizardStep2));
        OnPropertyChanged(nameof(WizardStep3));
        OnPropertyChanged(nameof(WizardStep4));
        OnPropertyChanged(nameof(WizardCanGoBack));
        OnPropertyChanged(nameof(WizardIsLastStep));
        OnPropertyChanged(nameof(WizardNotLastStep));
        OnPropertyChanged(nameof(WizardStepText));
        OnPropertyChanged(nameof(WizardProgress));
        WizardNextCommand.NotifyCanExecuteChanged();
        WizardCreateAndPlayCommand.NotifyCanExecuteChanged();
        WizardCreateOnlyCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Bir dünyada açılıp kapatılabilecek bir mod.</summary>
public partial class WorldModOption(string id, string? version, bool selected, Action changed) : ObservableObject
{
    public string Id { get; } = id;

    public string Version { get; } = version ?? "";

    [ObservableProperty] private bool _isSelected = selected;

    partial void OnIsSelectedChanged(bool value) => changed();
}
