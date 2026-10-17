using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using CoopKeep.App.ViewModels;

namespace CoopKeep.App.Views.Pages;

public partial class ModsPage : UserControl
{
    private MainViewModel? _vm;

    public ModsPage() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>Sayfa değişince listenin başı görünsün (düğmeler en altta kalıyor).</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ModsPage) || Scroller.Content is not Visual content) return;

        var top = ListCard.TranslatePoint(new Point(0, 0), content)?.Y ?? 0;
        if (Scroller.Offset.Y > top) Scroller.Offset = new Vector(Scroller.Offset.X, Math.Max(0, top - 12));
    }
}
