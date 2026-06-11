using System.Collections.Concurrent;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;
using MirageBox.TinyGauges;
using SkiaSharp;
using GaugeConfig = MirageBox.Oasis.Core.Config.GaugeConfig;

namespace MirageBox.Oasis.Core.Engine;

/// <summary>
/// Renders a configured gauge to an encoded image. Shared by the per-device
/// render loop (JPEG to hardware) and the editor's tile previews (PNG).
/// </summary>
internal static class GaugeRenderer
{
    // Keys already warned about, so the per-frame render loop logs once per gauge.
    private static readonly HashSet<string> WarnedKeys = new();

    private static void WarnOnce(string key, string message)
    {
        lock (WarnedKeys)
        {
            if (!WarnedKeys.Add(key)) return;
        }
        Console.Error.WriteLine($"[gauge] {key}: {message}");
    }

    /// <param name="valueOverride">
    /// When set, renders this synthetic value instead of querying the data
    /// source (used for static previews). Text gauges fall back to the label.
    /// </param>
    public static byte[]? Render(
        GaugeConfig gaugeConfig,
        OasisConfig config,
        ConcurrentDictionary<string, IDataSource> dataSources,
        RendererRegistry rendererRegistry,
        Func<string, SKTypeface?> resolveFont,
        int width,
        int height,
        SKEncodedImageFormat format,
        int quality,
        float? valueOverride = null)
    {
        dataSources.TryGetValue(gaugeConfig.Source, out var source);
        if (source == null && valueOverride == null)
            return null;

        var sensorValue = valueOverride == null
            ? source!.GetValue(gaugeConfig.Sensor)
            : SensorValue.Empty;

        var theme = ResolveTheme(config, gaugeConfig.Theme);
        var typeface = resolveFont(gaugeConfig.Font ?? config.Defaults.Font);

        var sensorInfo = source?.GetAvailableSensors()
            .FirstOrDefault(s => s.Path == gaugeConfig.Sensor);
        var sensorType = sensorInfo?.Type ?? SensorValueType.Numeric;

        RenderFunc? renderer = null;
        if (gaugeConfig.Renderer.Type == SourceRenderer.RendererType)
        {
            renderer = source?.GetCustomRenderer(gaugeConfig.Sensor);
            if (renderer == null)
            {
                WarnOnce($"{gaugeConfig.Source}/{gaugeConfig.Sensor}",
                    "gauge uses __source__ but the source provides no renderer for this sensor; falling back to Text");
                renderer = rendererRegistry.Resolve("Text", sensorType, null);
            }
        }

        renderer ??= rendererRegistry.Resolve(gaugeConfig.Renderer.Type, sensorType, gaugeConfig.Renderer.Parameters);
        if (renderer == null) return null;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        var bounds = new SKRect(0, 0, width, height);

        if (valueOverride == null && (sensorType == SensorValueType.Text || sensorValue.IsText))
        {
            var textLabel = sensorValue.Text ?? gaugeConfig.Label ?? "";
            var rv = new RangedValue(0, 1, 0);
            renderer(canvas, theme, typeface!, bounds, textLabel, rv);
        }
        else
        {
            var (min, max) = ResolveRange(gaugeConfig, source);
            var value = valueOverride ?? sensorValue.Numeric ?? 0;
            var rv = new RangedValue(min, max, value);
            renderer(canvas, theme, typeface!, bounds, gaugeConfig.Label, rv);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public static (float Min, float Max) ResolveRange(GaugeConfig gaugeConfig, IDataSource? source)
    {
        var range = (source as IRangedDataSource)?.GetRange(gaugeConfig.Sensor);
        float min = gaugeConfig.Min ?? range?.Min ?? 0;
        float max = gaugeConfig.Max ?? range?.Max ?? 100;
        if (max <= min) { min = 0; max = 100; }
        return (min, max);
    }

    private static Theme ResolveTheme(OasisConfig config, string? themeName)
    {
        themeName ??= config.Defaults.Theme;
        if (!config.Themes.TryGetValue(themeName, out var tc))
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
}
