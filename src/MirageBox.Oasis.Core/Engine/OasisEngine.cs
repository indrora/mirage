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
    private readonly Dictionary<string, IDataSource> _dataSources = new();
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

        foreach (var ds in _dataSources.Values)
        {
            await ds.StopAsync();
            ds.Dispose();
        }
        _dataSources.Clear();

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
        {
            var source = PluginLoader.Create(dsConfig.Plugin);
            if (source == null)
            {
                Console.Error.WriteLine($"Warning: Could not load data source plugin '{dsConfig.Plugin}' for '{name}'");
                continue;
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
        }
    }

    private async Task ConnectDevices()
    {
        foreach (var (deviceName, deviceConfig) in _config.Devices)
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
                continue;
            }

            SceneManager? sceneManager = null;
            if (_config.Scenes.TryGetValue(deviceName, out var sceneConfig))
            {
                sceneManager = new SceneManager(sceneConfig);
                _sceneManagers[deviceName] = sceneManager;
            }
            else
            {
                continue;
            }

            var orchestrator = new DeviceOrchestrator(
                deviceName, device, sceneManager, _config,
                _dataSources, _rendererRegistry, _actionExecutor,
                ResolveFont);

            _orchestrators[deviceName] = orchestrator;
            await orchestrator.StartAsync();
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
