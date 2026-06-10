using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirageBox;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.Engine;

namespace MirageBox.Oasis.Desktop.ViewModels;

using MirageBox.Oasis.Core.DataSources;

public record SourceActionDef(string Name, string Description, string? ParamName = null, string? ParamDefault = null, bool IsDefault = false)
{
    public bool HasParam => ParamName != null;
    public bool IsSentinel => Name is "" or "__default__";
    public override string ToString() => Name;

    public static SourceActionDef None { get; } = new("", "No action on press");
    public static SourceActionDef DefaultFor(SourceActionInfo info) =>
        new("__default__", $"Default: {info.Name} — {info.Description}", info.ParamName, info.ParamDefault);

    public static SourceActionDef FromInfo(SourceActionInfo info) =>
        new(info.Name, info.Description, info.ParamName, info.ParamDefault, info.IsDefault);
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly OasisConfig _config;
    private OasisEngine? _engine;
    private CancellationTokenSource? _engineCts;
    private readonly string _configPath;
    private readonly RendererRegistry _rendererRegistry = new();
    private DispatcherTimer? _applyTimer;

    [ObservableProperty] private string _statusText = "Starting…";

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();
    public ObservableCollection<DataSourceViewModel> DataSources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDevice))]
    private DeviceViewModel? _selectedDevice;

    public ObservableCollection<SceneViewModel> DeviceScenes { get; } = new();
    public ObservableCollection<ButtonSlotViewModel> DisplaySlots { get; } = new();
    public ObservableCollection<ButtonSlotViewModel> TactileSlots { get; } = new();
    public ObservableCollection<ButtonSlotViewModel> EncoderSlots { get; } = new();

    [ObservableProperty] private SceneViewModel? _selectedScene;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSlot))]
    private ButtonSlotViewModel? _selectedSlot;

    public ObservableCollection<SourceActionDef> SelectedSlotSourceActions { get; } = new();
    private SourceActionDef? _selectedSlotActionDef;
    public SourceActionDef? SelectedSlotActionDef
    {
        get => _selectedSlotActionDef;
        set
        {
            if (SetProperty(ref _selectedSlotActionDef, value))
            {
                OnPropertyChanged(nameof(SelectedSlotActionHasParam));
                if (value != null && SelectedSlot != null)
                {
                    if (value.Name == "")
                        SelectedSlot.ActionSourceAction = "";
                    else
                        SelectedSlot.ActionSourceAction = value.Name;
                }
            }
        }
    }
    public bool SelectedSlotActionHasParam => SelectedSlotActionDef?.HasParam ?? false;

    public ObservableCollection<string> GaugeNames { get; } = new();
    public ObservableCollection<string> DataSourceNames { get; } = new();

    public List<string> RendererTypes { get; private set; } = new();

    public static List<string> ActionTypes { get; } = ["(none)", "dataSource", "switchScene", "launch", "command"];

    public bool HasSelectedDevice => SelectedDevice != null;
    public bool HasSelectedSlot => SelectedSlot != null;

    private Dictionary<string, IMirageDevice> _discoveredDevices = new();

    public MainWindowViewModel()
    {
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            _configPath = "";
            var designerDevice = new DesignerDevice();
            _discoveredDevices[designerDevice.SerialNumber] = designerDevice;
            _config = CreateDesignTimeConfig(designerDevice);
            RendererTypes = ["FullRing", "Bar", "Text"];
            RefreshFromConfig();
            return;
        }

        _configPath = ConfigLoader.DefaultConfigPath;
        _config = ConfigLoader.Load(_configPath);
        RendererTypes = _rendererRegistry.GetAll().Select(r => r.Name).Order().ToList();
        DiscoverHardware();
        RefreshFromConfig();

        // Live-first model: the engine is always running; the UI edits it.
        _ = StartEngineAsync();
    }

    private static OasisConfig CreateDesignTimeConfig(DesignerDevice device)
    {
        var config = new OasisConfig();

        config.Devices["designer"] = new DeviceConfig
        {
            Serial = device.SerialNumber,
            Simulator = false,
            Buttons = device.ImageButtonCount,
            Tactile = device.TactileButtonCount,
            ImageSize = device.ImageWidth,
        };

        config.DataSources["clock"] = new DataSourceConfig { Plugin = "__builtin:clock" };
        config.DataSources["counter"] = new DataSourceConfig { Plugin = "__builtin:counter" };
        config.DataSources["timer"] = new DataSourceConfig { Plugin = "__builtin:timer" };

        config.Gauges["clock"] = new GaugeConfig
        {
            Source = "clock", Sensor = "time",
            Renderer = new RendererConfig { Type = "Text" },
            Label = "Clock",
        };
        config.Gauges["count"] = new GaugeConfig
        {
            Source = "counter", Sensor = "value",
            Renderer = new RendererConfig { Type = "FullRing" },
            Label = "Counter", Max = 100,
        };
        config.Gauges["elapsed"] = new GaugeConfig
        {
            Source = "timer", Sensor = "elapsed",
            Renderer = new RendererConfig { Type = "Bar" },
            Label = "Timer", Max = 60,
        };

        config.Scenes["designer"] = new DeviceSceneConfig
        {
            ActiveScene = "main",
            List = new Dictionary<string, SceneConfig>
            {
                ["main"] = new SceneConfig
                {
                    Buttons = new Dictionary<string, ButtonAssignmentConfig>
                    {
                        ["0"] = new ButtonAssignmentConfig { Gauge = "clock" },
                        ["1"] = new ButtonAssignmentConfig { Gauge = "count" },
                        ["2"] = new ButtonAssignmentConfig { Gauge = "elapsed" },
                    }
                }
            }
        };

        return config;
    }

    private void DiscoverHardware()
    {
        try
        {
            foreach (var dev in DeviceFactory.DiscoverDevices())
                _discoveredDevices[dev.SerialNumber] = dev;
        }
        catch { }
    }

    private void RefreshFromConfig()
    {
        Devices.Clear();
        DataSources.Clear();
        GaugeNames.Clear();
        DataSourceNames.Clear();

        foreach (var (name, dc) in _config.Devices)
        {
            var vm = new DeviceViewModel(name, dc);
            if (!dc.Simulator && dc.Serial != null
                && _discoveredDevices.TryGetValue(dc.Serial, out var hwDevice))
            {
                vm.ApplyHardwareProfile(hwDevice);
            }
            if (_config.Scenes.TryGetValue(name, out var sc))
                vm.LoadScenes(sc);
            Devices.Add(vm);
        }

        foreach (var (name, ds) in _config.DataSources)
        {
            DataSources.Add(new DataSourceViewModel(name, ds));
            DataSourceNames.Add(name);
        }

        foreach (var name in _config.Gauges.Keys)
            GaugeNames.Add(name);

        if (Devices.Count > 0)
            SelectedDevice = Devices[0];
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        RebuildSceneList();
        RemoveDeviceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRemoveSelectedDevice));
    }

    partial void OnSelectedSceneChanged(SceneViewModel? value)
    {
        FlushSlotsToScene();
        RebuildSlots();
    }

    partial void OnSelectedSlotChanged(ButtonSlotViewModel? oldValue, ButtonSlotViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSlotPropertyChanged;
            oldValue.IsSelected = false;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnSlotPropertyChanged;
            newValue.IsSelected = true;
        }
        RebuildSourceActions();
    }

    private void RebuildSourceActions()
    {
        SelectedSlotSourceActions.Clear();
        SelectedSlotActionDef = null;

        var sourceName = SelectedSlot?.ActionSource;
        if (string.IsNullOrEmpty(sourceName)) return;

        var ds = DataSources.FirstOrDefault(d => d.Name == sourceName);
        if (ds == null) return;

        var sourceType = PluginLoader.ResolveType(ds.Plugin);
        if (sourceType == null) return;
        var actions = SourceActionHelper.GetActionsFromAttributes(sourceType);

        SelectedSlotSourceActions.Add(SourceActionDef.None);

        var defaultAction = actions.FirstOrDefault(a => a.IsDefault);
        if (defaultAction != null)
            SelectedSlotSourceActions.Add(SourceActionDef.DefaultFor(defaultAction));

        foreach (var a in actions)
            SelectedSlotSourceActions.Add(SourceActionDef.FromInfo(a));

        var currentAction = SelectedSlot?.ActionSourceAction;
        if (string.IsNullOrEmpty(currentAction))
            SelectedSlotActionDef = SelectedSlotSourceActions[0]; // None
        else if (currentAction == "__default__")
            SelectedSlotActionDef = SelectedSlotSourceActions.FirstOrDefault(a => a.Name == "__default__");
        else
            SelectedSlotActionDef = SelectedSlotSourceActions.FirstOrDefault(a => a.Name == currentAction);
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ButtonSlotViewModel.ActionSource))
            RebuildSourceActions();
        if (e.PropertyName != nameof(ButtonSlotViewModel.IsSelected))
            ScheduleApply();
    }

    private void RebuildSceneList()
    {
        DeviceScenes.Clear();
        SelectedScene = null;
        if (SelectedDevice == null) return;

        foreach (var scene in SelectedDevice.Scenes.Where(s => !s.IsPinned))
            DeviceScenes.Add(scene);

        if (DeviceScenes.Count > 0)
            SelectedScene = DeviceScenes[0];
    }

    private SceneViewModel? _lastFlushedScene;

    private void RebuildSlots()
    {
        DisplaySlots.Clear();
        TactileSlots.Clear();
        EncoderSlots.Clear();
        SelectedSlot = null;
        if (SelectedDevice == null) return;

        var pinnedScene = SelectedDevice.Scenes.FirstOrDefault(s => s.IsPinned);
        var activeScene = SelectedScene;

        for (int i = 0; i < SelectedDevice.Buttons; i++)
        {
            var idx = i.ToString();
            var pinnedBtn = pinnedScene?.Buttons.FirstOrDefault(b => b.ButtonIndex == idx);
            var sceneBtn = activeScene?.Buttons.FirstOrDefault(b => b.ButtonIndex == idx);

            var slot = new ButtonSlotViewModel(i, "display");
            if (pinnedBtn != null)
            {
                slot.IsPinned = true;
                slot.GaugeName = pinnedBtn.Gauge;
                slot.ActionType = pinnedBtn.ActionType;
                slot.ActionParam = pinnedBtn.ActionParam;
                slot.ActionSource = pinnedBtn.ActionSource;
                slot.ActionSourceAction = pinnedBtn.ActionSourceAction;
                slot.ActionSourceParam = pinnedBtn.ActionSourceParam;
            }
            else if (sceneBtn != null)
            {
                slot.GaugeName = sceneBtn.Gauge;
                slot.ActionType = sceneBtn.ActionType;
                slot.ActionParam = sceneBtn.ActionParam;
                slot.ActionSource = sceneBtn.ActionSource;
                slot.ActionSourceAction = sceneBtn.ActionSourceAction;
                slot.ActionSourceParam = sceneBtn.ActionSourceParam;
            }
            DisplaySlots.Add(slot);
        }

        for (int i = 0; i < SelectedDevice.Tactile; i++)
        {
            var idx = i.ToString();
            var slot = new ButtonSlotViewModel(i, "tactile");
            var btn = activeScene?.TactileButtons.FirstOrDefault(b => b.ButtonIndex == idx);
            if (btn != null)
            {
                slot.ActionType = btn.ActionType;
                slot.ActionParam = btn.ActionParam;
                slot.ActionSource = btn.ActionSource;
                slot.ActionSourceAction = btn.ActionSourceAction;
                slot.ActionSourceParam = btn.ActionSourceParam;
            }
            TactileSlots.Add(slot);
        }

        for (int i = 0; i < SelectedDevice.Encoders; i++)
        {
            var idx = i.ToString();
            var slot = new ButtonSlotViewModel(i, "encoder");
            var btn = activeScene?.Encoders.FirstOrDefault(b => b.ButtonIndex == idx);
            if (btn != null)
            {
                slot.ActionType = btn.ActionType;
                slot.ActionParam = btn.ActionParam;
                slot.ActionSource = btn.ActionSource;
                slot.ActionSourceAction = btn.ActionSourceAction;
                slot.ActionSourceParam = btn.ActionSourceParam;
            }
            EncoderSlots.Add(slot);
        }

        _lastFlushedScene = activeScene;
    }

    private void FlushSlotsToScene()
    {
        if (SelectedDevice == null || _lastFlushedScene == null) return;
        if (DisplaySlots.Count == 0) return;

        var pinnedScene = SelectedDevice.Scenes.FirstOrDefault(s => s.IsPinned);

        pinnedScene?.Buttons.Clear();
        _lastFlushedScene.Buttons.Clear();
        _lastFlushedScene.TactileButtons.Clear();
        _lastFlushedScene.Encoders.Clear();

        foreach (var slot in DisplaySlots)
        {
            if (slot.IsEmpty) continue;
            var btn = new DeviceButtonViewModel(slot.Index.ToString(), slot.GaugeName,
                slot.ActionType, slot.ActionParam, slot.ActionSource, slot.ActionSourceAction,
                slot.ActionSourceParam, slot.IsPinned);

            if (slot.IsPinned)
                pinnedScene?.Buttons.Add(btn);
            else
                _lastFlushedScene.Buttons.Add(btn);
        }

        foreach (var slot in TactileSlots)
        {
            if (slot.IsEmpty) continue;
            _lastFlushedScene.TactileButtons.Add(new DeviceButtonViewModel(
                slot.Index.ToString(), null,
                slot.ActionType, slot.ActionParam, slot.ActionSource,
                slot.ActionSourceAction, slot.ActionSourceParam, false));
        }

        foreach (var slot in EncoderSlots)
        {
            if (slot.IsEmpty) continue;
            _lastFlushedScene.Encoders.Add(new DeviceButtonViewModel(
                slot.Index.ToString(), null,
                slot.ActionType, slot.ActionParam, slot.ActionSource,
                slot.ActionSourceAction, slot.ActionSourceParam, false));
        }
    }

    public ManageGaugesViewModel CreateManageGaugesViewModel() =>
        new(_config.Gauges, _config.DataSources, DataSourceNames, RendererTypes, _rendererRegistry,
            name => _engine?.GetDataSource(name));

    public ManageDataSourcesViewModel CreateManageDataSourcesViewModel() =>
        new(_config.DataSources, name => _engine?.GetDataSource(name));

    /// <summary>Hot-applies edited data source configs to the running engine.</summary>
    public async Task ApplyDataSourceChangesAsync()
    {
        if (_engine == null) return;
        try
        {
            await _engine.ApplyDataSourceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Data source reload failed: {ex.Message}";
        }
    }

    public void RefreshGaugeNames()
    {
        GaugeNames.Clear();
        foreach (var name in _config.Gauges.Keys)
            GaugeNames.Add(name);
    }

    public void RefreshDataSourceNames()
    {
        DataSourceNames.Clear();
        DataSources.Clear();
        foreach (var (name, ds) in _config.DataSources)
        {
            DataSourceNames.Add(name);
            DataSources.Add(new DataSourceViewModel(name, ds));
        }
    }

    public void SwapSlots(string slotType, int fromIndex, int toIndex)
    {
        var collection = slotType == "display" ? DisplaySlots : TactileSlots;
        if (fromIndex < 0 || fromIndex >= collection.Count) return;
        if (toIndex < 0 || toIndex >= collection.Count) return;

        var from = collection[fromIndex];
        var to = collection[toIndex];

        var tmpGauge = from.GaugeName;
        var tmpAction = from.ActionType;
        var tmpParam = from.ActionParam;
        var tmpSource = from.ActionSource;
        var tmpSourceAction = from.ActionSourceAction;
        var tmpSourceParam = from.ActionSourceParam;
        var tmpPinned = from.IsPinned;

        from.GaugeName = to.GaugeName;
        from.ActionType = to.ActionType;
        from.ActionParam = to.ActionParam;
        from.ActionSource = to.ActionSource;
        from.ActionSourceAction = to.ActionSourceAction;
        from.ActionSourceParam = to.ActionSourceParam;
        from.IsPinned = to.IsPinned;

        to.GaugeName = tmpGauge;
        to.ActionType = tmpAction;
        to.ActionParam = tmpParam;
        to.ActionSource = tmpSource;
        to.ActionSourceAction = tmpSourceAction;
        to.ActionSourceParam = tmpSourceParam;
        to.IsPinned = tmpPinned;

        ScheduleApply();
    }

    [RelayCommand]
    private void AddScene()
    {
        if (SelectedDevice == null) return;
        var name = $"scene{DeviceScenes.Count + 1}";
        var scene = new SceneViewModel(name, false);
        SelectedDevice.Scenes.Add(scene);
        DeviceScenes.Add(scene);
        SelectedScene = scene;
        ScheduleApply();
    }

    [RelayCommand]
    private void RemoveScene()
    {
        if (SelectedScene == null || SelectedScene.IsPinned) return;
        var idx = DeviceScenes.IndexOf(SelectedScene);
        SelectedDevice?.Scenes.Remove(SelectedScene);
        DeviceScenes.Remove(SelectedScene);
        SelectedScene = DeviceScenes.Count > 0
            ? DeviceScenes[Math.Min(idx, DeviceScenes.Count - 1)]
            : null;
        ScheduleApply();
    }

    [RelayCommand]
    private void AddDevice()
    {
        var name = $"device{Devices.Count + 1}";
        var vm = new DeviceViewModel(name, new DeviceConfig { Simulator = true });
        Devices.Add(vm);
        SelectedDevice = vm;
        ScheduleApply();
    }

    /// <summary>Only simulators can be removed; physical hardware stays (ignore it instead).</summary>
    public bool CanRemoveSelectedDevice => SelectedDevice is { IsSimulator: true };

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedDevice))]
    private void RemoveDevice()
    {
        if (SelectedDevice is not { IsSimulator: true }) return;
        var idx = Devices.IndexOf(SelectedDevice);
        Devices.Remove(SelectedDevice);
        SelectedDevice = Devices.Count > 0
            ? Devices[Math.Min(idx, Devices.Count - 1)]
            : null;
        ScheduleApply();
    }

    private async Task StartEngineAsync()
    {
        try
        {
            SyncConfigFromViewModels();
            _engine = new OasisEngine(_config);
            _engineCts = new CancellationTokenSource();
            await _engine.StartAsync(_engineCts.Token);
            UpdateLiveStatus();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    /// <summary>Stops the engine; called when the application shuts down.</summary>
    public async Task ShutdownAsync()
    {
        if (_engine == null) return;
        _engineCts?.Cancel();
        await _engine.StopAsync();
        _engine.Dispose();
        _engine = null;
    }

    private void UpdateLiveStatus()
        => StatusText = $"Live — {_config.Devices.Count} device(s), {_config.DataSources.Count} source(s), {_config.Gauges.Count} gauge(s)";

    /// <summary>
    /// Debounced push of the current UI state into the running engine.
    /// Every layout edit funnels through here; the UI always reflects "now".
    /// </summary>
    public void ScheduleApply()
    {
        if (_engine == null) return;
        _applyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _applyTimer.Tick -= OnApplyTimerTick;
        _applyTimer.Tick += OnApplyTimerTick;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private async void OnApplyTimerTick(object? sender, EventArgs e)
    {
        _applyTimer?.Stop();
        await ApplyNowAsync();
    }

    private async Task ApplyNowAsync()
    {
        if (_engine == null) return;
        try
        {
            SyncConfigFromViewModels();
            await _engine.ApplyConfigChangesAsync();
            UpdateLiveStatus();
        }
        catch (Exception ex)
        {
            StatusText = $"Apply failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        SyncConfigFromViewModels();
        ConfigLoader.Save(_config, _configPath);
        StatusText = "Saved to disk";
    }

    [RelayCommand]
    private async Task RevertConfig()
    {
        ReplaceConfigContents(ConfigLoader.Load(_configPath));
        RefreshFromConfig();
        if (_engine != null) await _engine.ApplyConfigChangesAsync();
        StatusText = "Reverted to saved version";
    }

    [RelayCommand]
    private async Task ResetConfig()
    {
        var fresh = new OasisConfig();
        // Physical hardware is never removed — carry it over with a blank layout.
        foreach (var (name, dev) in _config.Devices.Where(kv => !kv.Value.Simulator))
        {
            fresh.Devices[name] = dev;
            fresh.Scenes[name] = new DeviceSceneConfig
            {
                ActiveScene = "main",
                List = new Dictionary<string, SceneConfig> { ["main"] = new() },
            };
        }

        ReplaceConfigContents(fresh);
        RefreshFromConfig();
        if (_engine != null) await _engine.ApplyConfigChangesAsync();
        StatusText = "Reset to default layout (not yet saved)";
    }

    public async Task ExportConfigAsync(string zipPath)
    {
        try
        {
            SyncConfigFromViewModels();
            await Task.Run(() => ConfigArchive.Export(_config, _rendererRegistry, zipPath));
            StatusText = $"Exported to {Path.GetFileName(zipPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    public async Task ImportConfigAsync(string zipPath)
    {
        try
        {
            var imported = await Task.Run(() => ConfigArchive.Import(zipPath, _rendererRegistry));
            ReplaceConfigContents(imported);
            RefreshFromConfig();
            if (_engine != null) await _engine.ApplyConfigChangesAsync();
            StatusText = $"Imported {Path.GetFileName(zipPath)} (not yet saved)";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Copies another config's contents into the live config object in place,
    /// preserving the object/dictionary instances the engine holds.
    /// </summary>
    private void ReplaceConfigContents(OasisConfig source)
    {
        _config.Devices.Clear();
        foreach (var (k, v) in source.Devices) _config.Devices[k] = v;
        _config.DataSources.Clear();
        foreach (var (k, v) in source.DataSources) _config.DataSources[k] = v;
        _config.Gauges.Clear();
        foreach (var (k, v) in source.Gauges) _config.Gauges[k] = v;
        _config.Scenes.Clear();
        foreach (var (k, v) in source.Scenes) _config.Scenes[k] = v;
        _config.Themes.Clear();
        foreach (var (k, v) in source.Themes) _config.Themes[k] = v;
        _config.Defaults = source.Defaults;
        _config.ContentDirs = source.ContentDirs;
    }

    private void SyncConfigFromViewModels()
    {
        FlushSlotsToScene();

        _config.Devices.Clear();
        foreach (var d in Devices)
            _config.Devices[d.Name] = d.ToConfig();

        _config.Scenes.Clear();
        foreach (var dev in Devices)
            _config.Scenes[dev.Name] = dev.ToSceneConfig();
    }
}
