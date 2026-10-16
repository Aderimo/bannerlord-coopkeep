using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Configuration;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Saves;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Dünya ayarları ve "Yeni dünya" sihirbazı.
/// </summary>
/// <remarks>
/// <para>
/// Kullanıcı şikayeti: oyunda yeni bir sandbox açarken zorluk ve kurallar seçiliyor,
/// CoopKeep'te ise ad yazıp basınca dünya doğrudan açılıyordu.
/// </para>
/// <para>
/// Çözüm (Coop v0.1.6'da doğrulandı): Coop'un oyun ayarları <c>CoopData\mod-config.json</c>
/// dosyasında ve barındıran taraf bunları <b>her açılışta</b> uyguluyor. CoopKeep her
/// dünyanın ayarlarını ayrı saklıyor ve sunucuyu başlatmadan hemen önce o dünyanınkini
/// dosyaya yazıyor. Böylece her dünya kendi zorluğuyla açılıyor ve ayarlar sonradan da
/// değiştirilebiliyor.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private readonly WorldProfileStore _profiles = new(WorldProfileStore.DefaultDirectory);
    private ModConfigStore? _modConfig;

    /// <summary>Seçili dünyanın ayar formu.</summary>
    public WorldSettingsEditor WorldEditor { get; } = new();

    /// <summary>Seçili dünyanın kendi ayar profili var mı? Yoksa ortak dosyadaki değerler gösterilir.</summary>
    [ObservableProperty] private bool _worldHasProfile;

    private void LoadWorldEditor()
    {
        if (SelectedCampaign is null) return;

        var values = _profiles.Load(SelectedCampaign.Name);
        WorldHasProfile = values is not null;

        values ??= ReadModConfigSafe();
        WorldEditor.Load(values);
        SaveWorldSettingsCommand.NotifyCanExecuteChanged();
    }

    private System.Collections.Generic.Dictionary<string, object> ReadModConfigSafe()
    {
        try { return _modConfig?.Read() ?? ModSettingsCatalog.Defaults(); }
        catch (Exception) { return ModSettingsCatalog.Defaults(); }
    }

    [RelayCommand(CanExecute = nameof(CanSaveWorldSettings))]
    private void SaveWorldSettings()
    {
        if (SelectedCampaign is null) return;

        try
        {
            _profiles.Save(SelectedCampaign.Name, WorldEditor.Values);
            WorldEditor.IsDirty = false;
            WorldHasProfile = true;

            var runningThis = IsRunning && string.Equals(SelectedCampaign.Name, ActiveSaveName, StringComparison.OrdinalIgnoreCase);
            ShowToast(L[runningThis ? "msg.worldSavedRestart" : "msg.worldSaved"]);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    private bool CanSaveWorldSettings() => SelectedCampaign is not null && WorldEditor.IsDirty;

    [RelayCommand]
    private void RevertWorldSettings() => LoadWorldEditor();

    /// <summary>
    /// Başlatmadan hemen önce: dünyanın profilini <c>mod-config.json</c>'a yazar.
    /// </summary>
    /// <remarks>
    /// Profili olmayan (CoopKeep öncesinden kalma) dünyalarda dosyaya dokunulmaz —
    /// kullanıcının elle yaptığı ayarları ezmeyelim.
    /// </remarks>
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
    // Yeni dünya sihirbazı
    // =====================================================================

    public const int WizardLastStep = 4;

    [ObservableProperty] private bool _wizardOpen;
    [ObservableProperty] private int _wizardStep;

    /// <summary>Yeni dünya mı, oyundaki bir kaydı içe aktarma mı?</summary>
    [ObservableProperty] private bool _wizardImport;

    [ObservableProperty] private string _wizardName = "";
    [ObservableProperty] private bool _wizardCreating;

    public WorldSettingsEditor WizardSettings { get; } = new();

    /// <summary>Oyunun kendi save klasöründeki kampanyalar (içe aktarma için).</summary>
    public ObservableCollection<SaveSet> SinglePlayerSaves { get; } = [];

    [ObservableProperty] private SaveSet? _selectedImportSave;

    public bool HasSinglePlayerSaves => SinglePlayerSaves.Count > 0;

    public bool WizardNew { get => !WizardImport; set => WizardImport = !value; }

    public bool WizardStep0 => WizardStep == 0;
    public bool WizardStep1 => WizardStep == 1;
    public bool WizardStep2 => WizardStep == 2;
    public bool WizardStep3 => WizardStep == 3;
    public bool WizardStep4 => WizardStep == 4;
    public bool WizardCanGoBack => WizardStep > 0 && !WizardCreating;
    public bool WizardIsLastStep => WizardStep == WizardLastStep;
    public bool WizardNotLastStep => !WizardIsLastStep;

    /// <summary>Adım göstergesi: "Adım 2 / 5".</summary>
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
        WizardImport = false;
        WizardName = SuggestWorldName();
        WizardCreating = false;
        WizardSettings.Load(ModSettingsCatalog.Defaults());
        ReloadSinglePlayerSaves();
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

    // --- içe aktarılacak kaydın bilgisi -----------------------------------

    /// <summary>Seçili tek oyunculu kaydın başlığı (karakter, seviye, modlar).</summary>
    private SaveMetadata? _importMeta;

    public bool HasImportInfo => WizardImport && _importMeta is not null;

    public string ImportSummary => _importMeta is not { } m ? "" : string.Format(L["wiz.import.summary"],
        m.CharacterName ?? "?", m.HeroLevel?.ToString() ?? "?", m.Day is { } d ? ((int)d).ToString() : "?",
        m.Gold?.ToString("N0") ?? "?");

    /// <summary>
    /// Üçüncü parti modlarla oluşturulmuş kayıt: sunucu açamaz (canlı testte erişim
    /// ihlaliyle çöktü), bu yüzden içe aktarma engellenir.
    /// </summary>
    public bool ImportBlocked => WizardImport && _importMeta is { IsServerCompatible: false };

    public string ImportProblem => _importMeta is not { } m ? "" : string.Format(L["wiz.import.blocked"],
        string.Join(", ", m.ThirdPartyModules.Take(8)) + (m.ThirdPartyModules.Count > 8 ? $" (+{m.ThirdPartyModules.Count - 8})" : ""));

    public string ImportWarning => _importMeta is { UnsupportedOfficialModules.Count: > 0 } m && m.IsServerCompatible
        ? string.Format(L["wiz.import.warnOfficial"], string.Join(", ", m.UnsupportedOfficialModules))
        : "";

    public string WizardCreateHint => L[WizardImport ? "wiz.createHint.import" : "wiz.createHint.new"];

    public string WizardCreateLabel => L[WizardImport ? "wiz.create.import" : "wiz.create.new"];

    private void RaiseImportChanged()
    {
        _importMeta = WizardImport && SelectedImportSave is { } s ? SaveMetadata.TryRead(s.SavePath) : null;
        OnPropertyChanged(nameof(HasImportInfo));
        OnPropertyChanged(nameof(ImportSummary));
        OnPropertyChanged(nameof(ImportBlocked));
        OnPropertyChanged(nameof(ImportProblem));
        OnPropertyChanged(nameof(ImportWarning));
        OnPropertyChanged(nameof(WizardCreateHint));
        OnPropertyChanged(nameof(WizardCreateLabel));
    }

    private bool CanWizardNext() => WizardStep switch
    {
        0 => !WizardImport || (SelectedImportSave is not null && !ImportBlocked),
        1 => IsWizardNameUsable(),
        _ => WizardStep < WizardLastStep,
    };

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

    [RelayCommand(CanExecute = nameof(CanWizardCreate))]
    private async Task WizardCreateAsync()
    {
        if (_saves is null || _config is null) return;

        // Yeni dünya kurmak sunucuyu bir kez çalıştırır: önce lisans onayı.
        if (!WizardImport && !EulaAccepted)
        {
            await RequireEulaAsync(WizardCreateAsync);
            return;
        }

        var name = WizardName.Trim();
        WizardCreating = true;

        try
        {
            if (WizardImport)
            {
                if (SelectedImportSave is null) return;
                _saves.Import(SelectedImportSave.SavePath, name);
            }

            // Ayarlar önce kaydedilir: yeni dünya ilk açılışında bunlarla kurulsun.
            _profiles.Save(name, WizardSettings.Values);

            if (!WizardImport)
            {
                var ok = await CreateWorldAsync(name);
                if (!ok) return;
            }
            else
            {
                _config.EnsureSaveName(name);
                ApplyEditedSettings();
                ReloadConfig();
                ReloadCampaigns();
            }

            SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == name) ?? SelectedCampaign;
            WizardOpen = false;
            CurrentPage = AppPage.Home;
            ShowToast(string.Format(L["msg.worldCreated"], name));
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
        finally
        {
            WizardCreating = false;
        }
    }

    private bool CanWizardCreate() => WizardIsLastStep && !WizardCreating && IsWizardNameUsable() &&
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

    partial void OnWizardStepChanged(int value) => RaiseWizardChanged();
    partial void OnWizardImportChanged(bool value)
    {
        OnPropertyChanged(nameof(WizardNew));
        RaiseImportChanged();
        RaiseWizardChanged();
    }

    partial void OnWizardCreatingChanged(bool value) => RaiseWizardChanged();

    partial void OnSelectedImportSaveChanged(SaveSet? value)
    {
        // İçe aktarılan kaydın adını öner: genelde kullanıcının tanıdığı ad.
        if (value is not null && WizardImport && SaveSet.IsValidSaveName(value.Name) && _saves?.Exists(value.Name) != true)
            WizardName = value.Name;

        RaiseImportChanged();
        RaiseWizardChanged();
    }

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
        WizardCreateCommand.NotifyCanExecuteChanged();
    }
}
