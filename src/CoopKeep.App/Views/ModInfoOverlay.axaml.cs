using Avalonia.Controls;
using Avalonia.Input;
using CoopKeep.App.ViewModels;

namespace CoopKeep.App.Views;

public partial class ModInfoOverlay : UserControl
{
    public ModInfoOverlay()
    {
        InitializeComponent();

        // Paneli değil, yalnızca koyu zemini tıklamak kapatır.
        Backdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, Backdrop) && DataContext is MainViewModel vm) vm.CloseModInfoCommand.Execute(null);
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is MainViewModel vm)
        {
            vm.CloseModInfoCommand.Execute(null);
            e.Handled = true;
        }
    }
}
