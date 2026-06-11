using System.Collections.Concurrent;
using System.Text.Json;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;
using SkiaSharp;

namespace MirageBox.Oasis.Core.Engine;

public class OasisEngine : IDisposable
{
    private OasisConfig _config;
    private readonly RendererRegistry _rendererRegistry;
    private readonly ActionExecutor _actionExecutor;
    // Concurrent: device render loops read while config edits hot-swap entries.
    private readonly ConcurrentDictionary<string, IDataSource> _dataSources = new();
    private readonly Dictionary<string, string> _dataSourceFingerprints = new();
    private readonly Dictionary<string, string> _deviceFingerprints = new();
    private readonly Dictionary<string, SceneManager> _sceneManagers = new();
    private readonly Dictionary<string, DeviceOrchestrator> _orchestrators = new();
    private readonly Dictionary<string, SKTypeface> _fontCache = new();
    private readonly List<string> _contentDirs = new();
    private CancellationTokenSource? _cts;

    public OasisEngine(OasisConfig config)
    {
        _config = config;
        _rendererRegistry = new RendererRegistry();
        _actionExecutor = new ActionExecutor(
            name => _sceneManagers.TryGetValue(name, out var sm) ? sm : null,
            _dataSources);
    }

    /// <summary>Returns the running data source registered under the given config name, if any.</summary>
    public IDataSource? GetDataSource(string name)
        => _dataSources.TryGetValue(name, out var ds) ? ds : null;

    /// <summary>
    /// Renders a gauge for the editor's tile previews (PNG).
    /// live: true → current sensor value; false → a static safe value
    /// (midpoint of the gauge's resolved range).
    /// </summary>
    public byte[]? RenderGaugePreview(string gaugeName, int size = 96, bool live = true)
    {
        if (!_config.Gauges.TryGetValue(gaugeName, out var gaugeConfig))
            return null;

        float? valueOverride = null;
        if (!live)
        {
            _dataSources.TryGetValue(gaugeConfig.Source, out var source);
            var (min, max) = GaugeRenderer.ResolveRange(gaugeConfig, source);
            valueOverride = min + (max - min) / 2f;
        }

        return GaugeRenderer.Render(gaugeConfig, _config, _dataSources, _rendererRegistry,
            ResolveFont, size, size, SkiaSharp.SKEncodedImageFormat.Png, quality: 100, valueOverride);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ResolveContentDirs();
        await InitializeDataSources(_cts.Token);
        await ConnectDevices();
    }

    public async Task StopAsync()
    {
        foreach (var orch in _orchestrators.Values)
            await orch.StopAsync();
        _orchestrators.Clear();
        _deviceFingerprints.Clear();

        foreach (var ds in _dataSources.Values)
        {
            await ds.StopAsync();
            ds.Dispose();
        }
        _dataSources.Clear();
        _dataSourceFingerprints.Clear();

        _cts?.Cancel();
    }

    private void ResolveContentDirs()
    {
        _contentDirs.Clear();
        foreach (var dir in _config.ContentDirs)
        {
            var resolved = dir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            _contentDirs.Add(resolved);
        }
        _contentDirs.Add(Path.Combine(AppContext.BaseDirectory, "content"));
    }

    private async Task InitializeDataSources(CancellationToken ct)
    {
        foreach (var (name, dsConfig) in _config.DataSources)
            await StartDataSource(name, dsConfig, ct);
    }

    private async Task StartDataSource(string name, DataSourceConfig dsConfig, CancellationToken ct)
    {
        var source = PluginLoader.Create(dsConfig.Plugin);
        if (source == null)
        {
            Console.Error.WriteLine($"Warning: Could not load data source plugin '{dsConfig.Plugin}' for '{name}'");
            return;
        }

        var pluginConfig = new Dictionary<string, object?>();
        if (dsConfig.Config != null)
        {
            foreach (var (k, v) in dsConfig.Config)
                pluginConfig[k] = JsonElementToObject(v);
        }

        await source.InitializeAsync(pluginConfig);
        await source.StartAsync(ct);
        _dataSources[name] = source;
        _dataSourceFingerprints[name] = Fingerprint(dsConfig);
    }

    private static string Fingerprint(DataSourceConfig dsConfig)
        => JsonSerializer.Serialize(dsConfig);

    /// <summary>
    /// Hot-applies data source config changes without an engine restart:
    /// sources whose plugin or config changed (or that are new/removed) are
    /// stopped and recreated in place; unchanged ones keep running.
    /// </summary>
    public async Task ApplyDataSourceChangesAsync()
    {
        var ct = _cts?.Token ?? CancellationToken.None;

        foreach (var name in _dataSources.Keys.Except(_config.DataSources.Keys).ToList())
        {
            if (_dataSources.TryRemove(name, out var removed))
            {
                await removed.StopAsync();
                removed.Dispose();
            }
            _dataSourceFingerprints.Remove(name);
        }

        foreach (var (name, dsConfig) in _config.DataSources)
        {
            if (_dataSourceFingerprints.TryGetValue(name, out var fp)
                && fp == Fingerprint(dsConfig)
                && _dataSources.ContainsKey(name))
                continue;

            if (_dataSources.TryRemove(name, out var old))
            {
                await old.StopAsync();
                old.Dispose();
            }
            await StartDataSource(name, dsConfig, ct);
        }
    }

    private async Task ConnectDevices()
    {
        foreach (var (deviceName, deviceConfig) in _config.Devices)
            await ConnectDevice(deviceName, deviceConfig);
    }

    private async Task ConnectDevice(string deviceName, DeviceConfig deviceConfig)
    {
        IMirageDevice? device = null;

        if (deviceConfig.Simulator)
        {
            device = new SimulatorDevice(
                displayButtons: deviceConfig.Buttons,
                sideButtons: deviceConfig.Tactile,
                imgSize: deviceConfig.ImageSize);
        }
        else if (deviceConfig.Serial != null)
        {
            device = DeviceFactory.FindDeviceBySerial(deviceConfig.Serial);
        }

        if (device == null)
        {
            Console.Error.WriteLine($"Warning: Device '{deviceName}' not found (serial: {deviceConfig.Serial})");
            return;
        }

        if (!_config.Scenes.TryGetValue(deviceName, out var sceneConfig))
        {
            sceneConfig = new DeviceSceneConfig();
            _config.Scenes[deviceName] = sceneConfig;
        }
        var sceneManager = new SceneManager(sceneConfig);
        _sceneManagers[deviceName] = sceneManager;

        var orchestrator = new DeviceOrchestrator(
            deviceName, device, sceneManager, _config,
            _dataSources, _rendererRegistry, _actionExecutor,
            ResolveFont);

        _orchestrators[deviceName] = orchestrator;
        _deviceFingerprints[deviceName] = JsonSerializer.Serialize(deviceConfig);
        await orchestrator.StartAsync();
    }

    private async Task DisconnectDevice(string deviceName)
    {
        if (_orchestrators.Remove(deviceName, out var orch))
        {
            await orch.StopAsync();
            orch.Dispose();
        }
        _sceneManagers.Remove(deviceName);
        _deviceFingerprints.Remove(deviceName);
    }

    /// <summary>
    /// Hot-applies the current config to the running engine: data sources,
    /// devices, and scenes are diffed and only what changed is touched.
    /// Scene/button layout changes apply without reconnecting the device.
    /// </summary>
    public async Task ApplyConfigChangesAsync()
    {
        await ApplyDataSourceChangesAsync();

        // Devices: removed → disconnect; new/changed → (re)connect.
        foreach (var name in _orchestrators.Keys.Except(_config.Devices.Keys).ToList())
            await DisconnectDevice(name);

        foreach (var (name, deviceConfig) in _config.Devices)
        {
            var fp = JsonSerializer.Serialize(deviceConfig);
            if (_orchestrators.ContainsKey(name)
                && _deviceFingerprints.TryGetValue(name, out var old) && old == fp)
                continue;

            await DisconnectDevice(name);
            await ConnectDevice(name, deviceConfig);
        }

        // Scenes: hot-swap into the existing scene managers (no device restart).
        foreach (var (name, sm) in _sceneManagers)
        {
            if (_config.Scenes.TryGetValue(name, out var sceneConfig))
                sm.UpdateConfig(sceneConfig);
        }
    }

    private SKTypeface? ResolveFont(string fontName)
    {
        if (_fontCache.TryGetValue(fontName, out var cached))
            return cached;

        foreach (var dir in _contentDirs)
        {
            var path = Path.Combine(dir, fontName);
            if (File.Exists(path))
            {
                var tf = SKTypeface.FromFile(path);
                if (tf != null)
                {
                    _fontCache[fontName] = tf;
                    return tf;
                }
            }
        }

        return null;
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString()
    };

    public void Dispose()
    {
        foreach (var orch in _orchestrators.Values)
            orch.Dispose();
        foreach (var ds in _dataSources.Values)
            ds.Dispose();
        foreach (var tf in _fontCache.Values)
            tf.Dispose();
        _cts?.Dispose();
    }
}
