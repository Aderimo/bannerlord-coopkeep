using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CoopKeep.App.ViewModels;

namespace CoopKeep.App.Views.Pages;

/// <summary>
/// Mod yöneticisi. Kod tarafında yalnızca sürükle-bırak var: satırın tutamağından tutulan mod,
/// bırakıldığı modun yerine taşınır (sıra görünüm modelinde değişir).
/// </summary>
public partial class ModsPage : UserControl
{
    private const string DragFormat = "coopkeep-mod";
    private Border? _dropTarget;

    public ModsPage()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(null));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static T? FindAncestor<T>(object? source, System.Func<T, bool>? filter = null) where T : Visual =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault(v => filter?.Invoke(v) ?? true);

    private static Border? RowOf(object? source) => FindAncestor<Border>(source, b => b.Classes.Contains("modrow"));

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindAncestor<Border>(e.Source, b => b.Classes.Contains("grip")) is null) return;
        if (RowOf(e.Source)?.DataContext is not ManagedMod { CanMove: true } mod) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var data = new DataObject();
        data.Set(DragFormat, mod.Id);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        SetDropTarget(null);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var row = RowOf(e.Source);
        var ok = e.Data.Contains(DragFormat) && row?.DataContext is ManagedMod { CanMove: true };
        e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(ok ? row : null);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (DataContext is not MainViewModel vm || e.Data.Get(DragFormat) is not string id) return;
        if (RowOf(e.Source)?.DataContext is not ManagedMod target) return;
        if (vm.ManagedMods.FirstOrDefault(m => m.Id == id) is { } mod) vm.MoveModTo(mod, target);
    }

    private void SetDropTarget(Border? row)
    {
        if (_dropTarget == row) return;
        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = row;
        _dropTarget?.Classes.Add("dropTarget");
    }
}
