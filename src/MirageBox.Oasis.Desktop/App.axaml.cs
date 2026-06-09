using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MirageBox.Oasis.Desktop.ViewModels;
using MirageBox.Oasis.Desktop.Views;

namespace MirageBox.Oasis.Desktop;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow { DataContext = _viewModel };
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            desktop.ShutdownRequested += async (_, _) =>
            {
                if (_viewModel.IsRunning)
                    await _viewModel.StopEngineCommand.ExecuteAsync(null);
            };

            // Auto-start the engine on launch
            _ = _viewModel.StartEngineCommand.ExecuteAsync(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
