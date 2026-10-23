using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Server;
using CoopKeep.Core.Viewer;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// İzleyici modu: sunucudaki diğer oyuncular kendi CoopKeep'leriyle haritayı görür.
/// </summary>
/// <remarks>
/// <para>
/// Ev sahibi Ayarlar'dan "Haritayı paylaş"ı açar; CoopKeep salt okunur bir uç nokta açar
/// (<see cref="ViewerServer"/>). Oyuncu karşılama ekranında "Bir sunucuyu izle"yi seçip
/// adresi ve kodu girer; yalnızca Harita (ve Ayarlar) açılır, hiçbir yönetim komutu yoktur.
/// </para>
/// <para>
/// Oyun portu gibi bu portun da modemde açık olması gerekir (aynı ağda ya da VPN'de
/// gerekmez).
/// </para>
/// </remarks>
public partial class MainViewModel
{
    // --- ev sahibi --------------------------------------------------------

    private ViewerServer? _viewerServer;

    [ObservableProperty] private bool _shareMap;
    [ObservableProperty] private string _sharePortText = "";
    [ObservableProperty] private string _shareCode = "";
    [ObservableProperty] private string _shareStatus = "";

    private int DefaultSharePort =>
        (int.TryParse(PortText.Replace(" *", ""), out var p) ? p : Core.Configuration.ServerConfig.DefaultPort) + 1;

    public string ShareAddressText => PrivacyMode
        ? L["val.masked"]
        : $"{LocalIp}:{SharePortText}  ·  {L["viewer.code"]}: {ShareCode}";

    public string ShareInviteText => string.Format(L["viewer.inviteText"], LocalIp, SharePortText, ShareCode);

    private void InitViewerSettings()
    {
        ShareCode = _ui.ShareCode is { Length: >= 6 } c ? c : ViewerServer.NewCode();
        SharePortText = (_ui.SharePort > 0 ? _ui.SharePort : DefaultSharePort).ToString();
        ViewerHost = _ui.ViewerHost ?? "";
        ViewerPortText = _ui.ViewerPort > 0 ? _ui.ViewerPort.ToString() : "";
        ViewerCode = _ui.ViewerCode ?? "";
        if (_ui.ShareMap) ShareMap = true;
    }

    partial void OnShareMapChanged(bool value)
    {
        _ui.ShareMap = value;
        _ui.Save();
        _ = value ? StartViewerServerAsync() : StopViewerServerAsync();
    }

    private async Task StartViewerServerAsync()
    {
        await StopViewerServerAsync();
        if (!int.TryParse(SharePortText, out var port) || port is < 1024 or > 65535)
        {
            ShareStatus = L["viewer.badPort"];
            return;
        }

        try
        {
            _ui.ShareCode = ShareCode;
            _ui.SharePort = port;
            _ui.Save();

            _viewerServer = new ViewerServer(port, ShareCode);
            _viewerServer.Start();
            ShareStatus = string.Format(L["viewer.sharing"], port);
            LoadMap();
            _mapTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(4), Avalonia.Threading.DispatcherPriority.Background,
                (_, _) => _ = RefreshMapAsync());
            _mapTimer.Start();
        }
        catch (SocketException)
        {
            _viewerServer = null;
            ShareStatus = string.Format(L["viewer.portBusy"], port);
        }

        OnPropertyChanged(nameof(ShareAddressText));
    }

    private async Task StopViewerServerAsync()
    {
        if (_viewerServer is { } server)
        {
            _viewerServer = null;
            await server.DisposeAsync();
        }

        ShareStatus = "";
    }

    [RelayCommand]
    private async Task NewShareCodeAsync()
    {
        ShareCode = ViewerServer.NewCode();
        _ui.ShareCode = ShareCode;
        _ui.Save();
        if (ShareMap) await StartViewerServerAsync();
        OnPropertyChanged(nameof(ShareAddressText));
    }

    [RelayCommand]
    private async Task CopyShareInviteAsync()
    {
        await CopyTextAsync(ShareInviteText);
        ShowToast(L["msg.copied"], ToastKind.Success);
    }

    private void PublishSnapshot()
    {
        if (_viewerServer is null) return;
        _viewerServer.Publish(BuildSnapshot());
        var viewers = _viewerServer.RecentViewers;
        ShareStatus = viewers > 0
            ? string.Format(L["viewer.viewers"], viewers)
            : string.Format(L["viewer.sharing"], _viewerServer.Port);
    }

    // --- izleyici ---------------------------------------------------------

    [ObservableProperty] private bool _isViewerMode;
    [ObservableProperty] private bool _viewerConnectOpen;
    [ObservableProperty] private string _viewerHost = "";
    [ObservableProperty] private string _viewerPortText = "";
    [ObservableProperty] private string _viewerCode = "";
    [ObservableProperty] private string _viewerError = "";

    private readonly HttpClient _viewerHttp = new() { Timeout = TimeSpan.FromSeconds(6) };
    private int _viewerFailures;

    [RelayCommand]
    private void OpenViewerConnect()
    {
        ViewerError = "";
        ShowTutorial = false;
        ViewerConnectOpen = true;
    }

    [RelayCommand]
    private void CloseViewerConnect() => ViewerConnectOpen = false;

    [RelayCommand]
    private async Task ConnectViewerAsync()
    {
        ViewerError = "";
        if (string.IsNullOrWhiteSpace(ViewerHost) || !int.TryParse(ViewerPortText, out var port) || string.IsNullOrWhiteSpace(ViewerCode))
        {
            ViewerError = L["viewer.fillAll"];
            return;
        }

        try
        {
            var snap = await new ViewerClient(_viewerHttp).FetchAsync(ViewerHost, port, ViewerCode);
            if (snap is null) { ViewerError = L["viewer.failed"]; return; }

            _ui.ViewerHost = ViewerHost.Trim();
            _ui.ViewerPort = port;
            _ui.ViewerCode = ViewerCode.Trim().ToUpperInvariant();
            _ui.Save();

            IsViewerMode = true;
            ViewerConnectOpen = false;
            CurrentPage = AppPage.Map;
            LoadMap();
            ApplySnapshot(snap);
            MapStatus = string.Format(L["viewer.connected"], snap.World ?? ViewerHost);
        }
        catch (UnauthorizedAccessException)
        {
            ViewerError = L["viewer.badCode"];
        }
        catch (Exception)
        {
            ViewerError = L["viewer.failed"];
        }
    }

    [RelayCommand]
    private void LeaveViewer()
    {
        IsViewerMode = false;
        MapPlayers.Clear();
        _mapTimer?.Stop();
        CurrentPage = AppPage.Home;
    }

    private async Task RefreshFromHostAsync()
    {
        if (!int.TryParse(ViewerPortText, out var port)) return;
        _mapRefreshing = true;
        try
        {
            var snap = await new ViewerClient(_viewerHttp).FetchAsync(ViewerHost, port, ViewerCode);
            if (snap is null) return;

            ApplySnapshot(snap);
            _viewerFailures = 0;
            MapStatus = string.Format(L["viewer.connected"], snap.World ?? ViewerHost) + " · " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception)
        {
            if (++_viewerFailures >= 3) MapStatus = L["viewer.lost"];
        }
        finally
        {
            _mapRefreshing = false;
        }
    }

    partial void OnIsViewerModeChanged(bool value)
    {
        // İzleyicide yalnızca harita ve ansiklopedi var; ikisi de "Araçlar" altında.
        if (value) ToolsExpanded = true;
        RaiseRunStateChanged();
    }
}
