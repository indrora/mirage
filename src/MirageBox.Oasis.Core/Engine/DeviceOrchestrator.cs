using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;
using MirageBox.TinyGauges;
using SkiaSharp;

namespace MirageBox.Oasis.Core.Engine;

public class DeviceOrchestrator : IDisposable
{
    private readonly string _deviceName;
    private readonly IMirageDevice _device;
    private readonly SceneManager _sceneManager;
    private readonly OasisConfig _config;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDataSource> _dataSources;
    private readonly RendererRegistry _rendererRegistry;
    private readonly ActionExecutor _actionExecutor;
    private readonly Func<string, SKTypeface?> _resolveFont;

    private CancellationTokenSource? _renderCts;
    private Task? _renderTask;
    private readonly bool[] _dirty;

    public DeviceOrchestrator(
        string deviceName,
        IMirageDevice device,
        SceneManager sceneManager,
        OasisConfig config,
        System.Collections.Concurrent.ConcurrentDictionary<string, IDataSource> dataSources,
        RendererRegistry rendererRegistry,
        ActionExecutor actionExecutor,
        Func<string, SKTypeface?> resolveFont)
    {
        _deviceName = deviceName;
        _device = device;
        _sceneManager = sceneManager;
        _config = config;
        _dataSources = dataSources;
        _rendererRegistry = rendererRegistry;
        _actionExecutor = actionExecutor;
        _resolveFont = resolveFont;
        _dirty = new bool[device.ImageButtonCount];
        Array.Fill(_dirty, true);
    }

    public async Task StartAsync()
    {
        await _device.InitializeAsync();
        await _device.StartListeningAsync();

        _device.ImageButtonChanged += OnImageButtonChanged;
        _device.TactileButtonChanged += OnTactileButtonChanged;
        _device.EncoderRotated += OnEncoderRotated;

        _renderCts = new CancellationTokenSource();
        _renderTask = RenderLoopAsync(_renderCts.Token);
    }

    public async Task StopAsync()
    {
        _renderCts?.Cancel();
        if (_renderTask != null)
            await _renderTask;
        await _device.StopListeningAsync();
    }

    private void OnImageButtonChanged(object? sender, ImageButtonEventArgs e)
    {
        if (!e.IsPressed) return;
        var resolved = _sceneManager.GetButton(e.ButtonIndex);
        if (resolved == null) return;
        ExecuteOrDefault(resolved);
        MarkAllDirty();
    }

    private void OnTactileButtonChanged(object? sender, TactileButtonEventArgs e)
    {
        if (!e.IsPressed) return;
        var resolved = _sceneManager.GetTactileButton(e.ButtonIndex);
        if (resolved != null)
            ExecuteOrDefault(resolved);
    }

    private void OnEncoderRotated(object? sender, EncoderEventArgs e)
    {
        var resolved = _sceneManager.GetEncoder(e.EncoderIndex);
        if (resolved != null)
            ExecuteOrDefault(resolved);
    }

    private void ExecuteOrDefault(ResolvedButton resolved)
    {
        if (resolved.Action != null)
        {
            _actionExecutor.Execute(resolved.Action, _deviceName);
            return;
        }

        if (resolved.GaugeName == null) return;
        if (!_config.Gauges.TryGetValue(resolved.GaugeName, out var gauge)) return;
        if (!_dataSources.TryGetValue(gauge.Source, out var source)) return;

        var defaultAction = SourceActionHelper.GetDefaultAction(source.GetType());
        if (defaultAction != null)
            SourceActionHelper.ExecuteAction(source, defaultAction, null);
    }

    private void MarkAllDirty()
    {
        Array.Fill(_dirty, true);
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        const int targetFps = 30;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        while (!ct.IsCancellationRequested)
        {
            var frameStart = DateTime.UtcNow;

            try
            {
                for (int i = 0; i < _device.ImageButtonCount; i++)
                {
                    var resolved = _sceneManager.GetButton(i);
                    if (resolved?.GaugeName == null) continue;
                    if (!_config.Gauges.TryGetValue(resolved.GaugeName, out var gaugeConfig)) continue;

                    var jpeg = RenderGauge(gaugeConfig);
                    if (jpeg != null)
                        await _device.SetButtonImageNoFlushAsync(i, jpeg);
                }
                await _device.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Render error on {_deviceName}: {ex.Message}");
            }

            var elapsed = DateTime.UtcNow - frameStart;
            var delay = frameInterval - elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private byte[]? RenderGauge(Config.GaugeConfig gaugeConfig)
    {
        if (!_dataSources.TryGetValue(gaugeConfig.Source, out var source))
            return null;

        var sensorValue = source.GetValue(gaugeConfig.Sensor);
        var theme = ResolveTheme(gaugeConfig.Theme);
        var typeface = _resolveFont(gaugeConfig.Font ?? _config.Defaults.Font);

        RenderFunc? renderer = null;
        if (gaugeConfig.Renderer.Type == "__source__")
        {
            renderer = source.GetCustomRenderer(gaugeConfig.Sensor);
        }

        var sensorInfo = source.GetAvailableSensors()
            .FirstOrDefault(s => s.Path == gaugeConfig.Sensor);
        var sensorType = sensorInfo?.Type ?? SensorValueType.Numeric;

        renderer ??= _rendererRegistry.Resolve(gaugeConfig.Renderer.Type, sensorType, gaugeConfig.Renderer.Parameters);
        if (renderer == null) return null;

        var width = _device.ImageWidth;
        var height = _device.ImageHeight;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        var bounds = new SKRect(0, 0, width, height);

        if (sensorType == SensorValueType.Text || sensorValue.IsText)
        {
            var textLabel = sensorValue.Text ?? gaugeConfig.Label ?? "";
            var rv = new RangedValue(0, 1, 0);
            renderer(canvas, theme, typeface!, bounds, textLabel, rv);
        }
        else
        {
            var range = (source as IRangedDataSource)?.GetRange(gaugeConfig.Sensor);
            float min = gaugeConfig.Min ?? range?.Min ?? 0;
            float max = gaugeConfig.Max ?? range?.Max ?? 100;
            if (max <= min) { min = 0; max = 100; }
            var rv = new RangedValue(min, max, sensorValue.Numeric ?? 0);
            renderer(canvas, theme, typeface!, bounds, gaugeConfig.Label, rv);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private Theme ResolveTheme(string? themeName)
    {
        themeName ??= _config.Defaults.Theme;
        if (!_config.Themes.TryGetValue(themeName, out var tc))
            return Theme.Default;

        return new Theme
        {
            PrimaryColor = ParseColor(tc.Primary) ?? Theme.Default.PrimaryColor,
            SecondaryColor = ParseColor(tc.Secondary) ?? Theme.Default.SecondaryColor,
            BackgroundColor = ParseColor(tc.Background) ?? Theme.Default.BackgroundColor,
            TextColor = ParseColor(tc.Text) ?? Theme.Default.TextColor,
            Accents = tc.Accents?.Select(a => ParseColor(a) ?? SKColors.White).ToArray()
                      ?? Theme.Default.Accents,
        };
    }

    private static SKColor? ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        return SKColor.TryParse(hex, out var color) ? color : null;
    }

    public void Dispose()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _device.Dispose();
    }
}
