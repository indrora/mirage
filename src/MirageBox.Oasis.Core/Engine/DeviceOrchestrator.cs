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
    private readonly ButtonPressClassifier _displayClassifier;
    private readonly ButtonPressClassifier _tactileClassifier;

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
        _displayClassifier = new ButtonPressClassifier(
            (idx, kind) => HandleClassifiedPress(_sceneManager.GetButton(idx), kind, markDirty: true));
        _tactileClassifier = new ButtonPressClassifier(
            (idx, kind) => HandleClassifiedPress(_sceneManager.GetTactileButton(idx), kind, markDirty: false));
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
        RouteEdge(_displayClassifier, _sceneManager.GetButton(e.ButtonIndex),
            e.ButtonIndex, e.IsPressed, markDirty: true);
    }

    private void OnTactileButtonChanged(object? sender, TactileButtonEventArgs e)
    {
        RouteEdge(_tactileClassifier, _sceneManager.GetTactileButton(e.ButtonIndex),
            e.ButtonIndex, e.IsPressed, markDirty: false);
    }

    private void RouteEdge(ButtonPressClassifier classifier, ResolvedButton? resolved,
        int index, bool isPressed, bool markDirty)
    {
        if (resolved == null) return;

        // Zero-latency fast path: nothing bound to double/hold, fire on press.
        if (!resolved.HasMultiPressActions)
        {
            if (isPressed)
                HandleClassifiedPress(resolved, PressKind.Single, markDirty);
            return;
        }

        classifier.OnEdge(index, isPressed);
    }

    private void HandleClassifiedPress(ResolvedButton? resolved, PressKind kind, bool markDirty)
    {
        if (resolved == null) return;

        var action = kind switch
        {
            PressKind.Double => resolved.DoublePressAction,
            PressKind.Hold => resolved.HoldAction,
            _ => resolved.Action,
        };

        if (action != null)
            _actionExecutor.Execute(action, _deviceName);
        else if (kind == PressKind.Single)
            ExecuteSourceDefault(resolved);

        if (markDirty)
            MarkAllDirty();
    }

    private void OnEncoderRotated(object? sender, EncoderEventArgs e)
    {
        var resolved = _sceneManager.GetEncoder(e.EncoderIndex);
        if (resolved != null)
            HandleClassifiedPress(resolved, PressKind.Single, markDirty: false);
    }

    private void ExecuteSourceDefault(ResolvedButton resolved)
    {
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
        => GaugeRenderer.Render(gaugeConfig, _config, _dataSources, _rendererRegistry,
            _resolveFont, _device.ImageWidth, _device.ImageHeight,
            SKEncodedImageFormat.Jpeg, quality: 90);

    public void Dispose()
    {
        _displayClassifier.Dispose();
        _tactileClassifier.Dispose();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _device.Dispose();
    }
}
