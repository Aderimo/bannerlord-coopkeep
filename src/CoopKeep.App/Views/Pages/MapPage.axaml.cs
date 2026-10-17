using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CoopKeep.App.ViewModels;

namespace CoopKeep.App.Views.Pages;

/// <summary>
/// Harita: fare tekerleğiyle yakınlaştırma, sürükleyerek kaydırma.
/// </summary>
public partial class MapPage : UserControl
{
    private Point? _dragFrom;
    private Vector _dragOffset;

    public MapPage()
    {
        InitializeComponent();

        MapScroll.AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MapScroll.AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MapScroll.PointerMoved += OnMoved;
        MapScroll.PointerReleased += (_, _) => _dragFrom = null;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Tekerlek imlecin altındaki noktaya doğru yakınlaştırır.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Vm is not { } vm) return;

        var before = vm.MapZoom;
        var after = e.Delta.Y > 0 ? before * 1.15 : before / 1.15;
        after = System.Math.Clamp(after, 0.35, 4);
        if (System.Math.Abs(after - before) < 0.0001) return;

        var pointer = e.GetPosition(MapScroll);
        var content = (MapScroll.Offset + pointer) / before;
        vm.MapZoom = after;
        MapScroll.UpdateLayout();
        MapScroll.Offset = content * after - pointer;
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(MapScroll).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(MapScroll);
        _dragOffset = MapScroll.Offset;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from) return;
        if (!e.GetCurrentPoint(MapScroll).Properties.IsLeftButtonPressed) { _dragFrom = null; return; }

        var delta = e.GetPosition(MapScroll) - from;
        if (System.Math.Abs(delta.X) + System.Math.Abs(delta.Y) < 3) return;
        MapScroll.Offset = _dragOffset - delta;
    }
}
