using Avalonia.Controls;
using Avalonia.Interactivity;
using MirageBox.Oasis.Desktop.ViewModels;

namespace MirageBox.Oasis.Desktop.Views;

public record DataSourceResult(string Name, string Plugin);

public partial class AddDataSourceDialog : Window
{
    public AddDataSourceDialog()
    {
        InitializeComponent();
        PopulatePlugins();
    }

    private void PopulatePlugins()
    {
        foreach (var option in PluginCatalog.All)
            PluginCombo.Items.Add(new ComboBoxItem { Content = option.Display, Tag = option.Id });
        PluginCombo.Items.Add(new ComboBoxItem { Content = "Custom plugin...", Tag = "custom" });
        PluginCombo.SelectedIndex = 0;
    }

    private void OnPluginSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CustomPluginPanel == null) return;
        if (PluginCombo?.SelectedItem is not ComboBoxItem item) return;

        var tag = item.Tag as string;
        CustomPluginPanel.IsVisible = tag == "custom";

        if (ElevationWarning != null)
        {
            var option = tag != null && tag != "custom" ? PluginCatalog.Find(tag) : null;
            ElevationWarning.IsVisible = option is { RequiresElevation: true } && !PluginCatalog.IsElevated;
        }
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
