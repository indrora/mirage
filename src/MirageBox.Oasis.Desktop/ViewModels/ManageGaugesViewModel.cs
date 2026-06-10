using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
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
    private readonly Func<string, IDataSource?>? _liveSourceResolver;
    private IReadOnlyList<SensorInfo> _currentSensors = [];

    // Transient-probe results per source name (config can't change while this
    // modal dialog is open, so the name is a sufficient key).
    private readonly Dictionary<string, IReadOnlyList<SensorInfo>> _probeCache = new();
    private int _probeVersion;

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
    [ObservableProperty] private decimal? _min;
    [ObservableProperty] private decimal? _max;
    [ObservableProperty] private string? _theme;

    [ObservableProperty] private bool _showSensorElevationHint;

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
        RendererRegistry rendererRegistry,
        Func<string, IDataSource?>? liveSourceResolver = null)
    {
        _gauges = gauges;
        _dataSources = dataSources;
        _rendererRegistry = rendererRegistry;
        _liveSourceResolver = liveSourceResolver;
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

    partial void OnSensorChanged(string value)
    {
        UpdateSensorElevationHint();
    }

    private void RebuildSensorSuggestions()
    {
        SensorSuggestions.Clear();
        _currentSensors = [];
        _probeVersion++;

        if (string.IsNullOrEmpty(Source)) { UpdateSensorElevationHint(); return; }
        if (!_dataSources.TryGetValue(Source, out var dsConfig)) { UpdateSensorElevationHint(); return; }

        // Prefer the live instance: dynamic sources (e.g. hardware monitors)
        // only know their sensors at runtime.
        var live = _liveSourceResolver?.Invoke(Source);
        if (live != null)
        {
            ApplySensors(live.GetAvailableSensors());
            return;
        }

        var sourceType = PluginLoader.ResolveType(dsConfig.Plugin);
        if (sourceType == null) { UpdateSensorElevationHint(); return; }

        var fromAttributes = SensorAttributeHelper.GetSensorsFromAttributes(sourceType);
        if (fromAttributes.Count > 0)
        {
            ApplySensors(fromAttributes);
            return;
        }

        // Dynamic source that isn't running (newly added, or engine stopped):
        // probe a transient instance off the UI thread so the list works
        // without an engine restart.
        if (_probeCache.TryGetValue(Source, out var cached))
        {
            ApplySensors(cached);
            return;
        }

        UpdateSensorElevationHint();
        var name = Source;
        var version = _probeVersion;
        var plugin = dsConfig.Plugin;
        var config = dsConfig.Config?.ToDictionary(
            kv => kv.Key,
            object? (kv) => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : kv.Value.GetRawText())
            ?? new Dictionary<string, object?>();

        Task.Run(() => ProbeSensors(plugin, config)).ContinueWith(t =>
        {
            if (t.Result is not { } sensors) return;
            Dispatcher.UIThread.Post(() =>
            {
                _probeCache[name] = sensors;
                if (version == _probeVersion)
                    ApplySensors(sensors);
            });
        });
    }

    private void ApplySensors(IReadOnlyList<SensorInfo> sensors)
    {
        _currentSensors = sensors;
        SensorSuggestions.Clear();
        foreach (var sensor in sensors)
            SensorSuggestions.Add(sensor.Path);
        UpdateSensorElevationHint();
    }

    private static IReadOnlyList<SensorInfo>? ProbeSensors(string plugin, Dictionary<string, object?> config)
    {
        IDataSource? source = null;
        try
        {
            source = PluginLoader.Create(plugin);
            if (source == null) return null;
            source.InitializeAsync(config).GetAwaiter().GetResult();
            source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            return source.GetAvailableSensors();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gauges] Sensor probe of '{plugin}' failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (source != null)
            {
                try { source.StopAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
                source.Dispose();
            }
        }
    }

    private void UpdateSensorElevationHint()
    {
        var info = _currentSensors.FirstOrDefault(s => s.Path == Sensor);
        ShowSensorElevationHint = info is { RequiresElevation: true } && !PluginCatalog.IsElevated;
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
        gc.Min = (float?)Min;
        gc.Max = (float?)Max;
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
            Label = null; Min = null; Max = null; Theme = null;
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
        Min = (decimal?)gc.Min;
        Max = (decimal?)gc.Max;
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
