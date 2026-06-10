using Avalonia.Controls;
using Avalonia.Layout;

namespace MirageBox.Oasis.Desktop.Views;

/// <summary>Minimal yes/no confirmation dialog.</summary>
public static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var yes = new Button { Content = "Yes, do it", Padding = new Avalonia.Thickness(16, 6) };
        yes.Classes.Add("accent");
        var no = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(16, 6) };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 16 },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes },
                },
            },
        };

        var result = await dialog.ShowDialog<bool?>(owner);
        return result == true;
    }
}
