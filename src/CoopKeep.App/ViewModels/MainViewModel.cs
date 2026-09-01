using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => OnPropertyChanged(nameof(Uptime));
        _uptimeTimer.Start();

        WireSupervisor();
        Detect();
    }

    public Loc L => Loc.Current;

    // --- kurulum ---------------------------------------------------------

    [ObservableProperty] private string _gameVersion = "-";
    [ObservableProperty] private string _modVersion = "-";
    [ObservableProperty] private bool _installationFound;
    [ObservableProperty] private bool _versionsMatch;
    [ObservableProperty] private string _dataDirectory = "";

    // --- sunucu durumu ---------------------------------------------------

    [ObservableProperty] private ServerPhase _phase = ServerPhase.Unknown;
    [ObservableProperty] private string _activeSaveName = "-";
    [ObservableProperty] private string _portText = "-";
    [ObservableProperty] private string _passwordText = "-";
    [ObservableProperty] private string _autosaveText = "-";
    [ObservableProperty] private string _steamText = "-";
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _announceText = "";

    /// <summary>
    /// Kampanya adı kutusu — yeni dünya yaratma, yeniden adlandırma ve kopyalamada
    /// hedef ad olarak kullanılır.
    /// </summary>
    [ObservableProperty] private string _campaignNameInput = "";

    /// <summary>Silme onayı bekleniyor mu? (iki adımlı silme)</summary>
    [ObservableProperty] private bool _pendingDelete;

    // --- karşılama ekranı ---

    [ObservableProperty] private bool _showTutorial;
    [ObservableProperty] private bool _dontShowTutorialAgain;

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
    [ObservableProperty] private string _settingsNotice = "";

    // --- oyuncu arama ---

    [ObservableProperty] private string _playerFilter = "";

    // --- yedekleme ve çökme kurtarma ---

    [ObservableProperty] private bool _autoBackup = true;
    [ObservableProperty] private bool _autoRestart = true;
    [ObservableProperty] private string _backupNotice = "";
    [ObservableProperty] private BackupEntry? _selectedBackup;
    [ObservableProperty] private bool _pendingRestore;

    public ObservableCollection<BackupEntry> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

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
        _ => "#6B7280",
    }));

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

        // Bağlama zincirinin kökünü tazele. {Binding L[anahtar]} ifadeleri önce L'yi,
        // sonra indeksleyiciyi okur; kökü bildirmek tüm zincirin yeniden
        // değerlendirilmesini garanti eder.
        OnPropertyChanged(nameof(L));

        // C# tarafında hesaplanan metinler indeksleyici bağlaması kullanmıyor,
        // bu yüzden ayrıca tazelenmeleri gerekiyor.
        OnPropertyChanged(nameof(StatusText));
        ReloadConfig();
    }

    [RelayCommand]
    private void Refresh()
    {
        ReloadConfig();
        ReloadCampaigns();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (_coop is null || _config is null || SelectedCampaign is null) return;

        try
        {
            BusyMessage = L["msg.readyIn"];
            _config.EnsureSaveName(SelectedCampaign.Name);
            ReloadConfig();

            Console.Clear();
            _supervisor.Start(new ServerLaunchOptions
            {
                ExecutablePath = _coop.DedicatedServerExe,
                DataDirectory = DataDirectory,
            });

            RaiseRunStateChanged();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            BusyMessage = "";
        }
    }

    private bool CanStart() => InstallationFound && !IsRunning && SelectedCampaign is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        BusyMessage = L["status.stopping"];
        await _supervisor.StopAsync();
        BusyMessage = "";
        RaiseRunStateChanged();
    }

    private bool CanStop() => IsRunning && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCommand))]
    private async Task SaveNowAsync() => await SendAsync(ServerCommand.Save());

    [RelayCommand(CanExecute = nameof(CanAnnounce))]
    private async Task AnnounceAsync()
    {
        await SendAsync(ServerCommand.Say(AnnounceText));
        AnnounceText = "";
    }

    private bool CanAnnounce() => CanCommand() && !string.IsNullOrWhiteSpace(AnnounceText);

    [RelayCommand(CanExecute = nameof(CanKick))]
    private async Task KickAsync()
    {
        if (SelectedPlayer is null) return;
        await SendAsync(ServerCommand.Kick(SelectedPlayer.Id.ToString()));
    }

    private bool CanKick() => CanCommand() && SelectedPlayer is not null;

    private bool CanCommand() => IsServing && !IsBusy;

    /// <summary>
    /// Yeni bir sandbox dünyası yaratır — oyuna hiç girmeden.
    /// </summary>
    /// <remarks>
    /// Server, yapılandırılan save diskte yoksa <c>default_new_game.sav</c>'dan taze bir
    /// dünya kuruyor. Adı önceden yazıp başlatıyor, <c>serving</c> fazını bekleyip
    /// <c>stop</c> ile kaydettiriyoruz. Bu sayede modun kurulum talimatındaki
    /// "önce oyunda tek oyunculu bir save yarat" adımı tamamen gereksizleşiyor.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCreateWorld))]
    private async Task NewWorldAsync()
    {
        if (_coop is null || _config is null || _saves is null) return;

        var name = CampaignNameInput.Trim();
        if (!SaveSet.IsValidSaveName(name) || _saves.Exists(name)) return;

        var ready = new TaskCompletionSource();
        void OnPhase(ServerPhase p) { if (p == ServerPhase.Serving) ready.TrySetResult(); }
        void OnExit(ServerExitInfo i) => ready.TrySetException(
            new InvalidOperationException(ServerExit.Describe(i.Kind)));

        try
        {
            BusyMessage = L["msg.creating"];
            _config.EnsureSaveName(name);
            Console.Clear();

            _supervisor.PhaseChanged += OnPhase;
            _supervisor.Exited += OnExit;

            _supervisor.Start(new ServerLaunchOptions
            {
                ExecutablePath = _coop.DedicatedServerExe,
                DataDirectory = DataDirectory,
            });

            var timeout = Task.Delay(TimeSpan.FromMinutes(5));
            if (await Task.WhenAny(ready.Task, timeout) == timeout)
                throw new TimeoutException();

            await ready.Task;
            await _supervisor.StopAsync();

            CampaignNameInput = "";
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
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

    private bool CanCreateWorld() =>
        InstallationFound && !IsRunning && !IsBusy && SaveSet.IsValidSaveName(CampaignNameInput.Trim());

    // --- kampanya dosya işlemleri ---------------------------------------
    //
    // Hepsi .sav + .json çiftini birlikte taşır ve yalnızca sunucu duruyorken
    // çalışır: açık bir kampanyanın dosyalarını değiştirmek save bozulmasına yol açar.

    [RelayCommand(CanExecute = nameof(CanModifyCampaign))]
    private void RenameCampaign()
    {
        if (_saves is null || SelectedCampaign is null) return;

        var target = CampaignNameInput.Trim();
        if (!SaveSet.IsValidSaveName(target) || _saves.Exists(target)) return;

        try
        {
            var wasActive = string.Equals(SelectedCampaign.Name, ActiveSaveName, StringComparison.OrdinalIgnoreCase);
            _saves.Rename(SelectedCampaign.Name, target);

            // Aktif kampanyanın adı değiştiyse yapılandırma da güncellenmeli,
            // yoksa sunucu var olmayan bir save aramaya çalışır.
            if (wasActive) _config?.EnsureSaveName(target);

            CampaignNameInput = "";
            ReloadConfig();
            ReloadCampaigns();
            SelectedCampaign = Campaigns.FirstOrDefault(c => c.Name == target) ?? SelectedCampaign;
        }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
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
            CampaignNameInput = "";
            ReloadCampaigns();
        }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
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
            _saves.Delete(SelectedCampaign.Name);
            PendingDelete = false;
            ReloadCampaigns();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            PendingDelete = false;
        }
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        var folder = System.IO.Path.Combine(DataDirectory, "Game Saves");
        if (!System.IO.Directory.Exists(folder)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
    }

    /// <summary>Geliştiricinin sayfasını varsayılan tarayıcıda açar.</summary>
    [RelayCommand]
    private void OpenAuthorPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AuthorUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
    }

    public string AuthorUrl => "https://gitgit.me/aderimo";
    public string AuthorName => "aderimo";

    // --- yedekleme -------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanBackupNow))]
    private void BackupNow()
    {
        if (_backups is null || SelectedCampaign is null) return;

        try
        {
            _backups.Create(SelectedCampaign.Name);
            BackupNotice = L["msg.backupTaken"];
            ReloadBackups();
        }
        catch (Exception ex) { BackupNotice = ex.Message; }
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
            BackupNotice = L["msg.restored"];
            PendingRestore = false;

            ReloadBackups();
            ReloadCampaigns();
        }
        catch (Exception ex)
        {
            BackupNotice = ex.Message;
            PendingRestore = false;
        }
    }

    private bool CanRestore() =>
        _backups is not null && SelectedBackup is not null && !IsRunning && !IsBusy;

    private void ReloadBackups()
    {
        Backups.Clear();

        if (_backups is not null && SelectedCampaign is not null)
        {
            foreach (var b in _backups.List(SelectedCampaign.Name))
                Backups.Add(b);
        }

        OnPropertyChanged(nameof(HasBackups));
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
        if (!AutoBackup) return;
        if (!line.Contains("world saved to", StringComparison.Ordinal)) return;
        if (_backups is null || ActiveSaveName is not { Length: > 0 } name) return;

        try
        {
            _backups.Create(name);

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

    /// <summary>
    /// Göz simgesinin rengi: gizliyken vurgulu (dikkat çeksin), açıkken sönük.
    /// </summary>
    public IBrush PrivacyIconBrush => new SolidColorBrush(
        Color.Parse(PrivacyMode ? "#C9A227" : "#98A0AF"));

    /// <summary>Steam adı — gizli moddayken maskelenir.</summary>
    public string DisplayedHostName =>
        PrivacyMode && SteamHostName.Length > 0 ? L["val.masked"] : SteamHostName;

    /// <summary>Port — gizli moddayken maskelenir.</summary>
    public string DisplayedPort => PrivacyMode ? "••••" : PortText;

    /// <summary>
    /// Konsola uygulanan bulanıklık. Gizli modda okunamaz hâle getirir.
    /// </summary>
    public Avalonia.Media.IEffect? ConsoleEffect =>
        PrivacyMode ? new Avalonia.Media.BlurEffect { Radius = 9 } : null;

    partial void OnPrivacyModeChanged(bool value)
    {
        OnPropertyChanged(nameof(PrivacyIconBrush));
        OnPropertyChanged(nameof(DisplayedHostName));
        OnPropertyChanged(nameof(DisplayedPort));
        OnPropertyChanged(nameof(ConsoleEffect));
    }

    partial void OnSteamHostNameChanged(string value) => OnPropertyChanged(nameof(DisplayedHostName));

    partial void OnPortTextChanged(string value) => OnPropertyChanged(nameof(DisplayedPort));

    private bool CanModifyCampaign() =>
        !IsRunning && !IsBusy && SelectedCampaign is not null
        && SaveSet.IsValidSaveName(CampaignNameInput.Trim());

    private bool CanDeleteCampaign() => !IsRunning && !IsBusy && SelectedCampaign is not null;

    // --- karşılama ekranı ------------------------------------------------

    [RelayCommand]
    private void CloseTutorial()
    {
        ShowTutorial = false;

        if (DontShowTutorialAgain)
        {
            _ui.TutorialDismissed = true;
            _ui.Save();
        }
    }

    /// <summary>
    /// Şifre, port, otomatik kayıt ve Steam görünürlüğünü <c>server-config.json</c>'a yazar.
    /// </summary>
    /// <remarks>
    /// Sunucu bu dosyayı yalnızca açılışta okuyor, dolayısıyla değişiklikler ancak yeniden
    /// başlatınca geçerli oluyor. Bu yüzden komut yalnızca sunucu duruyorken çalışabiliyor —
    /// aksi hâlde kullanıcı ayarı değiştirip hiçbir şey olmadığını görürdü.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanApplySettings))]
    private void ApplySettings()
    {
        if (_config is null) return;

        try
        {
            var port = int.TryParse(EditPort, out var p) ? p : ServerConfig.DefaultPort;
            var autosave = int.TryParse(EditAutosave, out var a) ? a : ServerConfig.DefaultAutosaveMinutes;

            _config.Update(w => w
                .Password(EditPassword)
                .Port(port)
                .AutosaveMinutes(autosave)
                .Steam(EditSteam));

            ReloadConfig();
            SettingsNotice = L["msg.settingsSaved"];
        }
        catch (Exception ex)
        {
            SettingsNotice = ex.Message;
        }
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
            AppendConsole(line);
        });

        _supervisor.PhaseChanged += phase => Dispatcher.UIThread.Post(() =>
        {
            Phase = phase;
            if (phase == ServerPhase.Serving) BusyMessage = "";

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            RaiseRunStateChanged();
        });

        _supervisor.PlayersChanged += players => Dispatcher.UIThread.Post(() =>
        {
            _allPlayers.Clear();
            _allPlayers.AddRange(players);
            ApplyPlayerFilter();
        });

        _supervisor.Exited += info => Dispatcher.UIThread.Post(async () =>
        {
            AppendConsole("— " + ServerExit.Describe(info.Kind));

            Phase = ServerPhase.Unknown;
            BusyMessage = "";
            SteamHostName = "";

            _allPlayers.Clear();
            ApplyPlayerFilter();

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            RaiseRunStateChanged();
            ReloadCampaigns();
            ReloadBackups();

            await HandleCrashAsync(info);
        });
    }

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
    }

    /// <summary>
    /// Çöküş sonrası politikayı uygular: gerekiyorsa bekleyip yeniden başlatır.
    /// </summary>
    private async Task HandleCrashAsync(ServerExitInfo info)
    {
        _crashPolicy.Enabled = AutoRestart;

        var decision = _crashPolicy.Evaluate(info, DateTimeOffset.UtcNow);

        switch (decision.Reason)
        {
            case CrashDecisionReason.NotACrash:
                return;

            case CrashDecisionReason.NotRecoverable:
                BusyMessage = L["msg.noAutoRestart"];
                return;

            case CrashDecisionReason.CrashLoop:
                BusyMessage = L["msg.crashLoop"];
                return;

            case CrashDecisionReason.Disabled:
                BusyMessage = L["msg.crashed"];
                return;
        }

        if (!decision.ShouldRestart || _coop is null) return;

        BusyMessage = L["msg.crashed"] + " " + L["msg.restartingIn"];
        await Task.Delay(decision.Delay);

        // Kullanıcı bu arada elle başlattıysa araya girme.
        if (IsRunning) return;

        try
        {
            _supervisor.Start(new ServerLaunchOptions
            {
                ExecutablePath = _coop.DedicatedServerExe,
                DataDirectory = DataDirectory,
            });

            BusyMessage = L["msg.readyIn"];
            RaiseRunStateChanged();
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

        DataDirectory = CoopInstallation.DefaultDataDirectory;

        var savesDir = System.IO.Path.Combine(DataDirectory, "Game Saves");
        _saves = new SaveRepository(savesDir);
        _backups = new BackupService(savesDir);
        _config = new ServerConfigStore(System.IO.Path.Combine(DataDirectory, "server-config.json"));

        ReloadConfig();
        ReloadCampaigns();
        ReloadBackups();
    }

    private void ReloadConfig()
    {
        if (_config is null || !_config.Exists)
        {
            PortText = ServerConfig.DefaultPort.ToString();
            PasswordText = L["val.noPassword"];
            AutosaveText = "-";
            SteamText = "-";
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
    }

    private void RaiseRunStateChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsServing));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Uptime));

        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SaveNowCommand.NotifyCanExecuteChanged();
        AnnounceCommand.NotifyCanExecuteChanged();
        KickCommand.NotifyCanExecuteChanged();
        NewWorldCommand.NotifyCanExecuteChanged();
        ApplySettingsCommand.NotifyCanExecuteChanged();
        RenameCampaignCommand.NotifyCanExecuteChanged();
        DuplicateCampaignCommand.NotifyCanExecuteChanged();
        RequestDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnPlayerFilterChanged(string value) => ApplyPlayerFilter();

    partial void OnCampaignNameInputChanged(string value)
    {
        NewWorldCommand.NotifyCanExecuteChanged();
        RenameCampaignCommand.NotifyCanExecuteChanged();
        DuplicateCampaignCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Kampanya seçimi anında <c>server-config.json</c>'a yazılır.
    /// </summary>
    /// <remarks>
    /// Önceden seçim yalnızca "Başlat"a basınca uygulanıyordu; kullanıcı listede
    /// gezindiğinde sunucu panelindeki "Aktif kampanya" değişmiyor ve hiçbir şey
    /// olmuyormuş gibi görünüyordu. Artık seçtiğin kampanya <b>açılacak olandır</b>.
    /// Sunucu çalışırken yazmıyoruz: çalışan bir sunucunun save'ini değiştirmek
    /// ancak yeniden başlatınca geçerli olur ve yanıltıcı olurdu.
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

        StartCommand.NotifyCanExecuteChanged();
        RenameCampaignCommand.NotifyCanExecuteChanged();
        DuplicateCampaignCommand.NotifyCanExecuteChanged();
        RequestDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();

        PendingRestore = false;
        ReloadBackups();
        BackupNowCommand.NotifyCanExecuteChanged();
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
