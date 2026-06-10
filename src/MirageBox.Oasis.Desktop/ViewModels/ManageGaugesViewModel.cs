using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;
using MirageBox.Oasis.Core.Engine;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class ManageGaugesViewModel : ViewModelBase
{
    private readonly Dictionary<string, GaugeConfig> _gauges;
    private readonly Dictionary<string, DataSourceConfig> _dataSources;
    private readonly RendererRegistry _rendererRegistry;

    public ObservableCollection<string> Names { get; } = new();
    public ObservableCollection<string> DataSourceNames { get; }
    public List<string> RendererTypes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string? _selectedName;

    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _sensor = "";
    [ObservableProperty] private string _rendererType = "FullRing";
    [ObservableProperty] private string? _label;
    [ObservableProperty] private float _min;
    [ObservableProperty] private float _max = 100;
    [ObservableProperty] private string? _theme;

    public ObservableCollection<RendererParamViewModel> RendererParams { get; } = new();
    public bool HasRendererParams => RendererParams.Count > 0;

    /// <summary>
    /// Sensor path suggestions for the currently selected data source.
    /// Populated from [Sensor] attributes on the plugin type.
    /// </summary>
    public ObservableCollection<string> SensorSuggestions { get; } = new();

    public bool HasSelection => SelectedName != null;

    public ManageGaugesViewModel(
        Dictionary<string, GaugeConfig> gauges,
        Dictionary<string, DataSourceConfig> dataSources,
        IEnumerable<string> dataSourceNames,
        List<string> rendererTypes,
        RendererRegistry rendererRegistry)
    {
        _gauges = gauges;
        _dataSources = dataSources;
        _rendererRegistry = rendererRegistry;
        DataSourceNames = new ObservableCollection<string>(dataSourceNames);
        RendererTypes = rendererTypes;
        foreach (var name in gauges.Keys)
            Names.Add(name);
    }

    partial void OnSelectedNameChanged(string? oldValue, string? newValue)
    {
        SaveCurrent(oldValue);
        LoadCurrent(newValue);
    }

    partial void OnRendererTypeChanged(string value)
    {
        RebuildRendererParams();
    }

    partial void OnSourceChanged(string value)
    {
        RebuildSensorSuggestions();
    }

    private void RebuildSensorSuggestions()
    {
        SensorSuggestions.Clear();

        if (string.IsNullOrEmpty(Source)) return;
        if (!_dataSources.TryGetValue(Source, out var dsConfig)) return;

        var sourceType = PluginLoader.ResolveType(dsConfig.Plugin);
        if (sourceType == null) return;

        var sensors = SensorAttributeHelper.GetSensorsFromAttributes(sourceType);
        foreach (var sensor in sensors)
            SensorSuggestions.Add(sensor.Path);
    }

    private void RebuildRendererParams()
    {
        RendererParams.Clear();

        if (string.IsNullOrEmpty(RendererType))
        {
            OnPropertyChanged(nameof(HasRendererParams));
            return;
        }

        var entry = _rendererRegistry.Get(RendererType);
        if (entry == null || !entry.HasParameters)
        {
            OnPropertyChanged(nameof(HasRendererParams));
            return;
        }

        // Get current config params if editing an existing gauge
        Dictionary<string, JsonElement>? configParams = null;
        if (SelectedName != null && _gauges.TryGetValue(SelectedName, out var gc))
            configParams = gc.Renderer.Parameters;

        foreach (var paramInfo in entry.Parameters)
        {
            string? currentValue = null;
            if (configParams != null && configParams.TryGetValue(paramInfo.Key, out var elem))
            {
                currentValue = elem.ValueKind == JsonValueKind.String
                    ? elem.GetString()
                    : elem.GetRawText();
            }

            RendererParams.Add(new RendererParamViewModel(paramInfo, currentValue));
        }

        OnPropertyChanged(nameof(HasRendererParams));
    }

    private void SaveCurrent(string? name)
    {
        if (name == null || !_gauges.ContainsKey(name)) return;
        var gc = _gauges[name];
        gc.Source = Source;
        gc.Sensor = Sensor;
        gc.Renderer.Type = RendererType;
        gc.Label = Label;
        gc.Min = Min;
        gc.Max = Max;
        gc.Theme = Theme;

        SaveRendererParams(gc);
    }

    private void SaveRendererParams(GaugeConfig gc)
    {
        if (RendererParams.Count == 0)
        {
            gc.Renderer.Parameters = null;
            return;
        }

        gc.Renderer.Parameters ??= new Dictionary<string, JsonElement>();
        // Remove keys that no longer belong to the current renderer
        var validKeys = new HashSet<string>(RendererParams.Select(p => p.Key));
        foreach (var key in gc.Renderer.Parameters.Keys.ToList())
        {
            if (!validKeys.Contains(key))
                gc.Renderer.Parameters.Remove(key);
        }

        foreach (var param in RendererParams)
        {
            if (!string.IsNullOrEmpty(param.Value))
                gc.Renderer.Parameters[param.Key] = JsonSerializer.SerializeToElement(param.Value);
            else
                gc.Renderer.Parameters.Remove(param.Key);
        }

        if (gc.Renderer.Parameters.Count == 0)
            gc.Renderer.Parameters = null;
    }

    private void LoadCurrent(string? name)
    {
        if (name == null || !_gauges.TryGetValue(name, out var gc))
        {
            Source = ""; Sensor = ""; RendererType = "FullRing";
            Label = null; Min = 0; Max = 100; Theme = null;
            RendererParams.Clear();
            SensorSuggestions.Clear();
            OnPropertyChanged(nameof(HasRendererParams));
            return;
        }
        Source = gc.Source;
        // RebuildSensorSuggestions is called by OnSourceChanged
        Sensor = gc.Sensor;
        RendererType = gc.Renderer.Type;
        // RebuildRendererParams is called by OnRendererTypeChanged,
        // but if the type didn't change we need to rebuild manually
        // to pick up the new gauge's param values.
        RebuildRendererParams();
        Label = gc.Label;
        Min = gc.Min;
        Max = gc.Max;
        Theme = gc.Theme;
    }

    [RelayCommand]
    private void Add()
    {
        var i = 1;
        while (Names.Contains($"gauge{i}")) i++;
        var name = $"gauge{i}";
        _gauges[name] = new GaugeConfig();
        Names.Add(name);
        SelectedName = name;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedName == null) return;
        var name = SelectedName;
        var idx = Names.IndexOf(name);
        _gauges.Remove(name);
        Names.Remove(name);
        SelectedName = Names.Count > 0 ? Names[Math.Min(idx, Names.Count - 1)] : null;
    }

    public void SaveAll()
    {
        SaveCurrent(SelectedName);
    }
}
