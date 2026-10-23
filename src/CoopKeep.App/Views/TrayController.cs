using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CoopKeep.App.Localization;
using CoopKeep.App.ViewModels;

namespace CoopKeep.App.Views;

/// <summary>
/// Sistem tepsisi simgesi ve oyuncu katılınca uyarı.
/// </summary>
/// <remarks>
/// Sunucuyu açıp oyun oynayan kişi CoopKeep'i arkada bırakıyor. Tepsideki simge sunucunun
/// durumunu ve oyuncu sayısını gösterir; menüsünden pencere açılır ya da sunucu durdurulur.
/// Pencere küçültülünce kendiliğinden gizlenmez: görev çubuğundan kaybolan program "kapandı"
/// sanılıyordu.
/// </remarks>
internal sealed class TrayController
{
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly TrayIcon _tray;

    public TrayController(Application app, Window window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CoopKeep/Assets/coopkeep.ico"))),
            ToolTipText = "CoopKeep",
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(app, [_tray]);

        BuildMenu();
        UpdateTooltip();
        vm.PropertyChanged += OnViewModelChanged;
        Loc.Current.PropertyChanged += (_, _) => { BuildMenu(); UpdateTooltip(); };
        vm.PlayerJoinedAlert += _ => Dispatcher.UIThread.Post(AlertIfInBackground);
        window.Closed += (_, _) => _tray.IsVisible = false;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.OnlinePlayerCount) or nameof(MainViewModel.IsRunning))
        {
            UpdateTooltip();
            if (e.PropertyName == nameof(MainViewModel.IsRunning)) BuildMenu();
        }
    }

    private void UpdateTooltip()
    {
        var text = $"CoopKeep — {_vm.StatusText}";
        if (_vm.IsRunning) text += " · " + string.Format(Loc.Current["tray.players"], _vm.OnlinePlayerCount);
        _tray.ToolTipText = text;
    }

    private void BuildMenu()
    {
        var show = new NativeMenuItem(Loc.Current["tray.show"]);
        show.Click += (_, _) => ShowWindow();

        var stop = new NativeMenuItem(Loc.Current["tray.stop"]) { IsEnabled = _vm.IsRunning };
        stop.Click += (_, _) => { if (_vm.StopCommand.CanExecute(null)) _vm.StopCommand.Execute(null); };

        // Çıkış pencerenin kendi kapanışından geçer: açık sunucu önce güvenle durdurulur.
        var quit = new NativeMenuItem(Loc.Current["tray.quit"]);
        quit.Click += (_, _) => _window.Close();

        _tray.Menu = [show, stop, new NativeMenuItemSeparator(), quit];
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Pencere arkadaysa kısa bir ses ve görev çubuğunda yanıp sönme.</summary>
    private void AlertIfInBackground()
    {
        if (_window.IsActive || !OperatingSystem.IsWindows()) return;
        MessageBeep(MbIconAsterisk);
        if (_window.TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero)
        {
            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Window = handle,
                Flags = FlashAll | FlashUntilForeground,
                Count = 0,
                Timeout = 0,
            };
            FlashWindowEx(ref info);
        }
    }

    private const uint MbIconAsterisk = 0x40;
    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
