using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class DataSourceEntryViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _plugin;

    public DataSourceEntryViewModel(string name, string plugin)
    {
        _name = name;
        _plugin = plugin;
    }
}

public partial class ManageDataSourcesViewModel : ViewModelBase
{
    private readonly Dictionary<string, DataSourceConfig> _dataSources;

    public ObservableCollection<DataSourceEntryViewModel> Sources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private DataSourceEntryViewModel? _selectedSource;

    public bool HasSelection => SelectedSource != null;

    public static List<string> PluginOptions { get; } =
    [
        "__builtin:clock",
        "__builtin:counter",
        "__builtin:timer",
        "__builtin:static",
    ];

    public ManageDataSourcesViewModel(Dictionary<string, DataSourceConfig> dataSources)
    {
        _dataSources = dataSources;
        foreach (var (name, cfg) in dataSources)
            Sources.Add(new DataSourceEntryViewModel(name, cfg.Plugin));
    }

    [RelayCommand]
    private void Add()
    {
        var i = 1;
        while (Sources.Any(s => s.Name == $"source{i}")) i++;
        var name = $"source{i}";
        var entry = new DataSourceEntryViewModel(name, "__builtin:clock");
        Sources.Add(entry);
        SelectedSource = entry;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedSource == null) return;
        var idx = Sources.IndexOf(SelectedSource);
        Sources.Remove(SelectedSource);
        SelectedSource = Sources.Count > 0 ? Sources[Math.Min(idx, Sources.Count - 1)] : null;
    }

    public void SaveAll()
    {
        _dataSources.Clear();
        foreach (var entry in Sources)
            _dataSources[entry.Name] = new DataSourceConfig { Plugin = entry.Plugin };
    }
}
