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
public enum AppPage { Home, Worlds, Players, Backups, Mods, Settings, Console }

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
    public bool PageBackups { get => CurrentPage == AppPage.Backups; set { if (value) CurrentPage = AppPage.Backups; } }
    public bool PageMods { get => CurrentPage == AppPage.Mods; set { if (value) CurrentPage = AppPage.Mods; } }
    public bool PageSettings { get => CurrentPage == AppPage.Settings; set { if (value) CurrentPage = AppPage.Settings; } }
    public bool PageConsole { get => CurrentPage == AppPage.Console; set { if (value) CurrentPage = AppPage.Console; } }

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(PageHome));
        OnPropertyChanged(nameof(PageWorlds));
        OnPropertyChanged(nameof(PagePlayers));
        OnPropertyChanged(nameof(PageBackups));
        OnPropertyChanged(nameof(PageMods));
        OnPropertyChanged(nameof(PageSettings));
        OnPropertyChanged(nameof(PageConsole));

        if (value == AppPage.Mods) CheckModules();
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

    /// <summary>Steam adı henüz bilinmiyorken gösterilen metin: Steam kapalıysa bunu söyler.</summary>
    public string HostNamePlaceholder => L[!EditSteam ? "invite.steamOff" : IsServing ? "invite.steamWaiting" : "msg.hostNameUnknown"];

    private void RaiseHomeChanged()
    {
        OnPropertyChanged(nameof(HomeState));
        OnPropertyChanged(nameof(HomeSetup));
        OnPropertyChanged(nameof(HomeNoWorld));
        OnPropertyChanged(nameof(HomeReady));
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
        OnPropertyChanged(nameof(HostNamePlaceholder));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    // --- bildirimler -----------------------------------------------------

    public ObservableCollection<Toast> Toasts { get; } = [];

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
