using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MirageBox.Oasis.Desktop.Views;

public record DataSourceResult(string Name, string Plugin);

public partial class AddDataSourceDialog : Window
{
    public AddDataSourceDialog()
    {
        InitializeComponent();
    }

    private void OnPluginSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CustomPluginPanel == null) return;
        if (PluginCombo?.SelectedItem is ComboBoxItem item)
            CustomPluginPanel.IsVisible = item.Tag as string == "custom";
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        string plugin;
        if (PluginCombo.SelectedItem is ComboBoxItem item && item.Tag as string == "custom")
            plugin = CustomPluginBox.Text?.Trim() ?? "";
        else if (PluginCombo.SelectedItem is ComboBoxItem selected)
            plugin = selected.Tag as string ?? "";
        else
            return;

        if (string.IsNullOrEmpty(plugin)) return;

        Close(new DataSourceResult(name, plugin));
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
