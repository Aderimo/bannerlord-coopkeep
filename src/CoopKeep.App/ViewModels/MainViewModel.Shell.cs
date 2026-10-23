using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Protocol;

namespace CoopKeep.App.ViewModels;

/// <summary>Uygulamanın sayfaları.</summary>
public enum AppPage { Home, Worlds, Players, Cheats, Map, Backups, Mods, Settings, Console, Encyclopedia, Guide }

/// <summary>Ana sayfanın gösterdiği "sıradaki adım".</summary>
public enum HomeState
{
    /// <summary>Oyun veya Coop modu bulunamadı.</summary>
    Setup,

    /// <summary>Hiç dünya yok — ilk iş bir dünya oluşturmak.</summary>
    NoWorld,

    /// <summary>Dünya seçili, sunucu kapalı.</summary>
    Ready,

    /// <summary>Açılıyor (motor / dünya yükleniyor).</summary>
    Starting,

    /// <summary>Yayında.</summary>
    Online,

    /// <summary>Kapanıyor.</summary>
    Stopping,
}

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>Ekranın köşesinde beliren, kendiliğinden kaybolan bildirim.</summary>
public sealed partial class Toast(string text, ToastKind kind) : ObservableObject
{
    public string Text { get; } = text;
    public ToastKind Kind { get; } = kind;

    public bool IsSuccess => Kind == ToastKind.Success;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsError => Kind == ToastKind.Error;
    public bool IsInfo => Kind == ToastKind.Info;

    /// <summary>Kaybolma animasyonu için.</summary>
    [ObservableProperty] private bool _isLeaving;
}

/// <summary>Bildirim geçmişindeki bir kayıt (zil menüsü).</summary>
public sealed record ToastRecord(string Text, ToastKind Kind, DateTime At)
{
    public string Time => At.ToString("HH:mm");
    public bool IsError => Kind == ToastKind.Error;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsNormal => Kind is ToastKind.Success or ToastKind.Info;
}

/// <summary>
/// Gezinme, ana sayfa durumu ve bildirimler.
/// </summary>
/// <remarks>
/// Kullanıcı şikayeti: "ne yapacağımı anlamıyorum". Eski arayüz her şeyi tek ekranda
/// gösteriyordu. Artık her sayfanın tek bir işi var ve ana sayfa yalnızca sıradaki
/// adımı gösteriyor.
/// </remarks>
public partial class MainViewModel
{
    // --- gezinme ---------------------------------------------------------

    [ObservableProperty] private AppPage _currentPage = AppPage.Home;

    // RadioButton'lar iki yönlü bağlanabilsin diye her sayfa için bir bool.
    public bool PageHome { get => CurrentPage == AppPage.Home; set { if (value) CurrentPage = AppPage.Home; } }
    public bool PageWorlds { get => CurrentPage == AppPage.Worlds; set { if (value) CurrentPage = AppPage.Worlds; } }
    public bool PagePlayers { get => CurrentPage == AppPage.Players; set { if (value) CurrentPage = AppPage.Players; } }
    public bool PageCheats { get => CurrentPage == AppPage.Cheats; set { if (value) CurrentPage = AppPage.Cheats; } }
    public bool PageMap { get => CurrentPage == AppPage.Map; set { if (value) CurrentPage = AppPage.Map; } }
    public bool PageBackups { get => CurrentPage == AppPage.Backups; set { if (value) CurrentPage = AppPage.Backups; } }
    public bool PageMods { get => CurrentPage == AppPage.Mods; set { if (value) CurrentPage = AppPage.Mods; } }
    public bool PageSettings { get => CurrentPage == AppPage.Settings; set { if (value) CurrentPage = AppPage.Settings; } }
    public bool PageConsole { get => CurrentPage == AppPage.Console; set { if (value) CurrentPage = AppPage.Console; } }
    public bool PageGuide { get => CurrentPage == AppPage.Guide; set { if (value) CurrentPage = AppPage.Guide; } }
    public bool PageEncyclopedia { get => CurrentPage == AppPage.Encyclopedia; set { if (value) CurrentPage = AppPage.Encyclopedia; } }

    /// <summary>Yan menüdeki "Araçlar" grubu açık mı (Hileler, Harita, Ansiklopedi, Konsol).</summary>
    [ObservableProperty] private bool _toolsExpanded;

    partial void OnToolsExpandedChanged(bool value)
    {
        if (_ui.ToolsExpanded == value) return;
        _ui.ToolsExpanded = value;
        _ui.Save();
    }

    private static bool IsToolPage(AppPage page) =>
        page is AppPage.Cheats or AppPage.Map or AppPage.Encyclopedia or AppPage.Console;

    partial void OnCurrentPageChanged(AppPage value)
    {
        // Başka yerden bir araç sayfasına gidilirse grup açılır ki seçili sayfa görünsün.
        if (IsToolPage(value)) ToolsExpanded = true;
        OnPropertyChanged(nameof(PageHome));
        OnPropertyChanged(nameof(PageWorlds));
        OnPropertyChanged(nameof(PagePlayers));
        OnPropertyChanged(nameof(PageCheats));
        OnPropertyChanged(nameof(PageMap));
        OnPropertyChanged(nameof(PageBackups));
        OnPropertyChanged(nameof(PageMods));
        OnPropertyChanged(nameof(PageSettings));
        OnPropertyChanged(nameof(PageConsole));
        OnPropertyChanged(nameof(PageGuide));
        OnPropertyChanged(nameof(PageEncyclopedia));
        if (value == AppPage.Encyclopedia) OnEncyclopediaOpened();
        if (value is AppPage.Home or AppPage.Settings) OnCoopPageOpened();

        if (value == AppPage.Mods)
        {
            CheckModules();
            LoadManagedMods();
        }
        if (value is AppPage.Players or AppPage.Cheats && IsServing) _ = RefreshHeroesAsync();
        if (value == AppPage.Players) ReloadBans();
        if (value == AppPage.Cheats) LoadCheatCatalogs();
        OnMapPageChanged(value == AppPage.Map);
    }

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;

    // --- ana sayfa -------------------------------------------------------

    public HomeState HomeState
    {
        get
        {
            if (!InstallationFound) return HomeState.Setup;
            if (Phase == ServerPhase.Stopping) return HomeState.Stopping;
            if (IsRunning) return Phase == ServerPhase.Serving ? HomeState.Online : HomeState.Starting;
            if (!HasCampaigns) return HomeState.NoWorld;
            return HomeState.Ready;
        }
    }

    public bool HomeSetup => HomeState == HomeState.Setup;

    // Ana sayfanın üstündeki yolculuk: Kurulum → Dünya → Sunucu → Arkadaşlar. Yeni kullanıcı
    // "şu an neredeyim, sırada ne var"ı görür.
    public bool JourneySetupDone => InstallationFound;
    public bool JourneyWorldDone => JourneySetupDone && HasCampaigns && SelectedCampaign is not null;
    public bool JourneyServerDone => JourneyWorldDone && IsServing;
    public bool JourneyFriendsDone => JourneyServerDone && OnlinePlayerCount > 0;
    public bool JourneySetupActive => !JourneySetupDone;
    public bool JourneyWorldActive => JourneySetupDone && !JourneyWorldDone;
    public bool JourneyServerActive => JourneyWorldDone && !JourneyServerDone;
    public bool JourneyFriendsActive => JourneyServerDone && !JourneyFriendsDone;

    private void RaiseJourneyChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(JourneySetupDone), nameof(JourneyWorldDone), nameof(JourneyServerDone), nameof(JourneyFriendsDone),
                     nameof(JourneySetupActive), nameof(JourneyWorldActive), nameof(JourneyServerActive), nameof(JourneyFriendsActive),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Kurulum tamam ama düzeltilecek bir şey var: ana kartın altında gösterilir.</summary>
    public bool ShowSetupProblems => !HomeSetup && HasSetupProblems;
    public bool HomeNoWorld => HomeState == HomeState.NoWorld;
    public bool HomeReady => HomeState == HomeState.Ready;
    public bool HomeStarting => HomeState is HomeState.Starting or HomeState.Stopping;
    public bool HomeOnline => HomeState == HomeState.Online;

    /// <summary>Durum kartının büyük başlığı.</summary>
    public string HeroTitle => L["home.title." + HomeState.ToString().ToLowerInvariant()];

    /// <summary>Başlığın altındaki tek cümlelik açıklama.</summary>
    public string HeroSubtitle => HomeState switch
    {
        HomeState.Ready => string.Format(L["home.sub.ready"], SelectedCampaign?.Name ?? ActiveSaveName),
        HomeState.Online => string.Format(L["home.sub.online"], ActiveSaveName, _allPlayers.Count),
        _ => L["home.sub." + HomeState.ToString().ToLowerInvariant()],
    };

    /// <summary>Açılış adımlarının ilerlemesi (0-100). Animasyonlu çubuğu besler.</summary>
    public double BootProgress => Phase switch
    {
        ServerPhase.Starting => 12,
        ServerPhase.Boot => 35,
        ServerPhase.Loading => 70,
        ServerPhase.Serving => 100,
        _ => IsRunning ? 8 : 0,
    };

    public bool Step1Done => Phase is ServerPhase.Loading or ServerPhase.Serving;
    public bool Step1Active => IsRunning && !Step1Done;
    public bool Step2Done => Phase == ServerPhase.Serving;
    public bool Step2Active => Phase == ServerPhase.Loading;
    public bool Step3Done => Phase == ServerPhase.Serving;

    public int OnlinePlayerCount => _allPlayers.Count;

    /// <summary>
    /// Kenar çubuğundaki dünya adı: çalışırken sunucunun açtığı, değilken açılacak olan.
    /// </summary>
    public string SidebarWorldName => IsRunning ? ActiveSaveName : SelectedCampaign?.Name ?? "—";

    /// <summary>Sunucunun en son kaydettiği an (bu oturumda).</summary>
    [ObservableProperty] private DateTime? _lastSaveAt;

    /// <summary>En son alınan yedeğin zamanı.</summary>
    [ObservableProperty] private DateTime? _lastBackupAt;

    public string LastSaveText => LastSaveAt is { } t ? t.ToString("HH:mm") : "—";

    public string LastBackupText => (LastBackupAt ?? Backups.FirstOrDefault()?.CreatedLocal) is { } t
        ? (t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("dd.MM HH:mm"))
        : "—";

    partial void OnLastSaveAtChanged(DateTime? value) => OnPropertyChanged(nameof(LastSaveText));
    partial void OnLastBackupAtChanged(DateTime? value) => OnPropertyChanged(nameof(LastBackupText));

    /// <summary>"Kaydet ve yedek al": dünyayı kaydeder; kayıt bitince otomatik yedek alınır.</summary>
    [RelayCommand(CanExecute = nameof(CanCommand))]
    private async Task SaveAndBackupAsync()
    {
        await SendAsync(Core.Server.ServerCommand.Save());
        ShowToast(L[AutoBackup ? "msg.saveAndBackup" : "msg.saveRequested"], ToastKind.Info);
    }

    [RelayCommand]
    private void OpenSteamFriends() => OpenPath("steam://open/friends");

    /// <summary>Steam adı henüz bilinmiyorken gösterilen metin: Steam kapalıysa bunu söyler.</summary>
    public string HostNamePlaceholder => L[IsRunning ? "invite.steamWaiting" : "msg.hostNameUnknown"];

    private void RaiseHomeChanged()
    {
        OnPropertyChanged(nameof(HomeState));
        OnPropertyChanged(nameof(HomeSetup));
        OnPropertyChanged(nameof(HomeNoWorld));
        OnPropertyChanged(nameof(HomeReady));
        OnPropertyChanged(nameof(ShowModuleBanner));
        OnPropertyChanged(nameof(HomeStarting));
        OnPropertyChanged(nameof(HomeOnline));
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroSubtitle));
        OnPropertyChanged(nameof(BootProgress));
        OnPropertyChanged(nameof(Step1Done));
        OnPropertyChanged(nameof(Step1Active));
        OnPropertyChanged(nameof(Step2Done));
        OnPropertyChanged(nameof(Step2Active));
        OnPropertyChanged(nameof(Step3Done));
        OnPropertyChanged(nameof(OnlinePlayerCount));
        OnPropertyChanged(nameof(SidebarWorldName));
        RaiseJourneyChanged();
        OnPropertyChanged(nameof(HostNamePlaceholder));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    // --- bildirimler -----------------------------------------------------

    public ObservableCollection<Toast> Toasts { get; } = [];

    /// <summary>
    /// Son bildirimler (en yeni üstte). Bildirim birkaç saniyede kaybolduğu için kaçırılan
    /// mesaj ("gereken modlar açıldı", "abone ol sayfaları açıldı"…) buradan okunur.
    /// </summary>
    public ObservableCollection<ToastRecord> ToastHistory { get; } = [];

    /// <summary>Zil açılmadan gelen bildirim sayısı.</summary>
    [ObservableProperty] private int _unseenToasts;

    public bool HasUnseenToasts => UnseenToasts > 0;
    public bool HasToastHistory => ToastHistory.Count > 0;

    partial void OnUnseenToastsChanged(int value) => OnPropertyChanged(nameof(HasUnseenToasts));

    [RelayCommand]
    private void MarkToastsSeen() => UnseenToasts = 0;

    [RelayCommand]
    private void ClearToastHistory()
    {
        ToastHistory.Clear();
        UnseenToasts = 0;
        OnPropertyChanged(nameof(HasToastHistory));
    }

    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(4.5);

    /// <summary>Köşede kısa bir bildirim gösterir.</summary>
    public void ShowToast(string text, ToastKind kind = ToastKind.Success)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        void Add()
        {
            // Aynı metin zaten ekrandaysa tekrar gösterme.
            if (Toasts.Any(t => t.Text == text && !t.IsLeaving)) return;

            var toast = new Toast(text, kind);
            Toasts.Add(toast);
            ToastHistory.Insert(0, new ToastRecord(text, kind, DateTime.Now));
            while (ToastHistory.Count > 30) ToastHistory.RemoveAt(ToastHistory.Count - 1);
            UnseenToasts++;
            OnPropertyChanged(nameof(HasToastHistory));
            while (Toasts.Count > 4) Toasts.RemoveAt(0);

            _ = RemoveLaterAsync(toast, kind == ToastKind.Error ? ToastLifetime * 2 : ToastLifetime);
        }

        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    private async Task RemoveLaterAsync(Toast toast, TimeSpan after)
    {
        await Task.Delay(after);
        toast.IsLeaving = true;

        // Kayma/solma animasyonunun bitmesine izin ver.
        await Task.Delay(260);
        Toasts.Remove(toast);
    }

    [RelayCommand]
    private void DismissToast(Toast toast) => Toasts.Remove(toast);
}
