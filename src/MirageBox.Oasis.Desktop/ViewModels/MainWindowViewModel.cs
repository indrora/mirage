using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private OasisConfig _config;
    private OasisEngine? _engine;
    private CancellationTokenSource? _engineCts;
    private readonly string _configPath;
    private readonly RendererRegistry _rendererRegistry = new();

    [ObservableProperty] private string _statusText = "Stopped";
    [ObservableProperty] private bool _isRunning;

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
        _configPath = ConfigLoader.DefaultConfigPath;
        _config = ConfigLoader.Load(_configPath);
        RendererTypes = _rendererRegistry.GetAll().Select(r => r.Name).Order().ToList();
        DiscoverHardware();
        RefreshFromConfig();
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
        new(_config.Gauges, DataSourceNames, RendererTypes);

    public ManageDataSourcesViewModel CreateManageDataSourcesViewModel() =>
        new(_config.DataSources);

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
    }

    [RelayCommand]
    private void AddDevice()
    {
        var name = $"device{Devices.Count + 1}";
        var vm = new DeviceViewModel(name, new DeviceConfig { Simulator = true });
        Devices.Add(vm);
        SelectedDevice = vm;
    }

    [RelayCommand]
    private void RemoveDevice()
    {
        if (SelectedDevice == null) return;
        var idx = Devices.IndexOf(SelectedDevice);
        Devices.Remove(SelectedDevice);
        SelectedDevice = Devices.Count > 0
            ? Devices[Math.Min(idx, Devices.Count - 1)]
            : null;
    }

    [RelayCommand]
    private async Task StartEngine()
    {
        if (IsRunning) return;
        try
        {
            SyncConfigFromViewModels();
            _engine = new OasisEngine(_config);
            _engineCts = new CancellationTokenSource();
            await _engine.StartAsync(_engineCts.Token);
            IsRunning = true;
            StatusText = $"Running — {_config.Devices.Count} device(s), {_config.Gauges.Count} gauge(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopEngine()
    {
        if (!IsRunning || _engine == null) return;
        _engineCts?.Cancel();
        await _engine.StopAsync();
        _engine.Dispose();
        _engine = null;
        IsRunning = false;
        StatusText = "Stopped";
    }

    [RelayCommand]
    private async Task RestartEngine()
    {
        await StopEngine();
        await StartEngine();
    }

    [RelayCommand]
    private void SaveConfig()
    {
        SyncConfigFromViewModels();
        ConfigLoader.Save(_config, _configPath);
        StatusText = "Config saved";
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        _config = ConfigLoader.Load(_configPath);
        RefreshFromConfig();
        StatusText = "Config reloaded";
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
