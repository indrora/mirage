using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MirageBox.Oasis.Desktop.ViewModels;

namespace MirageBox.Oasis.Desktop.Views;

public partial class ManageGaugesDialog : Window
{
    public ManageGaugesDialog()
    {
        InitializeComponent();
    }

    private async void OnRenameGauge(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ManageGaugesViewModel vm || vm.SelectedName is not { } oldName) return;
                                                                                                                                                                                                                                                                                                                                                                                                                                                                  
        var dialog = new TextInputDialog
        {
            Message = $"Rename gauge '{oldName}'",
            Text = oldName,
            PositiveText = "Rename",
            Validate = vm.ValidateNewName,
        };
        var result = await dialog.ShowAsync("GaugeDialogHost");
        if (result.HasValue && result.Value != oldName)
            vm.RenameSelected(result.Value);
    }

    private void OnResetRendererParam(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RendererParamViewModel param })
            param.ResetToDefault();
    }

    private async void OnBrowseRendererParam(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RendererParamViewModel param }) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = param.Description,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        if (files.Count > 0)
        {
            param.Value = files[0].Path.LocalPath;
        }
    }
}
