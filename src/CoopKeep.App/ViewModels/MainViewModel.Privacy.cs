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

/// <summary>Yayın modu: adres, şifre ve konsolun gizlenmesi.</summary>
public partial class MainViewModel
{
    // --- gizlilik --------------------------------------------------------

    [RelayCommand]
    private void TogglePrivacy()
    {
        PrivacyMode = !PrivacyMode;

        _ui.PrivacyMode = PrivacyMode;
        _ui.Save();
    }

    /// <summary>Göz simgesinin rengi: gizliyken vurgulu (dikkat çeksin), açıkken sönük.</summary>
    public IBrush PrivacyIconBrush => Theming.ThemeManager.Brush(PrivacyMode ? "Accent" : "TextDim");

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

    partial void OnEditSteamChanged(bool value)
    {
        OnPropertyChanged(nameof(HostNamePlaceholder));
        MarkEditsDirty();
    }

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
}
