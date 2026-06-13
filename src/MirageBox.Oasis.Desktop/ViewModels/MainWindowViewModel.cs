using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaDialogs.Views;
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

    [ObservableProperty] private bool _livePreviews = true;
    private DispatcherTimer? _previewTimer;

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

        // MIRAGE_CONFIG points the editor at an alternate config (testing/dev).
        _configPath = Environment.GetEnvironmentVariable("MIRAGE_CONFIG")
                      ?? ConfigLoader.DefaultConfigPath;
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

    /// <summary>
    /// Adopts attached hardware that no config entry references into the live config
    /// (standard starter layout, clock on button 0). Keyed on serial and idempotent, so
    /// it is safe to run on every refresh: first launch with an empty config, Revert,
    /// Import, and Reset all funnel through RefreshFromConfig and get the same behavior.
    /// Without this, a discovered device that the config never mentioned was invisible —
    /// the UI only renders config entries (this was the "blank UI on a fresh machine" bug).
    /// </summary>
    private void AdoptDiscoveredHardware()
    {
        foreach (var device in _discoveredDevices.Values)
        {
            var name = DefaultConfigFactory.AddHardwareDevice(_config, device);
            if (name != null)
                StatusText = $"Found new device: {device.Profile.Name} ({device.SerialNumber})";
        }
    }

    private void RefreshFromConfig()
    {
        AdoptDiscoveredHardware();

        Devices.Clear();
        DataSources.Clear();

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
            DataSources.Add(new DataSourceViewModel(name, ds));

        // Name lists are diff-synced, never Clear()ed: see MergeNames for why.
        SyncNames(DataSourceNames, _config.DataSources.Keys);
        SyncNames(GaugeNames, _config.Gauges.Keys);

        if (Devices.Count > 0)
            SelectedDevice = Devices[0];
    }

    /// <summary>
    /// Brings <paramref name="target"/> up to date with <paramref name="desired"/> by
    /// inserting missing names and reordering existing ones — WITHOUT removing anything.
    /// Stale names drift to the tail and are dropped later by <see cref="PruneNames"/>.
    /// <para>
    /// The split exists because these collections back ComboBoxes whose SelectedItem is
    /// two-way bound into view models (e.g. the slot editor's gauge picker →
    /// SelectedSlot.GaugeName). If the selected name leaves the collection even for an
    /// instant — which the old Clear()-then-re-add pattern guaranteed — the ComboBox
    /// resets its selection to null and that null is written back through the binding,
    /// silently unassigning the gauge and hot-applying the loss to the device.
    /// Add-then-prune means a name only ever disappears when it is genuinely gone.
    /// </para>
    /// </summary>
    private static void MergeNames(ObservableCollection<string> target, IEnumerable<string> desired)
    {
        int insertAt = 0;
        foreach (var name in desired)
        {
            var existingIndex = target.IndexOf(name);
            if (existingIndex < 0)
                target.Insert(insertAt, name);
            else if (existingIndex != insertAt)
                target.Move(existingIndex, insertAt);
            insertAt++;
        }
    }

    /// <summary>Removes names absent from <paramref name="desired"/>. See <see cref="MergeNames"/>.</summary>
    private static void PruneNames(ObservableCollection<string> target, IEnumerable<string> desired)
    {
        var keep = new HashSet<string>(desired);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!keep.Contains(target[i]))
                target.RemoveAt(i);
    }

    /// <summary>Merge + prune in one step, for refreshes with no rename pass in between.</summary>
    private static void SyncNames(ObservableCollection<string> target, IEnumerable<string> desired)
    {
        MergeNames(target, desired);
        PruneNames(target, desired);
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
    }

    /// <summary>Maps a data source config name to its plugin type (for action pickers).</summary>
    private Type? ResolveSourceType(string sourceName)
    {
        var ds = DataSources.FirstOrDefault(d => d.Name == sourceName);
        return ds == null ? null : PluginLoader.ResolveType(ds.Plugin);
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The instant a slot is pinned, materialize the pinned pseudo-scene so the
        // upcoming flush has somewhere to file the button. Without this (and the
        // matching guarantee in FlushSlotsToScene) a first-ever pin was dropped on
        // the floor by pinnedScene?.Buttons.Add(...) against a null scene.
        if (e.PropertyName == nameof(ButtonSlotViewModel.IsPinned))
            SelectedDevice?.GetOrCreatePinnedScene();

        if (e.PropertyName is not (nameof(ButtonSlotViewModel.IsSelected)
            or nameof(ButtonSlotViewModel.Preview)
            or nameof(ButtonSlotViewModel.PreviewSourceLine)))
            ScheduleApply();

        if (e.PropertyName == nameof(ButtonSlotViewModel.GaugeName))
            RefreshPreviews(force: true);
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

        var pinnedScene = SelectedDevice.GetOrCreatePinnedScene();
        var activeScene = SelectedScene;

        for (int i = 0; i < SelectedDevice.Buttons; i++)
        {
            var idx = i.ToString();
            var pinnedBtn = pinnedScene?.Buttons.FirstOrDefault(b => b.ButtonIndex == idx);
            var sceneBtn = activeScene?.Buttons.FirstOrDefault(b => b.ButtonIndex == idx);

            var slot = new ButtonSlotViewModel(i, "display", ResolveSourceType, DataSourceNames);
            var btn = pinnedBtn ?? sceneBtn;
            if (btn != null)
            {
                slot.IsPinned = pinnedBtn != null;
                slot.GaugeName = btn.Gauge;
                LoadSlotActions(slot, btn);
            }
            DisplaySlots.Add(slot);
        }

        for (int i = 0; i < SelectedDevice.Tactile; i++)
        {
            var idx = i.ToString();
            var slot = new ButtonSlotViewModel(i, "tactile", ResolveSourceType, DataSourceNames);
            var btn = activeScene?.TactileButtons.FirstOrDefault(b => b.ButtonIndex == idx);
            if (btn != null) LoadSlotActions(slot, btn);
            TactileSlots.Add(slot);
        }

        for (int i = 0; i < SelectedDevice.Encoders; i++)
        {
            var idx = i.ToString();
            var slot = new ButtonSlotViewModel(i, "encoder", ResolveSourceType, DataSourceNames);
            var btn = activeScene?.Encoders.FirstOrDefault(b => b.ButtonIndex == idx);
            if (btn != null) LoadSlotActions(slot, btn);
            EncoderSlots.Add(slot);
        }

        _lastFlushedScene = activeScene;
        RefreshPreviews(force: true);
    }

    private static void LoadSlotActions(ButtonSlotViewModel slot, DeviceButtonViewModel btn)
    {
        slot.Press.LoadConfig(btn.Action);
        slot.DoublePress.LoadConfig(btn.DoublePressAction);
        slot.Hold.LoadConfig(btn.HoldAction);
    }

    private void FlushSlotsToScene()
    {
        if (SelectedDevice == null || _lastFlushedScene == null) return;
        if (DisplaySlots.Count == 0) return;

        // Never a null lookup: a missing pinned scene used to make the IsPinned branch
        // below silently discard the button (the pin-never-lands bug).
        var pinnedScene = SelectedDevice.GetOrCreatePinnedScene();

        pinnedScene.Buttons.Clear();
        _lastFlushedScene.Buttons.Clear();
        _lastFlushedScene.TactileButtons.Clear();
        _lastFlushedScene.Encoders.Clear();

        foreach (var slot in DisplaySlots)
        {
            if (slot.IsEmpty) continue;
            var btn = SlotToButton(slot, slot.GaugeName, slot.IsPinned);

            if (slot.IsPinned)
                pinnedScene.Buttons.Add(btn);
            else
                _lastFlushedScene.Buttons.Add(btn);
        }

        foreach (var slot in TactileSlots)
        {
            if (slot.IsEmpty) continue;
            _lastFlushedScene.TactileButtons.Add(SlotToButton(slot, null, false));
        }

        foreach (var slot in EncoderSlots)
        {
            if (slot.IsEmpty) continue;
            _lastFlushedScene.Encoders.Add(SlotToButton(slot, null, false));
        }
    }

    private static DeviceButtonViewModel SlotToButton(ButtonSlotViewModel slot, string? gauge, bool pinned)
        => new(slot.Index.ToString(), gauge,
            slot.Press.ToConfig(), slot.DoublePress.ToConfig(), slot.Hold.ToConfig(), pinned);

    public ManageGaugesViewModel CreateManageGaugesViewModel() =>
        new(_config.Gauges, _config.DataSources, DataSourceNames, RendererTypes, _rendererRegistry,
            name => _engine?.GetDataSource(name),
            // Renames mutate the live gauge dictionary the moment they're committed in
            // the dialog, so retarget slots/scenes/previews right away — otherwise every
            // tile (and hardware button) holding the old name renders dark until the
            // dialog closes. The close-time RefreshGaugeNames pass is now a no-op net.
            onRenamed: (oldName, newName) => RefreshGaugeNames([(oldName, newName)]));

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

    /// <summary>
    /// Refreshes GaugeNames after the Manage Gauges dialog closes, in three phases so
    /// the selected slot's gauge survives (see <see cref="MergeNames"/> for the binding
    /// hazard this avoids):
    ///   1. merge — new names become selectable, nothing is removed yet;
    ///   2. renames — slots/buttons retarget old → new while BOTH names are still in the
    ///      collection, so the combobox follows the rename instead of resetting to null;
    ///   3. prune — only names whose gauge was genuinely deleted disappear, and only
    ///      then does a slot pointing at one of them legitimately lose its selection.
    /// </summary>
    public void RefreshGaugeNames(IReadOnlyList<(string Old, string New)>? renames = null)
    {
        MergeNames(GaugeNames, _config.Gauges.Keys);
        if (renames is { Count: > 0 })
            ApplyGaugeRenames(renames);
        PruneNames(GaugeNames, _config.Gauges.Keys);
    }

    /// <summary>
    /// Retargets every scene button and live slot after gauges were renamed in the
    /// Manage Gauges dialog. Runs inside <see cref="RefreshGaugeNames"/> between the
    /// merge and prune phases — both the old and new names must be present in
    /// GaugeNames while this executes (see there for ordering rationale).
    /// </summary>
    private void ApplyGaugeRenames(IReadOnlyList<(string Old, string New)> renames)
    {
        if (renames.Count == 0) return;

        foreach (var (oldName, newName) in renames)
        {
            foreach (var device in Devices)
            foreach (var scene in device.Scenes)
            foreach (var btn in scene.Buttons.Concat(scene.TactileButtons).Concat(scene.Encoders))
            {
                if (btn.Gauge == oldName)
                    btn.Gauge = newName;
            }

            foreach (var slot in DisplaySlots)
            {
                if (slot.GaugeName == oldName)
                    slot.GaugeName = newName;
            }
        }

        ScheduleApply();
        RefreshPreviews(force: true);
    }

    public void RefreshDataSourceNames()
    {
        // Same diff-sync as GaugeNames (see MergeNames): today nothing two-way binds a
        // SelectedItem to DataSourceNames, but keeping the collections on one pattern
        // means a future picker can't reintroduce the silent-unassign bug.
        SyncNames(DataSourceNames, _config.DataSources.Keys);

        DataSources.Clear();
        foreach (var (name, ds) in _config.DataSources)
            DataSources.Add(new DataSourceViewModel(name, ds));
    }

    public void SwapSlots(string slotType, int fromIndex, int toIndex)
    {
        var collection = slotType == "display" ? DisplaySlots : TactileSlots;
        if (fromIndex < 0 || fromIndex >= collection.Count) return;
        if (toIndex < 0 || toIndex >= collection.Count) return;

        var from = collection[fromIndex];
        var to = collection[toIndex];

        var tmpGauge = from.GaugeName;
        var tmpPinned = from.IsPinned;
        var tmpPress = from.Press.ToConfig();
        var tmpDouble = from.DoublePress.ToConfig();
        var tmpHold = from.Hold.ToConfig();

        from.GaugeName = to.GaugeName;
        from.IsPinned = to.IsPinned;
        from.Press.LoadConfig(to.Press.ToConfig());
        from.DoublePress.LoadConfig(to.DoublePress.ToConfig());
        from.Hold.LoadConfig(to.Hold.ToConfig());

        to.GaugeName = tmpGauge;
        to.IsPinned = tmpPinned;
        to.Press.LoadConfig(tmpPress);
        to.DoublePress.LoadConfig(tmpDouble);
        to.Hold.LoadConfig(tmpHold);

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
    private async Task RenameScene()
    {
        if (SelectedDevice == null || SelectedScene == null || SelectedScene.IsPinned) return;
        var device = SelectedDevice;
        var scene = SelectedScene;
        var oldName = scene.Name;

        var dialog = new Views.TextInputDialog
        {
            Message = $"Rename scene '{oldName}'",
            Text = oldName,
            PositiveText = "Rename",
            Validate = value =>
                string.IsNullOrWhiteSpace(value) ? "Name cannot be empty."
                : value != oldName && device.Scenes.Any(s => s.Name == value) ? $"A scene named '{value}' already exists."
                : null,
        };
        var result = await dialog.ShowAsync();
        if (!result.HasValue || result.Value == oldName) return;
        var newName = result.Value;

        scene.Name = newName;
        if (device.ActiveScene == oldName)
            device.ActiveScene = newName;

        // Follow switchScene actions pointing at the old name: both the
        // persisted button configs and the live slot editors.
        foreach (var s in device.Scenes)
        foreach (var btn in s.Buttons.Concat(s.TactileButtons).Concat(s.Encoders))
        {
            RetargetSwitchSceneAction(btn.Action, oldName, newName);
            RetargetSwitchSceneAction(btn.DoublePressAction, oldName, newName);
            RetargetSwitchSceneAction(btn.HoldAction, oldName, newName);
        }
        foreach (var slot in DisplaySlots.Concat(TactileSlots).Concat(EncoderSlots))
        foreach (var action in new[] { slot.Press, slot.DoublePress, slot.Hold })
        {
            if (action.ActionType == "switchScene" && action.ActionParam == oldName)
                action.ActionParam = newName;
        }

        StatusText = $"Renamed scene '{oldName}' to '{newName}'";
        ScheduleApply();
    }

    private static void RetargetSwitchSceneAction(ActionConfig? action, string oldName, string newName)
    {
        if (action is not { Type: "switchScene", Parameters: { } parameters }) return;
        if (parameters.TryGetValue("scene", out var el)
            && el.ValueKind == System.Text.Json.JsonValueKind.String
            && el.GetString() == oldName)
        {
            parameters["scene"] = System.Text.Json.JsonSerializer.SerializeToElement(newName);
        }
    }
    
    [RelayCommand]
    private void DupeScene()
    {
        if (SelectedDevice == null || SelectedScene == null || SelectedScene.IsPinned) return;
        FlushSlotsToScene();

        var name = $"{SelectedScene.Name}-copy";
        var i = 2;
        while (SelectedDevice.Scenes.Any(s => s.Name == name))
            name = $"{SelectedScene.Name}-copy{i++}";

        var copy = new SceneViewModel(name, false);
        foreach (var btn in SelectedScene.Buttons)
            copy.Buttons.Add(DeviceButtonViewModel.FromConfig(btn.ButtonIndex, btn.ToConfig(), false));
        foreach (var btn in SelectedScene.TactileButtons)
            copy.TactileButtons.Add(DeviceButtonViewModel.FromConfig(btn.ButtonIndex, btn.ToConfig(), false));
        foreach (var btn in SelectedScene.Encoders)
            copy.Encoders.Add(DeviceButtonViewModel.FromConfig(btn.ButtonIndex, btn.ToConfig(), false));

        SelectedDevice.Scenes.Add(copy);
        DeviceScenes.Add(copy);
        SelectedScene = copy;
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
            SetupPreviewTimer();
            RefreshPreviews(force: true);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void SetupPreviewTimer()
    {
        _previewTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => RefreshPreviews(force: false));
        if (LivePreviews) _previewTimer.Start();
    }

    partial void OnLivePreviewsChanged(bool value)
    {
        if (value)
        {
            _previewTimer?.Start();
            RefreshPreviews(force: true);
        }
        else
        {
            _previewTimer?.Stop();
            RefreshPreviews(force: true);   // one static safe-value render
        }
    }

    /// <summary>
    /// Renders tile previews. Live mode re-renders every tick; static mode
    /// renders once (force) at a safe mid-range value.
    /// </summary>
    private void RefreshPreviews(bool force)
    {
        if (_engine == null) return;
        if (!LivePreviews && !force) return;

        foreach (var slot in DisplaySlots)
        {
            if (slot.GaugeName is not { } gaugeName || gaugeName.Length == 0)
            {
                slot.Preview = null;
                slot.PreviewSourceLine = null;
                continue;
            }

            try
            {
                var png = _engine.RenderGaugePreview(gaugeName, size: 96, live: LivePreviews);
                slot.Preview = png != null ? new Bitmap(new MemoryStream(png)) : null;
            }
            catch
            {
                slot.Preview = null;
            }

            slot.PreviewSourceLine = _config.Gauges.TryGetValue(gaugeName, out var gc)
                && _config.DataSources.TryGetValue(gc.Source, out var ds)
                ? gc.Renderer.Type == SourceRenderer.RendererType
                    ? $"{ds.Plugin} · sensor renderer"
                    : ds.Plugin
                : null;
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
        // Standard starter layout for whatever is plugged in right now (clock on button 0,
        // builtin clock source/gauge) — the same defaults a fresh install gets.
        var fresh = DefaultConfigFactory.Create(_discoveredDevices.Values);

        // Physical hardware is never removed — config entries for devices that are not
        // currently attached (unplugged, other machine) carry over with a blank layout.
        foreach (var (name, dev) in _config.Devices.Where(kv => !kv.Value.Simulator))
        {
            if (dev.Serial != null && fresh.Devices.Values.Any(d => d.Serial == dev.Serial))
                continue;

            var key = name;
            for (int i = 2; fresh.Devices.ContainsKey(key); i++)
                key = $"{name}{i}";

            fresh.Devices[key] = dev;
            fresh.Scenes[key] = new DeviceSceneConfig
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
