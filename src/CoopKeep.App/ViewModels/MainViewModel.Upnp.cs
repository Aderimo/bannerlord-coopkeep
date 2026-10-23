using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Net;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Modemde port açma (UPnP). Doğrudan bağlantıda arkadaşların en çok takıldığı adım; Steam ile
/// katılmada gerekmez. Modem desteklemiyorsa elle açma rehberine yönlendirilir.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Modemin internetteki adresi (UPnP ile alındıysa): davete bu yazılır.</summary>
    [ObservableProperty] private string _publicAddress = "";

    [ObservableProperty] private bool _openingPort;

    public bool HasPublicAddress => PublicAddress.Length > 0;

    partial void OnPublicAddressChanged(string value) => OnPropertyChanged(nameof(HasPublicAddress));

    partial void OnOpeningPortChanged(bool value) => OpenPortCommand.NotifyCanExecuteChanged();

    private bool CanOpenPort() => !OpeningPort;

    /// <summary>Coop'un UDP portunu modemde bu bilgisayara yönlendirir.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPort))]
    private async Task OpenPortAsync()
    {
        var port = _config?.Read().Port ?? 4200;
        OpeningPort = true;
        try
        {
            var gateway = await UpnpGateway.DiscoverAsync(TimeSpan.FromSeconds(4));
            if (gateway is null)
            {
                ShowToast(L["upnp.notFound"], ToastKind.Warning);
                return;
            }

            if (!await gateway.AddPortMappingAsync(port, "UDP", "CoopKeep - Bannerlord Coop"))
            {
                ShowToast(L["upnp.refused"], ToastKind.Warning);
                return;
            }

            var external = await gateway.GetExternalIpAsync();
            PublicAddress = external is null ? "" : $"{external}:{port}";
            ShowToast(external is null
                    ? string.Format(L["upnp.openedNoIp"], port)
                    : string.Format(L["upnp.opened"], port, PrivacyMode ? L["val.masked"] : PublicAddress),
                ToastKind.Success);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            ShowToast(L["upnp.notFound"], ToastKind.Warning);
        }
        finally
        {
            OpeningPort = false;
        }
    }
}
