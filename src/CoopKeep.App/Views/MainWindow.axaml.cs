using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CoopKeep.App.Localization;
using CoopKeep.App.ViewModels;

namespace CoopKeep.App.Views;

public partial class MainWindow : Window
{
    private bool _shutdownStarted;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => HookConsoleAutoScroll();
        Loc.Current.PropertyChanged += OnLanguageChanged;
        Closing += OnClosingAsync;
        Closed += (_, _) => Loc.Current.PropertyChanged -= OnLanguageChanged;
    }

    /// <summary>
    /// Pencere kapatılırken çalışan sunucuyu güvenle durdurur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bunu yapmazsak sunucu süreci arkada yetim kalıyor. CoopKeep kendi başlatmadığı
    /// bir sunucuya sonradan bağlanamıyor, dolayısıyla kullanıcının onu durdurmak için
    /// tek yolu Görev Yöneticisi'nden öldürmek olurdu — ve zorla öldürmek save
    /// bozulmasına yol açabiliyor.
    /// </para>
    /// <para>
    /// Kapanış iptal edilip önce <c>stop</c> gönderiliyor (dünya kaydediliyor),
    /// ardından pencere gerçekten kapatılıyor.
    /// </para>
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
    /// <para>
    /// Arayüzdeki metinler <c>{Binding L[anahtar]}</c> ile, yani bir indeksleyici
    /// üzerinden bağlanıyor. İndeksleyici bağlamalarının değişiklik bildirimiyle
    /// tazelenmesi ("Item[]") güvenilir çalışmadı — dil değiştiğinde metinlerin çoğu
    /// eski dilde kalıyordu.
    /// </para>
    /// <para>
    /// Bu yüzden kesin çözüm uygulanıyor: <see cref="StyledElement.DataContext"/> bir an
    /// için boşaltılıp geri veriliyor. Bu, penceredeki her bağlamayı sıfırdan kurar.
    /// Küçük bir pencere için maliyeti ihmal edilebilir ve sonucu garantidir.
    /// </para>
    /// </remarks>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Loc.Language)) return;

        var context = DataContext;
        if (context is null) return;

        DataContext = null;
        DataContext = context;
    }

    /// <summary>
    /// Konsola yeni satır geldikçe otomatik olarak en alta kaydırır.
    /// </summary>
    /// <remarks>
    /// Kaydırma bir sonraki düzen geçişine erteleniyor: satır eklendiği anda
    /// ScrollViewer'ın kapsamı henüz büyümemiş oluyor ve hemen kaydırmak
    /// bir satır geride kalıyor.
    /// </remarks>
    private void HookConsoleAutoScroll()
    {
        if (DataContext is not MainViewModel vm) return;

        vm.Console.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            Dispatcher.UIThread.Post(
                () => this.FindControl<ScrollViewer>("ConsoleScroll")?.ScrollToEnd(),
                DispatcherPriority.Background);
        };
    }
}
