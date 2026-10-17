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
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Saves;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>Konsolun sınırsız büyümesini engelleyen üst sınır.</summary>
    private const int MaxConsoleLines = 2000;

    private readonly ServerSupervisor _supervisor = new();
    private readonly DispatcherTimer _uptimeTimer;
    private readonly Settings.UiSettings _ui = Settings.UiSettings.Load();

    private CoopInstallation? _coop;
    private BannerlordInstallation? _game;
    private SaveRepository? _saves;
    private ServerConfigStore? _config;

    /// <summary>
    /// Liste yeniden yüklenirken yapılan otomatik seçimin yapılandırmaya yazmasını engeller.
    /// </summary>
    /// <remarks>
    /// Bu kilit olmadan, aktif kampanya listede bulunamadığında (silinmiş ya da bir Coop
    /// yedeği olduğu için) otomatik seçim başka bir kampanyayı sessizce aktif hâle
    /// getirirdi. Ayrıca her açılışta gereksiz bir dosya yazımı ve <c>.bak</c> üretirdi.
    /// Yalnızca kullanıcının kendi seçimi yapılandırmayı değiştirir.
    /// </remarks>
    private bool _suppressSelectionWrite;

    private BackupService? _backups;
    private ServerResourceMonitor? _resources;
    private readonly CrashRestartPolicy _crashPolicy = new();

    public MainViewModel()
    {
        // Kayıtlı dil tercihi varsa sistem dilinin önüne geçer.
        if (!string.IsNullOrEmpty(_ui.Language)) Loc.Current.Language = _ui.Language;

        ShowTutorial = !_ui.TutorialDismissed;
        DontShowTutorialAgain = false;
        PrivacyMode = _ui.PrivacyMode;
        AutoBackup = _ui.AutoBackup;
        AutoRestart = _ui.AutoRestart;
        HighPriority = _ui.HighPriority;
        MemoryWarningGb = _ui.MemoryWarningGb;
        CheckUpdates = _ui.CheckUpdates;
        _serverToolsEnabled = _ui.ServerTools;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _uptimeTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(Uptime));
            SampleResources();
        };
        _uptimeTimer.Start();

        foreach (var entry in Settings.AuditLogStore.Load()) AuditLog.Add(entry);
        foreach (var name in Settings.BlockListStore.Load()) BlockList.Add(name);

        WorldEditor.Changed += () => SaveWorldSettingsCommand.NotifyCanExecuteChanged();

        WireSupervisor();
        Detect();
        InitViewerSettings();
        CheckForForeignServer();

        if (CheckUpdates) _ = CheckForUpdateAsync();
        _ = LoadCommunityAsync();
    }

    /// <summary>
    /// Bu makinede CoopKeep dışında çalışan bir sunucu var mı?
    /// </summary>
    /// <remarks>
    /// CoopKeep başlattığı sürece bağlanabiliyor; kendi başlatmadığı bir sunucuya
    /// sonradan bağlanamıyor. Bunu söylemezsek kullanıcı "Başlat"a basıyor, sunucu
    /// portu bağlayamadığı için açılmıyor ve sebebi anlaşılmıyor.
    /// </remarks>
    private void CheckForForeignServer()
    {
        if (IsRunning) { ForeignServerWarning = ""; return; }

        try
        {
            var running = System.Diagnostics.Process.GetProcessesByName("BannerlordCoopServer");
            foreach (var p in running) p.Dispose();

            ForeignServerWarning = running.Length > 0 ? L["msg.alreadyRunning"] : "";
        }
        catch (InvalidOperationException)
        {
            // Süreç listesi okunamadı; kritik değil.
        }

        RaiseRunStateChanged();
    }

    /// <summary>CoopKeep dışında çalışan bir sunucu varsa uyarı metni; yoksa boş.</summary>
    [ObservableProperty] private string _foreignServerWarning = "";

    public bool HasForeignServer => ForeignServerWarning.Length > 0;

    partial void OnForeignServerWarningChanged(string value)
    {
        OnPropertyChanged(nameof(HasForeignServer));
        StartCommand.NotifyCanExecuteChanged();
    }

    public Loc L => Loc.Current;

    // --- kurulum ---------------------------------------------------------

    [ObservableProperty] private string _gameVersion = "-";
    [ObservableProperty] private string _modVersion = "-";
    [ObservableProperty] private bool _installationFound;
    [ObservableProperty] private bool _versionsMatch;
    [ObservableProperty] private string _dataDirectory = "";

    public bool GameFound => _game is not null;
    public bool CoopFound => _coop is not null;

    public string GameCheckText => GameFound
        ? string.Format(L["setup.gameOk"], GameVersion)
        : L["setup.gameMissing"];

    public string ModCheckText => CoopFound
        ? string.Format(L["setup.modOk"], ModVersion)
        : L["setup.modMissing"];

    // --- sunucu durumu ---------------------------------------------------

    [ObservableProperty] private ServerPhase _phase = ServerPhase.Unknown;
    [ObservableProperty] private string _activeSaveName = "-";
    [ObservableProperty] private string _portText = "-";
    [ObservableProperty] private string _passwordText = "-";
    [ObservableProperty] private string _autosaveText = "-";
    [ObservableProperty] private string _steamText = "-";
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _announceText = "";

    /// <summary>Kampanya adı kutusu — yeniden adlandırma ve kopyalamada hedef ad.</summary>
    [ObservableProperty] private string _campaignNameInput = "";

    /// <summary>Silme onayı bekleniyor mu? (iki adımlı silme)</summary>
    [ObservableProperty] private bool _pendingDelete;

    // --- karşılama ekranı ---

    [ObservableProperty] private bool _showTutorial;
    [ObservableProperty] private bool _dontShowTutorialAgain;

    /// <summary>Karşılama turunun adımı (0-2).</summary>
    [ObservableProperty] private int _tutorialStep;

    /// <summary>
    /// Sunucunun Steam sunucu tarayıcısında göründüğü ad.
    /// </summary>
    /// <remarks>
    /// Bu, hesabın Steam persona adıdır — <b>kampanya adı değil</b>. Sunucu tarayıcısındaki
    /// "Host Name" alanı bunu arar. Gösterilmemesi kullanıcıyı yanıltıyordu: kampanya adıyla
    /// arayınca hiçbir sonuç çıkmıyor.
    /// </remarks>
    [ObservableProperty] private string _steamHostName = "";

    /// <summary>
    /// Hassas bilgiler gizli mi? Yayın yapanlar için.
    /// </summary>
    /// <remarks>
    /// Gizliyken Steam adı ve port maskelenir, konsol bulanıklaştırılır. Konsol
    /// özellikle önemli: içinde dosya yolları (kullanıcı adı dahil), bağlanan
    /// oyuncuların adresleri ve Steam lobi kimliği geçiyor.
    /// </remarks>
    [ObservableProperty] private bool _privacyMode = true;

    // --- ayar düzenleme alanları ---

    [ObservableProperty] private string _editPassword = "";
    [ObservableProperty] private string _editPort = "";
    [ObservableProperty] private string _editAutosave = "";
    [ObservableProperty] private bool _editSteam = true;

    // --- oyuncu arama ---

    [ObservableProperty] private string _playerFilter = "";

    // --- yedekleme ve çökme kurtarma ---

    [ObservableProperty] private bool _autoBackup = true;
    [ObservableProperty] private bool _autoRestart = true;
    [ObservableProperty] private BackupEntry? _selectedBackup;
    [ObservableProperty] private bool _pendingRestore;

    public ObservableCollection<BackupEntry> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    // --- uyarı (çökme vb.) ---

    /// <summary>Ana sayfada kalıcı uyarı: çökme, çökme döngüsü. Yeni başlatmayla silinir.</summary>
    [ObservableProperty] private string _alertText = "";

    public bool HasAlert => AlertText.Length > 0;

    partial void OnAlertTextChanged(string value) => OnPropertyChanged(nameof(HasAlert));

    // --- bağlantı bilgisi ---

    [ObservableProperty] private string _localIp = "-";

    public string DisplayedLocalIp => PrivacyMode ? L["val.masked"] : LocalIp;

    public string DisplayedEndpoint => PrivacyMode ? L["val.masked"] : $"{LocalIp}:{PortText.Replace(" *", "")}";

    /// <summary>
    /// Arkadaşlara gönderilecek davet metni.
    /// </summary>
    /// <remarks>
    /// En sık hata: arkadaşlar sunucu adını arıyor, oysa listede <b>Steam adı</b> görünüyor.
    /// Davet metni bu yüzden aranacak adı açıkça yazar ve mod sürümünü de verir —
    /// farklı sürüm, "Incompatible" görünmesinin sebebi.
    /// </remarks>
    public string ConnectSummary
    {
        get
        {
            var lines = new List<string> { L["invite.text.head"] };
            lines.Add(string.Format(L["invite.text.mod"], ModVersion));
            if (SteamHostName.Length > 0) lines.Add(string.Format(L["invite.text.steam"], SteamHostName));
            lines.Add(string.Format(L["invite.text.ip"], LocalIp, PortText.Replace(" *", "")));
            if (EditPassword.Length > 0) lines.Add(string.Format(L["invite.text.password"], EditPassword));
            lines.Add(L["invite.text.mods"]);
            return string.Join(Environment.NewLine, lines);
        }
    }

    // --- modül uyumu ---

    [ObservableProperty] private string _moduleNotice = "";
    [ObservableProperty] private bool _moduleProblem;

    // --- yönetici işlemleri ---


    public ObservableCollection<AdminAuditEntry> AuditLog { get; } = [];

    public bool HasAuditEntries => AuditLog.Count > 0;

    public bool IsRunning => _supervisor.IsRunning;
    public bool IsServing => _supervisor.IsServing;
    public bool IsBusy => !string.IsNullOrEmpty(BusyMessage);

    /// <summary>Durum rozetinin metni — faz anahtarından yerelleştirilir.</summary>
    public string StatusText => L["status." + Phase switch
    {
        ServerPhase.Boot => "boot",
        ServerPhase.Loading => "loading",
        ServerPhase.Serving => "serving",
        ServerPhase.Stopping => "stopping",
        ServerPhase.Shutdown => "shutdown",
        ServerPhase.Starting => "starting",
        _ => IsRunning ? "starting" : "stopped",
    }];

    /// <summary>Rozet rengi: yeşil yayında, sarı geçişte, turuncu kapanırken, gri kapalı.</summary>
    public IBrush StatusBrush => new SolidColorBrush(Color.Parse(Phase switch
    {
        ServerPhase.Serving => "#4ADE80",
        ServerPhase.Boot or ServerPhase.Loading or ServerPhase.Starting => "#FBBF24",
        ServerPhase.Stopping => "#FB923C",
        _ => IsRunning ? "#FBBF24" : "#6B7280",
    }));

    private static IBrush StepBrush(bool done, bool active) => new SolidColorBrush(Color.Parse(
        done ? "#D4AA32" : active ? "#5A4A1C" : "#2A2F39"));

    public IBrush Step1Brush => StepBrush(Step1Done, Step1Active);
    public IBrush Step2Brush => StepBrush(Step2Done, Step2Active);
    public IBrush Step3Brush => StepBrush(Step3Done, false);

    [ObservableProperty] private string _cpuText = "-";
    [ObservableProperty] private string _ramText = "-";

    /// <summary>
    /// Sunucu süreç ağacının kaynak kullanımını örnekler.
    /// </summary>
    /// <remarks>
    /// Sunucunun bellek <i>sınırı</i> ayarlanamıyor — Bannerlord dedicated server'ında
    /// böyle bir seçenek yok. Bu yüzden yalnızca ölçüp gösteriyoruz.
    /// </remarks>
    private void SampleResources()
    {
        if (_resources is null || !IsRunning)
        {
            if (CpuText != "-") { CpuText = "-"; RamText = "-"; _resources?.Reset(); }
            return;
        }

        // Kapalıyken Normal'e döndürülür; açıkken "normalin üstü" (Yüksek değil: oyunun ve sistemin önüne geçmesin).
        _resources.DesiredPriority = HighPriority
            ? System.Diagnostics.ProcessPriorityClass.AboveNormal
            : System.Diagnostics.ProcessPriorityClass.Normal;
        var sample = _resources.Sample(_supervisor.ProcessId);

        CpuText = sample.ProcessCount == 0 ? "-" : $"{sample.CpuPercent:0}%";
        RamText = sample.ProcessCount == 0 ? "-" : $"{sample.MemoryGigabytes:0.0} GB";

        MemoryWarning = MemoryWarningGb > 0 && sample.MemoryGigabytes > MemoryWarningGb
            ? string.Format(L["msg.memoryHigh"], sample.MemoryGigabytes.ToString("0.0"), MemoryWarningGb)
            : "";
    }

    public string Uptime
    {
        get
        {
            if (!IsRunning || _supervisor.StartedAt is not { } start) return "-";
            var span = DateTimeOffset.UtcNow - start;
            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    // --- koleksiyonlar ---------------------------------------------------

    public ObservableCollection<SaveSet> Campaigns { get; } = [];
    public ObservableCollection<string> Console { get; } = [];

    /// <summary>Sunucudan gelen tam oyuncu listesi.</summary>
    private readonly List<ConnectedPlayer> _allPlayers = [];

    /// <summary>Arama kutusuna göre süzülmüş, arayüzde gösterilen liste.</summary>
    public ObservableCollection<ConnectedPlayer> Players { get; } = [];

    [ObservableProperty] private SaveSet? _selectedCampaign;
    [ObservableProperty] private ConnectedPlayer? _selectedPlayer;

    public bool HasCampaigns => Campaigns.Count > 0;
    public bool HasPlayers => _allPlayers.Count > 0;

    /// <summary>Oyuncu var ama arama hiçbir şey döndürmedi.</summary>
    public bool FilterHidesAll => HasPlayers && Players.Count == 0;

    // --- komutlar --------------------------------------------------------

    [RelayCommand]
    private void ToggleLanguage()
    {
        Loc.Current.Toggle();

        _ui.Language = Loc.Current.Language;
        _ui.Save();

        // Bağlama zincirinin kökünü tazele; C# tarafında hesaplanan metinler ayrıca.
        OnPropertyChanged(nameof(L));
        _profileSheet?.ApplyNames();
        _cheatSheet?.ApplyNames();
        if (CurrentPage == AppPage.Encyclopedia) OnEncyclopediaOpened();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PrivacyLabel));
        OnPropertyChanged(nameof(GameCheckText));
        OnPropertyChanged(nameof(ModCheckText));
        WorldEditor.RefreshTexts();
        WizardSettings.RefreshTexts();
        RaiseHomeChanged();
        RaiseWizardChanged();
        ReloadConfig();
        CheckModules();
    }

    [RelayCommand]
    private void Refresh()
    {
        Detect();
        CheckForForeignServer();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_coop is null || _config is null || SelectedCampaign is null) return;

        // İlk başlatmada sunucunun lisans sözleşmesi gösterilir; onaydan sonra devam edilir.
        if (!EulaAccepted)
        {
            await RequireEulaAsync(StartAsync);
            return;
        }

        try
        {
            BusyMessage = L["msg.readyIn"];
            AlertText = "";

            // Ekrandaki ayarları başlatmadan ÖNCE uygula: ekranda ne yazıyorsa sunucu
            // onunla açılır. SIRA ÖNEMLİ: önce yapılandırma var olmalı, sonra ayarlar yazılır;
            // ters sırada ilk kurulumda kullanıcının yazdığı şifre kayboluyordu.
            _config.EnsureSaveName(SelectedCampaign.Name);
            ApplyEditedSettings();

            // Dünyanın kendi oyun ayarları (zorluk, kurallar) — sunucu her açılışta okur.
            ApplyWorldProfile(SelectedCampaign.Name);

            ReloadConfig();
            LaunchServer();
            CurrentPage = AppPage.Home;
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            ShowToast(ex.Message, ToastKind.Error);
            BusyMessage = "";
        }
    }

    private void LaunchServer()
    {
        if (_coop is null) return;

        Console.Clear();
        ClearConsoleSummary();
        EnsureServerTools();
        _supervisor.Start(new ServerLaunchOptions
        {
            ExecutablePath = _coop.DedicatedServerExe,
            DataDirectory = DataDirectory,
            SteamBanFile = System.IO.Path.Combine(DataDirectory, Core.Server.SteamBanList.FileName),
            AcceptEula = EulaAccepted,
        });

        RaiseRunStateChanged();
    }

    private bool CanStart() =>
        InstallationFound && !IsRunning && SelectedCampaign is not null && !IsBusy && !HasForeignServer;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        _restartAfterExit = false;
        BusyMessage = L["msg.closingServer"];
        await _supervisor.StopAsync();
        BusyMessage = "";
        RaiseRunStateChanged();
    }

    /// <summary>Açılış sırasında da durdurulabilmeli — yanlış dünyayı seçen beklemek zorunda kalmasın.</summary>
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCommand))]
    private async Task SaveNowAsync()
    {
        await SendAsync(ServerCommand.Save());
        ShowToast(L["msg.saveRequested"], ToastKind.Info);
    }

    [RelayCommand(CanExecute = nameof(CanAnnounce))]
    private async Task AnnounceAsync()
    {
        await SendAsync(ServerCommand.Say(AnnounceText));
        ShowToast(L["msg.announced"]);
        AnnounceText = "";
    }

    private bool CanAnnounce() => CanCommand() && !string.IsNullOrWhiteSpace(AnnounceText);

    private bool CanCommand() => IsServing && !IsBusy;

    // --- yeniden başlatma (oyunculara süre tanıyarak) ---------------------

    /// <summary>Sunucu kendi kendine kapandıktan sonra yeniden başlatılsın mı?</summary>
    private bool _restartAfterExit;

    /// <summary>
    /// Oyuncuları uyarıp süre tanıyarak yeniden başlatır.
    /// </summary>
    /// <remarks>
    /// Coop'un kendi komutu kullanılıyor (<c>coop.server.shutdown &lt;sn&gt;</c>, v0.1.6'da
    /// doğrulandı): yeni girişleri kapatır, süre bitince oyuncuları düşürür ve kaydeder.
    /// Sunucu kapanınca CoopKeep onu yeniden açar.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCommand))]
    private async Task RestartWithCountdownAsync()
    {
        const int seconds = 60;

        await SendAsync(ServerCommand.Say(string.Format(L["msg.restartSay"], seconds)));
        await SendAsync(AdminCommand.ScheduleShutdown(seconds));

        _restartAfterExit = true;
        BusyMessage = string.Format(L["msg.restartScheduled"], seconds);
        ShowToast(BusyMessage, ToastKind.Info);
    }

    /// <summary>
    /// Yeni bir dünya kurar: sunucuyu yazılan adla başlatır, <c>serving</c> fazını bekler ve
    /// <c>stop</c> ile kaydettirir.
    /// </summary>
    /// <remarks>
    /// Server, yapılandırılan save diskte yoksa <c>default_new_game.sav</c>'dan taze bir
    /// dünya kuruyor. Dünya ayarları önceden profil olarak kaydedildiği için ilk açılışta
    /// <c>mod-config.json</c>'a yazılıyor ve dünya bu ayarlarla kuruluyor.
    /// </remarks>
    private async Task<bool> CreateWorldAsync(string name)
    {
        if (_coop is null || _config is null || _saves is null) return false;
        if (!SaveSet.IsValidSaveName(name) || _saves.Exists(name)) return false;

        // Hızlı yol: şablon kopyalanır, sunucu açılmaz. Dünya ayarları ilk "Başlat"ta
        // mod-config.json'a yazılır ve sunucu dünyayı o açılışta kurar.
        try
        {
            if (CreateWorldFromTemplate(name)) return true;
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }

        var ready = new TaskCompletionSource();
        void OnPhase(ServerPhase p) { if (p == ServerPhase.Serving) ready.TrySetResult(); }
        void OnExit(ServerExitInfo i) => ready.TrySetException(
            new InvalidOperationException(ServerExit.Describe(i.Kind)));

        try
        {
            BusyMessage = L["msg.creating"];

            // Yeni dünya kurulurken de sunucu gerçekten ayağa kalkıyor ve bu sırada
            // bağlanılabilir. Ekrandaki şifre/port ayarları burada da geçerli olmalı.
            _config.EnsureSaveName(name);
            ApplyEditedSettings();
            ApplyWorldProfile(name);

            _supervisor.PhaseChanged += OnPhase;
            _supervisor.Exited += OnExit;

            LaunchServer();

            var timeout = Task.Delay(TimeSpan.FromMinutes(8));
            if (await Task.WhenAny(ready.Task, timeout) == timeout)
                throw new TimeoutException(L["msg.createTimeout"]);

            await ready.Task;
            await _supervisor.StopAsync();
            return true;
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            ShowToast(ex.Message, ToastKind.Error);
            if (IsRunning) await _supervisor.StopAsync();
            return false;
        }
        finally
        {
            _supervisor.PhaseChanged -= OnPhase;
            _supervisor.Exited -= OnExit;

            BusyMessage = "";
            ReloadConfig();
            ReloadCampaigns();
            SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == name) ?? SelectedCampaign;
        }
    }

    /// <summary>Yeni dünyayı oyunun <c>default_new_game.sav</c> şablonundan kopyalar.</summary>
    private bool CreateWorldFromTemplate(string name)
    {
        if (_coop is null || _config is null || _saves is null) return false;

        var serverDir = Path.GetDirectoryName(_coop.DedicatedServerExe)!;
        var created = _saves.CreateFromTemplate(name,
        [
            Path.Combine(serverDir, "server-data", "Game Saves", SaveRepository.TemplateFileName),
            Path.Combine(serverDir, "engine", "bin", "Win64_Shipping_Server", SaveRepository.TemplateFileName),
        ]);
        if (created is null) return false;

        _config.EnsureSaveName(name);
        ApplyEditedSettings();
        ReloadConfig();
        ReloadCampaigns();
        SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == name) ?? SelectedCampaign;
        return true;
    }

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

    [RelayCommand]
    private void OpenSavesFolder() =>
        OpenPath(System.IO.Path.Combine(DataDirectory, "Game Saves"));

    [RelayCommand]
    private void OpenAuthorPage() => OpenPath(AuthorUrl);

    /// <summary>Coop modunun Workshop sayfası — kurulum eksikse ilk adım.</summary>
    [RelayCommand]
    private void OpenWorkshopPage() => OpenPath("steam://url/CommunityFilePage/3770450698");

    private void OpenPath(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { ShowToast(ex.Message, ToastKind.Error); }
    }

    public string AuthorUrl => "https://gitgit.me/aderimo";
    public string AuthorName => "aderimo";

    /// <summary>Uygulama sürümü — <c>Directory.Build.props</c> içindeki tek kaynaktan gelir.</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(MainViewModel).Assembly.GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // --- bağlantı bilgisi -------------------------------------------------

    [RelayCommand]
    private async Task CopyConnectInfoAsync()
    {
        var top = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;

        var clipboard = top?.MainWindow?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(ConnectSummary);
        ShowToast(L["msg.inviteCopied"]);
    }

    private static async Task CopyTextAsync(string text)
    {
        var top = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (top?.MainWindow?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>Yerel ağ adresini bulur — doğrudan bağlanacak arkadaşlar için.</summary>
    private static string DetectLocalIp()
    {
        try
        {
            // Dışarı bir UDP soketi "bağlamak" paket göndermez ama işletim sistemine
            // hangi yerel arayüzün kullanılacağını sorar.
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);

            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "-";
        }
        catch (System.Net.Sockets.SocketException)
        {
            return "-";
        }
    }

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

    // --- gizlilik --------------------------------------------------------

    [RelayCommand]
    private void TogglePrivacy()
    {
        PrivacyMode = !PrivacyMode;

        _ui.PrivacyMode = PrivacyMode;
        _ui.Save();
    }

    /// <summary>Göz simgesinin rengi: gizliyken vurgulu (dikkat çeksin), açıkken sönük.</summary>
    public IBrush PrivacyIconBrush => new SolidColorBrush(
        Color.Parse(PrivacyMode ? "#D4AA32" : "#9AA2B1"));

    public string PrivacyLabel => L[PrivacyMode ? "privacy.on" : "privacy.off"];

    /// <summary>Steam adı — gizli moddayken maskelenir.</summary>
    public string DisplayedHostName =>
        PrivacyMode && SteamHostName.Length > 0 ? L["val.masked"] : SteamHostName;

    /// <summary>Port — gizli moddayken maskelenir.</summary>
    public string DisplayedPort => PrivacyMode ? "••••" : PortText;

    /// <summary>Konsola uygulanan bulanıklık. Gizli modda okunamaz hâle getirir.</summary>
    public IEffect? ConsoleEffect => PrivacyMode ? new BlurEffect { Radius = 9 } : null;

    partial void OnPrivacyModeChanged(bool value)
    {
        OnPropertyChanged(nameof(PrivacyIconBrush));
        OnPropertyChanged(nameof(PrivacyLabel));
        OnPropertyChanged(nameof(DisplayedHostName));
        OnPropertyChanged(nameof(DisplayedPort));
        OnPropertyChanged(nameof(DisplayedLocalIp));
        OnPropertyChanged(nameof(DisplayedEndpoint));
        OnPropertyChanged(nameof(ConsoleEffect));
    }

    partial void OnSteamHostNameChanged(string value) => OnPropertyChanged(nameof(DisplayedHostName));

    partial void OnEditSteamChanged(bool value) => OnPropertyChanged(nameof(HostNamePlaceholder));

    partial void OnPortTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedPort));
        OnPropertyChanged(nameof(DisplayedEndpoint));
    }

    partial void OnLocalIpChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedLocalIp));
        OnPropertyChanged(nameof(DisplayedEndpoint));
    }

    private bool CanModifyCampaign() =>
        !IsRunning && !IsBusy && SelectedCampaign is not null
        && SaveSet.IsValidSaveName(CampaignNameInput.Trim())
        && _saves?.Exists(CampaignNameInput.Trim()) != true;

    private bool CanDeleteCampaign() => !IsRunning && !IsBusy && SelectedCampaign is not null;

    // --- karşılama ekranı ------------------------------------------------

    public const int TutorialLastStep = 2;

    public bool TutorialStep0 => TutorialStep == 0;
    public bool TutorialStep1 => TutorialStep == 1;
    public bool TutorialStep2 => TutorialStep == 2;
    public bool TutorialIsLast => TutorialStep == TutorialLastStep;
    public bool TutorialNotLast => !TutorialIsLast;

    partial void OnTutorialStepChanged(int value)
    {
        OnPropertyChanged(nameof(TutorialStep0));
        OnPropertyChanged(nameof(TutorialStep1));
        OnPropertyChanged(nameof(TutorialStep2));
        OnPropertyChanged(nameof(TutorialIsLast));
        OnPropertyChanged(nameof(TutorialNotLast));
    }

    [RelayCommand]
    private void TutorialNext()
    {
        if (TutorialStep < TutorialLastStep) TutorialStep++;
        else CloseTutorial();
    }

    [RelayCommand]
    private void CloseTutorial()
    {
        ShowTutorial = false;

        // Turu sonuna kadar izleyen bir daha görmek istemez.
        if (DontShowTutorialAgain || TutorialIsLast)
        {
            _ui.TutorialDismissed = true;
            _ui.Save();
        }
    }

    [RelayCommand]
    private void ShowWelcome()
    {
        TutorialStep = 0;
        ShowTutorial = true;
    }

    /// <summary>
    /// Şifre, port, otomatik kayıt ve Steam görünürlüğünü <c>server-config.json</c>'a yazar.
    /// </summary>
    /// <remarks>
    /// Sunucu bu dosyayı yalnızca açılışta okuyor, dolayısıyla değişiklikler ancak yeniden
    /// başlatınca geçerli oluyor. Bu yüzden komut yalnızca sunucu duruyorken çalışabiliyor.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanApplySettings))]
    private void ApplySettings()
    {
        try
        {
            ApplyEditedSettings();
            ReloadConfig();
            ShowToast(L["msg.settingsSaved"]);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Düzenleme alanlarındaki değerleri yapılandırmaya yazar.</summary>
    private void ApplyEditedSettings()
    {
        if (_config is null || !_config.Exists) return;

        var port = int.TryParse(EditPort, out var p) ? p : ServerConfig.DefaultPort;
        var autosave = int.TryParse(EditAutosave, out var a) ? a : ServerConfig.DefaultAutosaveMinutes;

        _config.Update(w => w
            .Password(EditPassword)
            .Port(port)
            .AutosaveMinutes(autosave)
            .Steam(EditSteam));
    }

    private bool CanApplySettings() => _config is not null && _config.Exists && !IsRunning && !IsBusy;

    private async Task SendAsync(string command)
    {
        try { await _supervisor.SendCommandAsync(command); }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
    }

    // --- iç işleyiş ------------------------------------------------------

    private void WireSupervisor()
    {
        _supervisor.LineReceived += line => Dispatcher.UIThread.Post(() =>
        {
            TryCaptureSteamHostName(line);
            TryAutoBackup(line);

            if (IsHarmlessStartupNoise(line)) return;
            if (line.Contains(DsEventParser.Marker, StringComparison.Ordinal)) return;
            if (IsToolsDataLine(line)) return;
            AppendConsole(line);
        });

        _supervisor.PhaseChanged += phase => Dispatcher.UIThread.Post(() =>
        {
            Phase = phase;
            if (phase == ServerPhase.Serving)
            {
                BusyMessage = "";
                if (!IsCreatingWorld) ShowToast(L["msg.online"]);
                // Yayına geçer geçmez sunucuya iş yığılmasın: kahramanlar diskteki save'den,
                // sunucu sorguları sayfa açılınca. Eklenti yalnızca yoklanır.
                ReloadHeroes();
                if (CurrentPage is AppPage.Players or AppPage.Cheats) _ = RefreshHeroesAsync();
                _ = DetectServerToolsAsync();
            }

            RaiseRunStateChanged();
        });

        _supervisor.PlayersChanged += players => Dispatcher.UIThread.Post(() =>
        {
            var joined = players.Where(p => _allPlayers.All(o => o.Id != p.Id)).ToList();

            var left = _allPlayers.Where(o => players.All(p => p.Id != o.Id)).ToList();

            _allPlayers.Clear();
            _allPlayers.AddRange(players);
            ApplyPlayerFilter();
            UpdateHeroFlags();
            RaiseHomeChanged();

            foreach (var p in left) PlayerLeft(p.Name);

            foreach (var p in joined) OnPlayerJoined(p);
        });

        _supervisor.Exited += info => Dispatcher.UIThread.Post(async () =>
        {
            AppendConsole("— " + ServerExit.Describe(info.Kind));

            Phase = ServerPhase.Unknown;
            BusyMessage = "";
            SteamHostName = "";

            foreach (var p in _allPlayers) PlayerLeft(p.Name);
            _allPlayers.Clear();
            ApplyPlayerFilter();
            UpdateHeroFlags();

            RaiseRunStateChanged();
            ReloadCampaigns();
            ReloadBackups();

            // Sözleşme değişmiş ya da onay silinmiş: yeniden sor.
            if (info.Kind == ServerExitKind.EulaRequired)
            {
                _ui.AcceptedEulaFingerprint = null;
                _ui.Save();
                await RequireEulaAsync(StartAsync);
                return;
            }

            if (_restartAfterExit && !info.IsCrash)
            {
                _restartAfterExit = false;
                await StartAsync();
                return;
            }

            await HandleCrashAsync(info);
        });
    }

    private bool IsCreatingWorld => WizardCreating;

    /// <summary>
    /// Açılışta basılan "Cannot load: X.dll" satırları zararsızdır: hemen ardından
    /// coop assembly resolver devreye giriyor ve DLL'ler doğru dizinden yükleniyor.
    /// Kullanıcıya hata gibi göstermek her açılışta boş yere panik yaratırdı.
    /// </summary>
    private static bool IsHarmlessStartupNoise(string line) =>
        line.Contains("Cannot load:", StringComparison.Ordinal) ||
        line.Contains("Could not load file or assembly", StringComparison.Ordinal) ||
        line.Contains("Could not find the event index for", StringComparison.Ordinal) ||
        line.Contains("Unable to find item to add dependency", StringComparison.Ordinal);

    private void AppendConsole(string line)
    {
        Console.Add(line);
        while (Console.Count > MaxConsoleLines) Console.RemoveAt(0);
        OnConsoleLineAdded(line);
    }

    /// <summary>Çöküş sonrası politikayı uygular: gerekiyorsa bekleyip yeniden başlatır.</summary>
    private async Task HandleCrashAsync(ServerExitInfo info)
    {
        _crashPolicy.Enabled = AutoRestart;

        var decision = _crashPolicy.Evaluate(info, DateTimeOffset.UtcNow);

        switch (decision.Reason)
        {
            case CrashDecisionReason.NotACrash:
                return;

            case CrashDecisionReason.NotRecoverable:
                AlertText = L["msg.noAutoRestart"] + " " + ServerExit.Describe(info.Kind);
                return;

            case CrashDecisionReason.CrashLoop:
                AlertText = L["msg.crashLoop"];
                return;

            case CrashDecisionReason.Disabled:
                AlertText = L["msg.crashed"];
                return;
        }

        if (!decision.ShouldRestart || _coop is null) return;

        AlertText = L["msg.crashed"] + " " + L["msg.restartingIn"];
        await Task.Delay(decision.Delay);

        // Kullanıcı bu arada elle başlattıysa araya girme.
        if (IsRunning) return;

        try
        {
            LaunchServer();
            BusyMessage = L["msg.readyIn"];
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            BusyMessage = "";
        }
    }

    /// <summary>
    /// Sunucunun Steam listesinde göründüğü adı log satırından yakalar.
    /// </summary>
    /// <remarks>
    /// Kaynak satır:
    /// <c>[DedicatedServer] Steam user session initialized (appid 261550, persona 'ad')</c>
    /// </remarks>
    private void TryCaptureSteamHostName(string line)
    {
        const string marker = "persona '";

        var at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return;

        var start = at + marker.Length;
        var end = line.IndexOf('\'', start);
        if (end > start) SteamHostName = line[start..end];
    }

    private void ApplyPlayerFilter()
    {
        var filter = PlayerFilter.Trim();

        Players.Clear();
        foreach (var p in _allPlayers)
        {
            if (filter.Length == 0 ||
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString().Contains(filter, StringComparison.Ordinal))
            {
                Players.Add(p);
            }
        }

        OnPropertyChanged(nameof(HasPlayers));
        OnPropertyChanged(nameof(FilterHidesAll));
        OnPropertyChanged(nameof(OnlinePlayerCount));
        KickCommand.NotifyCanExecuteChanged();
    }

    private void Detect()
    {
        _game = GameLocator.FindBannerlord();
        _coop = GameLocator.FindCoop();

        GameVersion = _game?.GameVersion ?? "-";
        ModVersion = _coop?.ModVersion ?? "-";
        InstallationFound = _game is not null && _coop is not null;

        VersionsMatch = _game?.GameVersion is { } gv
                        && _coop?.RequiredGameVersion is { } rv
                        && string.Equals(gv, rv, StringComparison.OrdinalIgnoreCase);

        // Kaynak ölçümü, sunucunun kendi klasörü altındaki süreçleri sayar.
        if (_coop is not null)
        {
            var serverRoot = System.IO.Path.GetDirectoryName(_coop.DedicatedServerExe);
            if (serverRoot is not null) _resources = new ServerResourceMonitor(serverRoot);
        }

        // Geliştirme ve test için: gerçek dünyalara dokunmadan ayrı bir klasörle çalışmak.
        DataDirectory = Environment.GetEnvironmentVariable("COOPKEEP_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : CoopInstallation.DefaultDataDirectory;

        var savesDir = System.IO.Path.Combine(DataDirectory, "Game Saves");
        _saves = new SaveRepository(savesDir);
        _backups = new BackupService(savesDir);
        _config = new ServerConfigStore(System.IO.Path.Combine(DataDirectory, "server-config.json"));
        _modConfig = new ModConfigStore(CoopInstallation.DefaultModConfigPath);

        LocalIp = DetectLocalIp();
        CheckModules();

        ReloadConfig();
        ReloadCampaigns();
        ReloadBackups();

        OnPropertyChanged(nameof(GameFound));
        OnPropertyChanged(nameof(CoopFound));
        OnPropertyChanged(nameof(GameCheckText));
        OnPropertyChanged(nameof(ModCheckText));
        RaiseRunStateChanged();
    }

    /// <summary>
    /// İstemcide seçili modülleri sunucunun yüklediklerine karşı denetler.
    /// </summary>
    /// <remarks>
    /// Fazladan bir modül seçiliyse Coop'un modül doğrulaması bağlantıyı
    /// reddedebiliyor ve oyunun hatası bunu açıkça söylemiyor.
    /// </remarks>
    private void CheckModules()
    {
        var result = ModuleCompatibility.Check();
        ReloadModules(result);

        if (result is null)
        {
            ModuleNotice = L["msg.modulesUnknown"];
            ModuleProblem = false;
            return;
        }

        if (!result.CoopEnabled)
        {
            ModuleNotice = L["msg.coopDisabled"];
            ModuleProblem = true;
            return;
        }

        if (result.ExtraModules.Count > 0)
        {
            ModuleNotice = string.Format(L["msg.modulesExtra"], string.Join(", ", result.ExtraModules));
            ModuleProblem = true;
            return;
        }

        ModuleNotice = L["msg.modulesOk"];
        ModuleProblem = false;
    }

    private void ReloadConfig()
    {
        if (_config is null || !_config.Exists)
        {
            PortText = ServerConfig.DefaultPort.ToString();
            PasswordText = L["val.noPassword"];
            AutosaveText = "-";
            SteamText = "-";
            EditPort = ServerConfig.DefaultPort.ToString();
            EditAutosave = ServerConfig.DefaultAutosaveMinutes.ToString();
            return;
        }

        var cfg = _config.Read();
        ActiveSaveName = cfg.SaveName;

        // Yıldız: anahtar dosyada tanımlı değil, varsayılana düşülüyor.
        PortText = cfg.HasExplicitPort ? cfg.Port.ToString() : cfg.Port + " *";
        PasswordText = cfg.HasPassword ? L["val.hasPassword"] : L["val.noPassword"];
        AutosaveText = cfg.AutosaveEnabled ? $"{cfg.AutosaveMinutes} {L["val.minutes"]}" : L["val.autosaveOff"];
        SteamText = cfg.Steam ? L["val.on"] : L["val.off"];

        // Düzenleme alanlarını dosyadaki güncel değerlerle doldur.
        EditPassword = cfg.Password;
        EditPort = cfg.Port.ToString();
        EditAutosave = cfg.AutosaveMinutes.ToString();
        EditSteam = cfg.Steam;
    }

    private void ReloadCampaigns()
    {
        if (_saves is null) return;

        var previous = SelectedCampaign?.Name;

        Campaigns.Clear();
        foreach (var s in _saves.DiscoverCampaigns()) Campaigns.Add(s);

        OnPropertyChanged(nameof(HasCampaigns));

        _suppressSelectionWrite = true;
        try
        {
            SelectedCampaign =
                Campaigns.FirstOrDefault(c => c.Name == previous)
                ?? Campaigns.FirstOrDefault(c => c.Name == ActiveSaveName)
                ?? Campaigns.FirstOrDefault();
        }
        finally
        {
            _suppressSelectionWrite = false;
        }

        RaiseHomeChanged();
    }

    private void RaiseRunStateChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsServing));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Uptime));
        OnPropertyChanged(nameof(Step1Brush));
        OnPropertyChanged(nameof(Step2Brush));
        OnPropertyChanged(nameof(Step3Brush));
        RaiseHomeChanged();

        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SaveNowCommand.NotifyCanExecuteChanged();
        SaveAndBackupCommand.NotifyCanExecuteChanged();
        AnnounceCommand.NotifyCanExecuteChanged();
        KickCommand.NotifyCanExecuteChanged();
        RestartWithCountdownCommand.NotifyCanExecuteChanged();
        OpenWizardCommand.NotifyCanExecuteChanged();
        ApplySettingsCommand.NotifyCanExecuteChanged();
        RenameCampaignCommand.NotifyCanExecuteChanged();
        DuplicateCampaignCommand.NotifyCanExecuteChanged();
        RequestDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
        RequestRestoreCommand.NotifyCanExecuteChanged();
        ConfirmRestoreCommand.NotifyCanExecuteChanged();
        BackupNowCommand.NotifyCanExecuteChanged();
        NotifyAdminCommands();
    }

    partial void OnPhaseChanged(ServerPhase value) => RaiseRunStateChanged();

    partial void OnPlayerFilterChanged(string value) => ApplyPlayerFilter();

    partial void OnCampaignNameInputChanged(string value)
    {
        RenameCampaignCommand.NotifyCanExecuteChanged();
        DuplicateCampaignCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Kampanya seçimi anında <c>server-config.json</c>'a yazılır.
    /// </summary>
    /// <remarks>
    /// Seçtiğin kampanya <b>açılacak olandır</b>. Sunucu çalışırken yazmıyoruz: çalışan bir
    /// sunucunun save'ini değiştirmek ancak yeniden başlatınca geçerli olur ve yanıltıcı olurdu.
    /// </remarks>
    partial void OnSelectedCampaignChanged(SaveSet? value)
    {
        PendingDelete = false;

        if (value is not null && !IsRunning && !_suppressSelectionWrite && _config is not null)
        {
            try
            {
                _config.EnsureSaveName(value.Name);
                ReloadConfig();
            }
            catch (Exception ex) { AppendConsole("! " + ex.Message); }
        }

        LoadWorldEditor();
        if (!IsRunning) ReloadHeroes();

        StartCommand.NotifyCanExecuteChanged();
        RenameCampaignCommand.NotifyCanExecuteChanged();
        DuplicateCampaignCommand.NotifyCanExecuteChanged();
        RequestDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();

        PendingRestore = false;
        ReloadBackups();
        BackupNowCommand.NotifyCanExecuteChanged();
        RaiseHomeChanged();
    }

    partial void OnSelectedBackupChanged(BackupEntry? value)
    {
        RequestRestoreCommand.NotifyCanExecuteChanged();
        ConfirmRestoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnAutoRestartChanged(bool value)
    {
        _crashPolicy.Enabled = value;
        _ui.AutoRestart = value;
        _ui.Save();
    }

    partial void OnAutoBackupChanged(bool value)
    {
        _ui.AutoBackup = value;
        _ui.Save();
    }

    partial void OnSelectedPlayerChanged(ConnectedPlayer? value) => KickCommand.NotifyCanExecuteChanged();

    partial void OnAnnounceTextChanged(string value) => AnnounceCommand.NotifyCanExecuteChanged();

    partial void OnBusyMessageChanged(string value) => RaiseRunStateChanged();

    public async ValueTask DisposeAsync()
    {
        _uptimeTimer.Stop();
        await _supervisor.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
