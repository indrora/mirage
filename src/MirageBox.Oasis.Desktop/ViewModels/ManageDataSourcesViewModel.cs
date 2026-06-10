using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class DataSourceEntryViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;

    private string _plugin;

    /// <summary>
    /// Plugin id. Ignores null/empty writes: the editor ComboBox pushes null
    /// through its two-way SelectedValue binding during dialog teardown.
    /// </summary>
    public string Plugin
    {
        get => _plugin;
        set
        {
            if (string.IsNullOrEmpty(value) || value == _plugin) return;
            _plugin = value;
            OnPropertyChanged(nameof(Plugin));
            OnPropertyChanged(nameof(ElevationMarker));
            OnPropertyChanged(nameof(ShowElevationWarning));
        }
    }

    /// <summary>Plugin configuration values (round-tripped from config.json).</summary>
    public Dictionary<string, JsonElement> Config { get; set; }

    public DataSourceEntryViewModel(string name, string plugin, Dictionary<string, JsonElement>? config = null)
    {
        _name = name;
        _plugin = plugin;
        Config = config != null ? new Dictionary<string, JsonElement>(config) : new();
    }

    private bool NeedsElevation => PluginCatalog.Find(Plugin)?.RequiresElevation == true;

    public string ElevationMarker => NeedsElevation ? "🛡" : "";

    public bool ShowElevationWarning => NeedsElevation && !PluginCatalog.IsElevated;
}

public partial class ManageDataSourcesViewModel : ViewModelBase
{
    private readonly Dictionary<string, DataSourceConfig> _dataSources;
    private readonly Func<string, IDataSource?>? _liveSourceResolver;

    public ObservableCollection<DataSourceEntryViewModel> Sources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private DataSourceEntryViewModel? _selectedSource;

    public bool HasSelection => SelectedSource != null;

    public static IReadOnlyList<PluginOption> PluginOptions => PluginCatalog.All;

    public ObservableCollection<SourceParamViewModel> SourceParams { get; } = new();
    public bool HasSourceParams => SourceParams.Count > 0;

    public ManageDataSourcesViewModel(
        Dictionary<string, DataSourceConfig> dataSources,
        Func<string, IDataSource?>? liveSourceResolver = null)
    {
        _dataSources = dataSources;
        _liveSourceResolver = liveSourceResolver;
        foreach (var (name, cfg) in dataSources)
            Sources.Add(new DataSourceEntryViewModel(name, cfg.Plugin, cfg.Config));
    }

    partial void OnSelectedSourceChanged(DataSourceEntryViewModel? oldValue, DataSourceEntryViewModel? newValue)
    {
        if (oldValue != null)
        {
            SaveParams(oldValue);
            oldValue.PropertyChanged -= OnEntryPropertyChanged;
        }
        if (newValue != null)
            newValue.PropertyChanged += OnEntryPropertyChanged;

        RebuildSourceParams();
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataSourceEntryViewModel.Plugin) && sender == SelectedSource)
            RebuildSourceParams();
    }

    private void RebuildSourceParams()
    {
        SourceParams.Clear();
        OnPropertyChanged(nameof(HasSourceParams));

        var entry = SelectedSource;
        if (entry == null || string.IsNullOrEmpty(entry.Plugin)) return;

        // Cheap reflection check before instantiating anything: only sources
        // that opt into configuration get a transient probe.
        var type = PluginLoader.ResolveType(entry.Plugin);
        if (type == null || !typeof(IConfigurableSource).IsAssignableFrom(type)) return;

        IReadOnlyList<SourceParamInfo> infos;
        try
        {
            infos = DescribeParameters(entry);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sources] Could not read parameters of '{entry.Plugin}': {ex.Message}");
            return;
        }

        foreach (var info in infos)
        {
            string? current = null;
            if (entry.Config.TryGetValue(info.Key, out var elem))
                current = elem.ValueKind == JsonValueKind.String ? elem.GetString() : elem.GetRawText();

            var param = new SourceParamViewModel(info, current);
            // Sync edits into the entry immediately — the dialog can be torn
            // down (clearing these VMs) before any save hook runs.
            param.PropertyChanged += (_, _) => SaveParam(entry, param);
            foreach (var choice in param.Choices)
                choice.PropertyChanged += (_, _) => SaveParam(entry, param);
            SourceParams.Add(param);
        }
        OnPropertyChanged(nameof(HasSourceParams));
    }

    private static void SaveParam(DataSourceEntryViewModel entry, SourceParamViewModel param)
    {
        // Choice combos push null through SelectedValue during dialog teardown;
        // a real selection is never null, so ignore it rather than clear the key.
        if (param.Kind == SourceParamKind.Choice && string.IsNullOrEmpty(param.Value)) return;

        var value = param.ToConfigValue();
        if (value != null)
            entry.Config[param.Key] = JsonSerializer.SerializeToElement(value);
        else
            entry.Config.Remove(param.Key);
    }

    private IReadOnlyList<SourceParamInfo> DescribeParameters(DataSourceEntryViewModel entry)
    {
        // Prefer the live instance from the running engine.
        if (_liveSourceResolver?.Invoke(entry.Name) is IConfigurableSource live)
            return live.DescribeParameters();

        // Transient probe: create, start, describe, tear down.
        var transient = PluginLoader.Create(entry.Plugin);
        if (transient is not IConfigurableSource configurable) { transient?.Dispose(); return []; }
        try
        {
            var config = entry.Config.ToDictionary(
                kv => kv.Key,
                object? (kv) => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : kv.Value.GetRawText());
            transient.InitializeAsync(config).GetAwaiter().GetResult();
            transient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            return configurable.DescribeParameters();
        }
        finally
        {
            transient.StopAsync().GetAwaiter().GetResult();
            transient.Dispose();
        }
    }

    private void SaveParams(DataSourceEntryViewModel entry)
    {
        foreach (var param in SourceParams)
            SaveParam(entry, param);
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
        if (SelectedSource != null)
            SaveParams(SelectedSource);

        _dataSources.Clear();
        foreach (var entry in Sources)
            _dataSources[entry.Name] = new DataSourceConfig
            {
                Plugin = entry.Plugin,
                Config = entry.Config.Count > 0 ? entry.Config : null,
            };
    }
}
