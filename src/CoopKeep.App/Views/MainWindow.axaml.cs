using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CoopKeep.App.Localization;
using CoopKeep.App.ViewModels;
using CoopKeep.App.Views.Pages;

namespace CoopKeep.App.Views;

public partial class MainWindow : Window
{
    private bool _shutdownStarted;
    private MainViewModel? _vm;

    /// <summary>
    /// Sayfalar bir kez oluşturulur ve yeniden kullanılır: kaydırma konumu ve form
    /// içerikleri sayfa değişince kaybolmasın.
    /// </summary>
    private readonly Dictionary<AppPage, Control> _pages = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Loc.Current.PropertyChanged += OnLanguageChanged;
        Closing += OnClosingAsync;
        Closed += (_, _) => Loc.Current.PropertyChanged -= OnLanguageChanged;
        // Steam'den abone olup ya da modu elle kurup geri gelince liste tazelensin.
        Activated += (_, _) => _vm?.OnWindowActivated();
    }

    private void Attach(MainViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.ConsoleView.CollectionChanged -= OnConsoleChanged;
        }

        _vm = vm;
        if (vm is null) return;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.ConsoleView.CollectionChanged += OnConsoleChanged;
        ShowPage(vm.CurrentPage);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage) && _vm is not null)
            ShowPage(_vm.CurrentPage);
    }

    /// <summary>Sayfayı geçiş animasyonuyla gösterir.</summary>
    private void ShowPage(AppPage page)
    {
        // Harita en ağır sayfa (binlerce işaret): çıkınca bırakılır, dönünce yeniden kurulur.
        // Yakınlaştırma ve konum MapView'da durduğu için kaybolmaz.
        if (page != AppPage.Map && _pages.Remove(AppPage.Map, out var map) && ReferenceEquals(PageHost.Content, map))
            PageHost.Content = null;

        if (!_pages.TryGetValue(page, out var control))
        {
            control = page switch
            {
                AppPage.Worlds => new WorldsPage(),
                AppPage.Players => new PlayersPage(),
                AppPage.Cheats => new CheatsPage(),
                AppPage.Map => new MapPage(),
                AppPage.Encyclopedia => new EncyclopediaPage(),
                AppPage.Backups => new BackupsPage(),
                AppPage.Mods => new ModsPage(),
                AppPage.Settings => new SettingsPage(),
                AppPage.Guide => new GuidePage(),
                AppPage.Console => new ConsolePage(),
                _ => new HomePage(),
            };
            _pages[page] = control;
        }

        PageHost.Content = control;

        if (page == AppPage.Console) ScrollConsoleToEnd();
    }

    /// <summary>
    /// Pencere kapatılırken çalışan sunucuyu güvenle durdurur.
    /// </summary>
    /// <remarks>
    /// Bunu yapmazsak sunucu süreci arkada yetim kalıyor ve kullanıcının onu durdurmak için
    /// tek yolu Görev Yöneticisi'nden öldürmek olurdu — zorla öldürmek save bozabilir.
    /// Kapanış iptal edilip önce <c>stop</c> gönderiliyor, ardından pencere kapatılıyor.
    /// </remarks>
    private async void OnClosingAsync(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownStarted) return;
        if (DataContext is not MainViewModel vm || !vm.IsRunning) return;

        e.Cancel = true;
        _shutdownStarted = true;

        vm.BusyMessage = Loc.Current["msg.closingServer"];

        try { await vm.DisposeAsync(); }
        catch { /* kapanışta yutulur; aşağıda pencere yine de kapanıyor */ }

        Close();
    }

    /// <summary>
    /// Dil değişince tüm bağlamaları yeniden değerlendirir.
    /// </summary>
    /// <remarks>
    /// İndeksleyici bağlamalarının "Item[]" bildirimiyle tazelenmesi güvenilir çalışmadı.
    /// Kesin çözüm: <see cref="StyledElement.DataContext"/> bir an boşaltılıp geri verilir.
    /// Önbellekteki sayfalar da kendi bağlamalarını yeniden kursun diye atılır.
    /// </remarks>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Loc.Language)) return;

        var context = DataContext;
        if (context is null) return;

        _pages.Clear();
        DataContext = null;
        DataContext = context;
        if (_vm is not null) ShowPage(_vm.CurrentPage);
    }

    /// <summary>
    /// Konsola yeni satır geldikçe otomatik olarak en alta kaydırır.
    /// </summary>
    /// <remarks>
    /// Kaydırma bir sonraki düzen geçişine erteleniyor: satır eklendiği anda
    /// ScrollViewer'ın kapsamı henüz büyümemiş oluyor.
    /// </remarks>
    private void OnConsoleChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (_vm?.CurrentPage != AppPage.Console) return;

        ScrollConsoleToEnd();
    }

    private void ScrollConsoleToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_pages.TryGetValue(AppPage.Console, out var page))
                page.FindControl<ScrollViewer>("ConsoleScroll")?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
