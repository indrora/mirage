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

    /// <summary>
    /// Fired immediately when a rename is committed inside the dialog. The rename
    /// mutates the LIVE config dictionary on the spot (this dialog edits
    /// _config.Gauges by reference, and the running engine reads the same instance),
    /// so anything still holding the old name — slot view models, live preview
    /// rendering, the device itself — starts failing to resolve the gauge the moment
    /// RenameSelected returns. The owner uses this to retarget those references right
    /// away instead of waiting for the dialog to close; the close-time batch pass
    /// over RenamedGauges remains as an idempotent safety net.
    /// </summary>
    private readonly Action<string, string>? _onRenamed;
    private IReadOnlyList<SensorInfo> _currentSensors = [];

    // Transient-probe results per source name (config can't change while this
    // modal dialog is open, so the name is a sufficient key). Only metadata is
    // cached — a RenderFunc must never outlive its disposed probe instance, so
    // custom-renderer availability is captured as a set of sensor paths.
    private readonly Dictionary<string, ProbeResult> _probeCache = new();
    private int _probeVersion;
    private IReadOnlySet<string> _currentRendererPaths = new HashSet<string>();

    private sealed record ProbeResult(IReadOnlyList<SensorInfo> Sensors, IReadOnlySet<string> CustomRendererPaths);

    public ObservableCollection<string> Names { get; } = new();
    public ObservableCollection<string> DataSourceNames { get; }
    private readonly List<string> _builtinRendererTypes;
    public ObservableCollection<RendererOptionViewModel> RendererOptions { get; } = new();

    [ObservableProperty] private RendererOptionViewModel? _selectedRenderer;
    private bool _syncingRenderer;

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
        Func<string, IDataSource?>? liveSourceResolver = null,
        Action<string, string>? onRenamed = null)
    {
        _gauges = gauges;
        _dataSources = dataSources;
        _rendererRegistry = rendererRegistry;
        _liveSourceResolver = liveSourceResolver;
        _onRenamed = onRenamed;
        DataSourceNames = new ObservableCollection<string>(dataSourceNames);
        _builtinRendererTypes = rendererTypes;
        RebuildRendererOptions();
        foreach (var name in gauges.Keys)
            Names.Add(name);
    }

    partial void OnSelectedRendererChanged(RendererOptionViewModel? value)
    {
        // Ignore the null the ComboBox pushes while the options list is rebuilt.
        if (value != null && !_syncingRenderer)
            RendererType = value.Id;
    }

    private void RebuildRendererOptions()
    {
        RendererOptions.Clear();
        foreach (var name in _builtinRendererTypes)
            RendererOptions.Add(new RendererOptionViewModel(name, name, IsFromSource: false));
        if (SourceHasRendererFor(Sensor))
            RendererOptions.Add(SensorRendererOption);
        SyncSelectedRenderer();
    }

    private static RendererOptionViewModel SensorRendererOption =>
        new(SourceRenderer.RendererType, "Sensor renderer", IsFromSource: true);

    /// <summary>
    /// Re-selects the option matching the config value, adding an inert entry
    /// for values the current options don't cover (e.g. "__source__" while the
    /// source isn't running) so the config value isn't silently lost on save.
    /// </summary>
    private void SyncSelectedRenderer()
    {
        var match = RendererOptions.FirstOrDefault(
            o => string.Equals(o.Id, RendererType, StringComparison.OrdinalIgnoreCase));
        if (match == null && !string.IsNullOrEmpty(RendererType))
        {
            match = RendererType == SourceRenderer.RendererType
                ? SensorRendererOption
                : new RendererOptionViewModel(RendererType, RendererType, IsFromSource: false);
            RendererOptions.Add(match);
        }
        _syncingRenderer = true;
        SelectedRenderer = match;
        _syncingRenderer = false;
    }

    private bool SourceHasRendererFor(string sensor)
    {
        if (string.IsNullOrEmpty(Source) || string.IsNullOrEmpty(sensor)) return false;

        var live = _liveSourceResolver?.Invoke(Source);
        if (live != null)
            return live.HasCustomRenderer && live.GetCustomRenderer(sensor) != null;

        return _currentRendererPaths.Contains(sensor);
    }

    partial void OnSelectedNameChanged(string? oldValue, string? newValue)
    {
        SaveCurrent(oldValue);
        LoadCurrent(newValue);
    }

    partial void OnRendererTypeChanged(string value)
    {
        SyncSelectedRenderer();
        RebuildRendererParams();
    }

    partial void OnSourceChanged(string value)
    {
        RebuildSensorSuggestions();
    }

    partial void OnSensorChanged(string value)
    {
        UpdateSensorElevationHint();
        RebuildRendererOptions();
    }

    private void RebuildSensorSuggestions()
    {
        SensorSuggestions.Clear();
        _currentSensors = [];
        _currentRendererPaths = new HashSet<string>();
        _probeVersion++;

        if (string.IsNullOrEmpty(Source)) { ApplySensors([]); return; }
        if (!_dataSources.TryGetValue(Source, out var dsConfig)) { ApplySensors([]); return; }

        // Prefer the live instance: dynamic sources (e.g. hardware monitors)
        // only know their sensors at runtime, and custom-renderer availability
        // is checked against it directly.
        var live = _liveSourceResolver?.Invoke(Source);
        if (live != null)
        {
            ApplySensors(live.GetAvailableSensors());
            return;
        }

        var sourceType = PluginLoader.ResolveType(dsConfig.Plugin);
        if (sourceType == null) { ApplySensors([]); return; }

        if (_probeCache.TryGetValue(Source, out var cached))
        {
            ApplySensors(cached.Sensors, cached.CustomRendererPaths);
            return;
        }

        // [Sensor] attributes give an immediate list while the probe runs;
        // custom-renderer availability still needs an instance.
        ApplySensors(SensorAttributeHelper.GetSensorsFromAttributes(sourceType));

        // Source that isn't running (newly added, or engine stopped): probe a
        // transient instance off the UI thread so the sensor list and renderer
        // availability work without an engine restart.
        var name = Source;
        var version = _probeVersion;
        var plugin = dsConfig.Plugin;
        var config = dsConfig.Config?.ToDictionary(
            kv => kv.Key,
            object? (kv) => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : kv.Value.GetRawText())
            ?? new Dictionary<string, object?>();

        Task.Run(() => ProbeSensors(plugin, config)).ContinueWith(t =>
        {
            if (t.Result is not { } probed) return;
            Dispatcher.UIThread.Post(() =>
            {
                _probeCache[name] = probed;
                if (version == _probeVersion)
                    ApplySensors(probed.Sensors, probed.CustomRendererPaths);
            });
        });
    }

    private void ApplySensors(IReadOnlyList<SensorInfo> sensors, IReadOnlySet<string>? rendererPaths = null)
    {
        _currentSensors = sensors;
        _currentRendererPaths = rendererPaths ?? new HashSet<string>();
        SensorSuggestions.Clear();
        foreach (var sensor in sensors)
            SensorSuggestions.Add(sensor.Path);
        UpdateSensorElevationHint();
        RebuildRendererOptions();
    }

    private static ProbeResult? ProbeSensors(string plugin, Dictionary<string, object?> config)
    {
        IDataSource? source = null;
        try
        {
            source = PluginLoader.Create(plugin);
            if (source == null) return null;
            source.InitializeAsync(config).GetAwaiter().GetResult();
            source.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            var sensors = source.GetAvailableSensors();
            var rendererPaths = source.HasCustomRenderer
                ? sensors.Where(s => source.GetCustomRenderer(s.Path) != null).Select(s => s.Path).ToHashSet()
                : new HashSet<string>();
            return new ProbeResult(sensors, rendererPaths);
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

        // The source supplies the RenderFunc directly; registry params don't apply.
        if (RendererType == SourceRenderer.RendererType)
        {
            OnPropertyChanged(nameof(HasRendererParams));
            return;
        }

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

    /// <summary>
    /// Renames applied during this dialog session (old → new, in order), so
    /// the main view model can retarget scene buttons after the dialog closes.
    /// </summary>
    public List<(string Old, string New)> RenamedGauges { get; } = new();

    /// <summary>Returns an error message, or null if the name is usable.</summary>
    public string? ValidateNewName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Name cannot be empty."
        : name != SelectedName && _gauges.ContainsKey(name) ? $"A gauge named '{name}' already exists."
        : null;

    public void RenameSelected(string newName)
    {
        if (SelectedName is not { } oldName || oldName == newName) return;
        if (!_gauges.TryGetValue(oldName, out var gc) || _gauges.ContainsKey(newName)) return;

        SaveCurrent(oldName);
        _gauges.Remove(oldName);
        _gauges[newName] = gc;
        RenamedGauges.Add((oldName, newName));

        var idx = Names.IndexOf(oldName);
        Names[idx] = newName;
        SelectedName = newName;

        _onRenamed?.Invoke(oldName, newName);
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
