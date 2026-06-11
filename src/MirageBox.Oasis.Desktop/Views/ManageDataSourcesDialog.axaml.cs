using Avalonia.Interactivity;
using AvaloniaDialogs.Views;

namespace MirageBox.Oasis.Desktop.Views;

public partial class ManageDataSourcesDialog : BaseDialog
{
    public ManageDataSourcesDialog()
    {
        InitializeComponent();
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();
}
