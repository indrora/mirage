using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class DataSourceViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _plugin;

    public DataSourceViewModel(string name, DataSourceConfig config)
    {
        _name = name;
        _plugin = config.Plugin;
    }

    public DataSourceConfig ToConfig() => new() { Plugin = Plugin };
}
