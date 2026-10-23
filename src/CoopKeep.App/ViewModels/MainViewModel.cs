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

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>Konsolun sınırsız büyümesini engelleyen üst sınır.</summary>
    private const int MaxConsoleLines = 2000;

    /// <summary>Coop'un özel sunucusu (<c>BannerlordCoopServer.exe</c>).</summary>
    private readonly ServerSupervisor _supervisor = new();
    private readonly DispatcherTimer _uptimeTimer;
    private readonly Settings.UiSettings _ui = Settings.UiSettings.Load();

    /// <summary>Coop modu ve özel sunucusu (Steam Atölyesi'nden).</summary>
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

    /// <summary>En son başlatılan dünya; çöküş sonrası bu açılır.</summary>
    private string? _lastLaunchedSave;

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
        _ui.AdvancedMode ??= _ui.ToolsExpanded; // eski kurulum: Araçlar açıktıysa gelişmiş görünüm
        ToolsExpanded = _ui.ToolsExpanded;

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

        BuildThemeOptions();
        WireSupervisor();
        StartConsoleFlush();
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
        StartPlayingCommand.NotifyCanExecuteChanged();
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
            if (HasPublicAddress) lines.Add(string.Format(L["invite.text.public"], PublicAddress));
            if (EditPassword.Length > 0) lines.Add(string.Format(L["invite.text.password"], EditPassword));

            // Modlu dünyada arkadaşlar aynı modları kurmalı (Coop farkı reddediyor).
            var world = IsRunning ? ActiveSaveName : SelectedCampaign?.Name;
            var mods = world is { Length: > 0 } ? WorldModIds(world).ToList() : [];
            lines.Add(mods.Count > 0 ? string.Format(L["invite.text.modded"], string.Join(", ", mods)) : L["invite.text.mods"]);
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

    private static IBrush StepBrush(bool done, bool active) =>
        Theming.ThemeManager.Brush(done ? "Accent" : active ? "AccentDim" : "BgRaised");

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
    private async void SampleResources()
    {
        if (_resources is null || !IsRunning)
        {
            if (CpuText != "-" && !_sampling) { CpuText = "-"; RamText = "-"; _resources?.Reset(); }
            return;
        }

        // Ölçüm tüm süreçleri dolaşıyor; UI thread'inde her 2 saniyede bir takılma yapıyordu.
        if (_sampling) return;
        _sampling = true;

        // Kapalıyken Normal'e döndürülür; açıkken "normalin üstü" (Yüksek değil: oyunun ve sistemin önüne geçmesin).
        _resources.DesiredPriority = HighPriority
            ? System.Diagnostics.ProcessPriorityClass.AboveNormal
            : System.Diagnostics.ProcessPriorityClass.Normal;

        var monitor = _resources;
        var pid = _supervisor.ProcessId;
        ServerResources sample;
        try { sample = await Task.Run(() => monitor.Sample(pid)); }
        catch (Exception) { return; }
        finally { _sampling = false; }

        if (!IsRunning) return;

        CpuText = sample.ProcessCount == 0 ? "-" : $"{sample.CpuPercent:0}%";
        RamText = sample.ProcessCount == 0 ? "-" : $"{sample.MemoryGigabytes:0.0} GB";

        MemoryWarning = MemoryWarningGb > 0 && sample.MemoryGigabytes > MemoryWarningGb
            ? string.Format(L["msg.memoryHigh"], sample.MemoryGigabytes.ToString("0.0"), MemoryWarningGb)
            : "";
    }

    private bool _sampling;

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
        BuildThemeOptions();
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

        // Kodda hesaplanan yeni metinler: kurulum denetimi, Coop durumu, mod etiketleri.
        RefreshSetupChecks();
        OnCoopPageOpened();
        if (!ModsDirty && ManagedMods.Count > 0) LoadManagedMods();
    }

    [RelayCommand]
    private void Refresh()
    {
        Detect();
        CheckForForeignServer();
    }

    /// <summary>Sunucuyu başlatır (oyun açılmaz; sunucu kendi penceresiz programında çalışır).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => StartWorldAsync(joinWhenReady: false);

    /// <summary>Sunucuyu başlatır; açılınca oyunu Coop ve dünyanın modlarıyla açar.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartPlayingAsync() => StartWorldAsync(joinWhenReady: true);

    /// <summary>Sunucu yayına geçince oyun açılsın mı ("Başlat ve oyna").</summary>
    private bool _joinWhenServing;

    private async Task StartWorldAsync(bool joinWhenReady)
    {
        if (_coop is null || _config is null || SelectedCampaign is null) return;

        // İlk başlatmada sunucunun lisans sözleşmesi gösterilir; onaydan sonra devam edilir.
        if (!EulaAccepted)
        {
            await RequireEulaAsync(() => StartWorldAsync(joinWhenReady));
            return;
        }

        // Coop'un bilinen tuzakları: açmadan önce söylenir (bkz. CoopPreflight).
        if (!await ConfirmPreflightAsync(SelectedCampaign.Name)) return;

        var gameOpened = false;
        try
        {
            BusyMessage = L["msg.readyIn"];
            AlertText = "";

            // Elle başlatma yeni bir sayfa açar: önceki çöküşler otomatik yeniden
            // başlatma sayacına sayılmasın.
            _crashPolicy.Reset();
            _restartAfterExit = false;

            // "Başlat ve oyna": önce oyun açılır, sunucu arkada hazırlanır. Tersi sırada oyuncu
            // sunucuyu (30-60 sn) bekleyip sonra oyunun açılmasını da bekliyordu; ikisi aynı
            // anda yüklenince bilgisayar da zorlanıyordu (kullanıcı isteği, 2026-10-23).
            _joinWhenServing = joinWhenReady;
            if (joinWhenReady && !ModuleCompatibility.IsGameRunning(null))
                gameOpened = LaunchGameForWorld(SelectedCampaign.Name, L["start.gameFirst"]);
            PrepareAndLaunch(SelectedCampaign.Name);
            CurrentPage = AppPage.Home;
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            // Oyun önce açıldıysa kullanıcı neden sunucunun gelmediğini anlasın.
            ShowToast(gameOpened ? string.Format(L["start.serverFailedGameOpen"], ex.Message) : ex.Message, ToastKind.Error);
            _joinWhenServing = false;
            BusyMessage = "";
        }
    }

    /// <summary>
    /// Ayarları diske ve sunucunun okuduğu dosyalara yazıp sunucuyu başlatır.
    /// </summary>
    /// <remarks>
    /// Hem elle başlatma hem çöküş sonrası yeniden başlatma buradan geçer. Eskiden çöküş
    /// sonrası yol ayarları uygulamadan açıyordu; çalışırken kaydedilen ayarlar
    /// ("yeniden başlatınca geçerli") o yolda hiç uygulanmıyordu.
    /// </remarks>
    private void PrepareAndLaunch(string saveName)
    {
        if (_config is null) return;

        // Ekrandaki ayarları başlatmadan ÖNCE uygula: ekranda ne yazıyorsa sunucu
        // onunla açılır. SIRA ÖNEMLİ: önce yapılandırma var olmalı, sonra ayarlar yazılır;
        // ters sırada ilk kurulumda kullanıcının yazdığı şifre kayboluyordu.
        _config.EnsureSaveName(saveName);
        ApplyEditedSettings();

        // Dünyanın kendi oyun ayarları (zorluk, kurallar) — Coop'un ayar dosyasına yazılır.
        // Kaydedilmemiş düzenleme önce kaydedilir; yoksa yeniden başlatınca kayboluyordu.
        SaveWorldEditorFor(saveName);
        ApplyWorldProfile(saveName);

        ReloadConfig(force: true);
        _lastLaunchedSave = saveName;
        LaunchServer();
    }

    /// <summary>
    /// Coop'un sunucusunu açar. Modsuz dünyada Coop'un kendi başlatıcısıyla; modlu dünyada
    /// modlar sunucunun modül klasörüne kurulur ve motor o modlarla başlatılır.
    /// </summary>
    /// <remarks>
    /// Modlu açılış Coop'un hiçbir denetimini atlamaz (sözleşme ve Coop modülünün parmak izi
    /// motorun içinde de denetleniyor). Coop'un kendi klasörüne dokunulmaz; modlar CoopKeep'in
    /// kurduğu ayrı klasörlere kopyalanır (bkz. <see cref="ServerModDeployer"/>).
    /// </remarks>
    private void LaunchServer()
    {
        if (_coop is null || _config is null) return;

        Console.Clear();
        ClearConsoleSummary();
        EnsureServerTools();

        var cfg = _config.Read();
        var engineModules = DeployWorldMods(cfg.SaveName ?? "");

        _supervisor.Start(new ServerLaunchOptions
        {
            ExecutablePath = _coop.DedicatedServerExe,
            DataDirectory = DataDirectory,
            SteamBanFile = System.IO.Path.Combine(DataDirectory, Core.Server.SteamBanList.FileName),
            AcceptEula = EulaAccepted,
            EngineModules = engineModules,
        });

        RaiseRunStateChanged();
    }

    /// <summary>
    /// Modlu dünyada modları sunucuya kurar ve motorun modül listesini döner; modsuz dünyada
    /// önceden kurulmuş modları kaldırır ve <c>null</c> döner (Coop'un kendi başlatıcısı).
    /// </summary>
    private IReadOnlyList<string>? DeployWorldMods(string worldName)
    {
        if (DedicatedServerDir is not { } ds) return null;
        var deployer = new ServerModDeployer(System.IO.Path.Combine(ds, "engine", "Modules"));
        var mods = worldName.Length > 0 ? ModsForWorld(worldName) : [];

        if (mods.Count == 0)
        {
            if (deployer.Deployed.Count > 0) deployer.RemoveAll();
            return null;
        }

        var plan = ModPlanner.Plan(mods, FreshGameModules(), _coop?.ModuleId ?? "Coop");
        if (!plan.CanLaunch)
            throw new InvalidOperationException(string.Format(L["mods.server.missing"],
                DescribeMissing(plan.MissingMods.Concat(plan.MissingDependencies.Select(d => d.Dependency)))));

        var result = deployer.Deploy(plan.Mods);
        if (result.Installed.Count > 0) AppendConsole("— " + string.Format(L["mods.server.installed"], string.Join(", ", result.Installed)));
        if (result.Removed.Count > 0) AppendConsole("— " + string.Format(L["mods.server.removed"], string.Join(", ", result.Removed)));
        return plan.ModuleOrder;
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
            new InvalidOperationException(DescribeExit(i.Kind)));

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

    /// <summary>Yeni dünyayı Coop sunucusunun boş dünya şablonundan (<c>default_new_game.sav</c>) kopyalar.</summary>
    private bool CreateWorldFromTemplate(string name)
    {
        if (_coop is null || _config is null || _saves is null) return false;

        var serverDir = System.IO.Path.GetDirectoryName(_coop.DedicatedServerExe)!;
        var created = _saves.CreateFromTemplate(name,
        [
            System.IO.Path.Combine(serverDir, "server-data", "Game Saves", SaveRepository.TemplateFileName),
            System.IO.Path.Combine(serverDir, "engine", "bin", "Win64_Shipping_Server", SaveRepository.TemplateFileName),
        ]);
        if (created is null) return false;

        _config.EnsureSaveName(name);
        ApplyEditedSettings();
        ReloadConfig();
        ReloadCampaigns();
        SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == name) ?? SelectedCampaign;
        return true;
    }

    [RelayCommand]
    private void OpenSavesFolder() =>
        OpenPath(System.IO.Path.Combine(DataDirectory, "Game Saves"));

    [RelayCommand]
    private void OpenAuthorPage() => OpenPath(AuthorUrl);

    /// <summary>Coop modunun Workshop sayfası — kurulum eksikse ilk adım.</summary>
    [RelayCommand]
    /// <summary>
    /// CoopKeep modunun indirme sayfası. Atölye sayfası yayınlanınca oraya çevrilecek; o
    /// zamana kadar sürümlerin yayınlandığı yer.
    /// </summary>
    private void OpenWorkshopPage() => OpenPath("https://github.com/Aderimo/bannerlord-coopkeep/releases");

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
    public string AuthorLinkText => "gitgit.me/aderimo";

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

        // Geçersiz değer sessizce varsayılana çevrilmesin: kullanıcı yazdığının
        // uygulandığını sanıp sonra "ayarlar sıfırlandı" diyordu.
        if (!int.TryParse(EditPort, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException(L["msg.invalidPort"]);
        if (!int.TryParse(EditAutosave, out var autosave) || autosave < 0)
            throw new InvalidOperationException(L["msg.invalidAutosave"]);
        _config.Update(w => w
            .Password(EditPassword)
            .Port(port)
            .AutosaveMinutes(autosave)
            .Steam(EditSteam));

        _editsDirty = false;
    }

    private bool CanApplySettings() => _config is not null && _config.Exists && !IsRunning && !IsBusy;

    private async Task SendAsync(string command)
    {
        try { await _supervisor.SendCommandAsync(command); }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
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
        RaiseJourneyChanged();
        KickCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Coop güncellendiyse ana sayfada gösterilen not; yoksa boş.</summary>
    [ObservableProperty] private string _coopUpdatedNotice = "";

    public bool HasCoopUpdatedNotice => CoopUpdatedNotice.Length > 0;

    partial void OnCoopUpdatedNoticeChanged(string value) => OnPropertyChanged(nameof(HasCoopUpdatedNotice));

    /// <summary>
    /// Coop Atölye'de güncellenince söyler: Coop sürüm farkında bağlantıyı reddediyor, herkesin
    /// (ve sunucu paketinin) aynı sürümde olması gerekiyor.
    /// </summary>
    private void NoticeCoopUpdate()
    {
        if (_coop?.ModVersion is not { Length: > 0 } now) return;
        if (_ui.LastCoopVersion is { Length: > 0 } before && !string.Equals(before, now, StringComparison.OrdinalIgnoreCase))
            CoopUpdatedNotice = string.Format(L["msg.coopUpdated"], before, now);
        if (_ui.LastCoopVersion == now) return;
        _ui.LastCoopVersion = now;
        _ui.Save();
    }

    [RelayCommand]
    private void DismissCoopUpdated() => CoopUpdatedNotice = "";

    private void Detect()
    {
        _game = GameLocator.FindBannerlord();
        _coop = GameLocator.FindCoop(_game?.InstallDirectory);

        GameVersion = _game?.GameVersion ?? "-";
        ModVersion = _coop?.ModVersion ?? "-";
        InstallationFound = _game is not null && _coop is not null;
        NoticeCoopUpdate();

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
        RefreshSetupChecks();

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

        // Coop'un kapalı olması ve reddettikleri kurulum denetiminde söyleniyor. Burada yalnızca
        // başlatıcıdaki modlarla dünyanın modları arasındaki fark: oyunu başlatıcıdan açan biri
        // farklı modlarla girer ve Coop reddeder. ("Oyuna gir" her zaman doğru modlarla açar.)
        var world = IsRunning ? ActiveSaveName : SelectedCampaign?.Name;
        var worldMods = world is { Length: > 0 } ? WorldModIds(world).ToList() : [];
        var userOn = result.All
            .Where(m => m.IsSelected && !ModuleCompatibility.ServerModules.Contains(m.Id) && m.Status != ModuleStatus.Blocked &&
                        !m.Id.StartsWith("Coop", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToList();
        var extra = userOn.Where(id => !worldMods.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var missing = worldMods.Where(id => !userOn.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();

        var parts = new List<string>();
        if (extra.Count > 0) parts.Add(string.Format(L["msg.modsExtraForWorld"], string.Join(", ", extra.Select(NameFor))));
        if (missing.Count > 0) parts.Add(string.Format(L["msg.modsMissingForWorld"], string.Join(", ", missing.Select(NameFor))));
        ModuleProblem = parts.Count > 0;
        ModuleNotice = ModuleProblem ? string.Join(" ", parts) + " " + L["msg.modsPlayHint"] : L["msg.modulesOk"];
    }

    /// <summary>Ana sayfadaki mod uyarısı yalnızca bir dünya seçiliyken ya da sunucu açıkken.</summary>
    public bool ShowModuleBanner => ModuleProblem && (HomeReady || HomeOnline);

    /// <summary>Ayarlar sayfasında henüz uygulanmamış düzenleme var mı?</summary>
    private bool _editsDirty;
    private bool _loadingEdits;

    partial void OnEditPasswordChanged(string value) => MarkEditsDirty();
    partial void OnEditPortChanged(string value) => MarkEditsDirty();
    partial void OnEditAutosaveChanged(string value) => MarkEditsDirty();

    private void MarkEditsDirty()
    {
        if (!_loadingEdits) _editsDirty = true;
    }

    /// <summary>Ekrandaki değerleri dosyadan tazeler.</summary>
    /// <param name="force">
    /// false iken kullanıcının henüz uygulamadığı düzenlemeler korunur: dil değişimi ya da
    /// dünya seçimi gibi yan etkiler yazılanı silmesin.
    /// </param>
    private void ReloadConfig(bool force = false)
    {
        if (force) _editsDirty = false;

        if (_config is null || !_config.Exists)
        {
            PortText = ServerConfig.DefaultPort.ToString();
            PasswordText = L["val.noPassword"];
            AutosaveText = "-";
            SteamText = "-";
            if (!_editsDirty)
                LoadEdits(EditPassword, ServerConfig.DefaultPort, ServerConfig.DefaultAutosaveMinutes, EditSteam);
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
        if (!_editsDirty) LoadEdits(cfg.Password, cfg.Port, cfg.AutosaveMinutes, cfg.Steam);
    }

    private void LoadEdits(string password, int port, int autosave, bool steam)
    {
        _loadingEdits = true;
        try
        {
            EditPassword = password;
            EditPort = port.ToString();
            EditAutosave = autosave.ToString();
            EditSteam = steam;

        }
        finally
        {
            _loadingEdits = false;
        }
    }

    private void ReloadCampaigns()
    {
        if (_saves is null) return;

        var previous = SelectedCampaign?.Name;
        var previousModsWorld = ModsWorld?.Name;

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

        // Modlar sayfasındaki sunucu dünyası liste yenilenince boşa düşmesin.
        ModsWorld = Campaigns.FirstOrDefault(c => c.Name == previousModsWorld) ?? SelectedCampaign;

        RaiseHomeChanged();
    }

    private void RaiseRunStateChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsServing));
        OnPropertyChanged(nameof(ShowJoinOwn));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Uptime));
        OnPropertyChanged(nameof(Step1Brush));
        OnPropertyChanged(nameof(Step2Brush));
        OnPropertyChanged(nameof(Step3Brush));
        RaiseHomeChanged();

        StartCommand.NotifyCanExecuteChanged();
        StartPlayingCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        JoinOwnServerCommand.NotifyCanExecuteChanged();
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
        SendConsoleInputCommand.NotifyCanExecuteChanged();
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
        CheckModules(); // ana sayfa mod uyarısı seçili dünyaya göre

        if (value is not null && !IsRunning && !_suppressSelectionWrite && _config is not null)
        {
            try
            {
                _config.EnsureSaveName(value.Name);
                ReloadConfig();
            }
            catch (Exception ex) { AppendConsole("! " + ex.Message); }
        }

        // Aynı dünya yeniden seçildiyse (sunucu kapanınca liste tazelenir, kaydın boyutu
        // değiştiği için seçim "değişmiş" görünür) kaydedilmemiş düzenlemeyi silme.
        var sameWorld = string.Equals(value?.Name, _editorWorldName, StringComparison.OrdinalIgnoreCase);
        if (!(sameWorld && WorldEditor.IsDirty)) LoadWorldEditor();
        if (!IsRunning) ReloadHeroes();

        StartCommand.NotifyCanExecuteChanged();
        StartPlayingCommand.NotifyCanExecuteChanged();
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
