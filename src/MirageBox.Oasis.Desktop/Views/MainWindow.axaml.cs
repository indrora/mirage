using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MirageBox.Oasis.Desktop.ViewModels;

namespace MirageBox.Oasis.Desktop.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<string> SlotIndexFormat = DataFormat.CreateInProcessFormat<string>("mirage/slotIndex");
    private static readonly DataFormat<string> SlotTypeFormat = DataFormat.CreateInProcessFormat<string>("mirage/slotType");

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnSlotDrop);
        AddHandler(DragDrop.DragOverEvent, OnSlotDragOver);
    }

    private void OnSlotTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ButtonSlotViewModel slot }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedSlot = slot;
        }
    }

    private async void OnSlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ButtonSlotViewModel slot }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (DataContext is MainWindowViewModel vm)
            vm.SelectedSlot = slot;

        if (slot.IsEmpty) return;

        var data = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(SlotIndexFormat, slot.Index.ToString());
        item.Set(SlotTypeFormat, slot.SlotType);
        data.Add(item);

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnSlotDragOver(object? sender, DragEventArgs e)
    {
        var target = FindSlotFromEvent(e);
        if (target == null || !e.DataTransfer.Contains(SlotIndexFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var sourceType = e.DataTransfer.TryGetValue(SlotTypeFormat);
        if (sourceType != target.SlotType)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
    }

    private void OnSlotDrop(object? sender, DragEventArgs e)
    {
        var target = FindSlotFromEvent(e);
        if (target == null) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var sourceIndexStr = e.DataTransfer.TryGetValue(SlotIndexFormat);
        var sourceType = e.DataTransfer.TryGetValue(SlotTypeFormat);
        if (sourceType == target.SlotType && int.TryParse(sourceIndexStr, out var sourceIndex))
        {
            vm.SwapSlots(sourceType, sourceIndex, target.Index);
        }
    }

    private static ButtonSlotViewModel? FindSlotFromEvent(RoutedEventArgs e)
    {
        var current = e.Source as Avalonia.Visual;
        while (current != null)
        {
            if (current is Control { DataContext: ButtonSlotViewModel slot })
                return slot;
            current = current.GetVisualParent();
        }
        return null;
    }

    private async void OnManageGauges(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dialogVm = vm.CreateManageGaugesViewModel();
        var dialog = new ManageGaugesDialog { DataContext = dialogVm };
        await dialog.ShowDialog(this);
        dialogVm.SaveAll();
        vm.RefreshGaugeNames();
    }

    private async void OnManageDataSources(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dialogVm = vm.CreateManageDataSourcesViewModel();
        var dialog = new ManageDataSourcesDialog { DataContext = dialogVm };
        await dialog.ShowDialog(this);
        dialogVm.SaveAll();
        vm.RefreshDataSourceNames();
    }
}
