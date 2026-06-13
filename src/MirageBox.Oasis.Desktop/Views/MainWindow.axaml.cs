using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        Opened += async (_, _) => await OfferSimulatorIfNoDevices();
        Closing += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.ShutdownAsync();
        };
    }

    /// <summary>
    /// A config with zero devices leaves the editor unusable (nothing to select, no slots).
    /// Attached hardware is adopted automatically by the view model; this covers the
    /// remaining case — no config entries AND no hardware — by offering a simulator so a
    /// first run always lands somewhere usable. Also re-checked after Reset, which wipes
    /// simulators.
    /// </summary>
    private async Task OfferSimulatorIfNoDevices()
    {
        if (DataContext is not MainWindowViewModel vm || vm.Devices.Count > 0) return;

        var confirmed = await ConfirmDialog.ShowAsync(this,
            "No devices found",
            "No control surface hardware was detected and no devices are configured. " +
            "Add a simulator device to get started?");
        if (confirmed)
            vm.AddDeviceCommand.Execute(null);
    }

    private void OnSlotTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ButtonSlotViewModel slot }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedSlot = slot;
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
        await dialog.ShowAsync();
        
        dialogVm.SaveAll();
        vm.RefreshGaugeNames(dialogVm.RenamedGauges);
    }

    private async void OnManageDataSources(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dialogVm = vm.CreateManageDataSourcesViewModel();
        var dialog = new ManageDataSourcesDialog { DataContext = dialogVm };
        await dialog.ShowAsync();
        dialogVm.SaveAll();
        vm.RefreshDataSourceNames();
        await vm.ApplyDataSourceChangesAsync();
    }

    private async void OnExportConfig(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export configuration",
            SuggestedFileName = "oasis-config.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is string path)
            await vm.ExportConfigAsync(path);
    }

    private async void OnImportConfig(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Import configuration",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
            await vm.ImportConfigAsync(path);
    }

    private async void OnResetConfig(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var confirmed = await ConfirmDialog.ShowAsync(this,
            "Reset configuration?",
            "This wipes all gauges, data sources, scenes and simulators back to a default layout. " +
            "Physical hardware is kept. Nothing is written to disk until you Save.");
        if (confirmed)
        {
            await vm.ResetConfigCommand.ExecuteAsync(null);
            await OfferSimulatorIfNoDevices();
        }
    }
}
