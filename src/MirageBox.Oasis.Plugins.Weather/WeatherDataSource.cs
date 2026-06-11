using System.Globalization;
using MirageBox.Oasis.Core.DataSources;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Plugins.Weather;

[DataSource("current", "Current weather conditions (Open-Meteo)", Category = "Internet")]
[Sensor("temperature", SensorValueType.Numeric, "Air temperature")]
[Sensor("feels-like", SensorValueType.Numeric, "Apparent temperature")]
[Sensor("humidity", SensorValueType.Numeric, "Relative humidity (%)")]
[Sensor("wind-speed", SensorValueType.Numeric, "Wind speed")]
[Sensor("condition-code", SensorValueType.Numeric, "WMO weather code")]
[Sensor("condition", SensorValueType.Text, "Condition (e.g. Partly cloudy)")]
[Sensor("location", SensorValueType.Text, "Resolved place name")]
[Sensor("status", SensorValueType.Text, "Weather status card (has sensor renderer)")]
public class WeatherDataSource : IDataSource, IRangedDataSource, IConfigurableSource
{
    private readonly OpenMeteoClient _client = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private string? _place;
    private float? _lat, _lon;
    private bool _imperial;
    private TimeSpan _interval = TimeSpan.FromMinutes(15);

    private volatile WeatherSnapshot? _snapshot;
    internal WeatherSnapshot? Snapshot => _snapshot;
    internal bool Imperial => _imperial;

    public string Name => "weather";

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("place", out var p) && p is string ps && !string.IsNullOrWhiteSpace(ps))
            _place = ps.Trim();
        _lat = ParseFloat(config, "latitude");
        _lon = ParseFloat(config, "longitude");
        if (config.TryGetValue("units", out var u) && u is string us)
            _imperial = string.Equals(us, "imperial", StringComparison.OrdinalIgnoreCase);
        // Floor of 5 minutes: be polite to the free Open-Meteo API.
        var minutes = ParseFloat(config, "updateInterval") ?? 15f;
        _interval = TimeSpan.FromMinutes(Math.Max(5, minutes));
        return Task.CompletedTask;
    }

    private static float? ParseFloat(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var v) || v == null) return null;
        if (v is float f) return f;
        if (v is double d) return (float)d;
        return float.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RefreshLoop(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task RefreshLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Keep the last snapshot; the gauge keeps showing known-good data.
                Console.Error.WriteLine($"[weather] refresh failed: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (_lat == null || _lon == null)
        {
            if (string.IsNullOrEmpty(_place))
            {
                Console.Error.WriteLine("[weather] no place or latitude/longitude configured");
                return;
            }
            var geo = await _client.GeocodeAsync(_place, ct);
            if (geo == null)
            {
                Console.Error.WriteLine($"[weather] could not geocode '{_place}'");
                return;
            }
            (_lat, _lon, _place) = geo.Value;
        }

        _snapshot = await _client.GetCurrentAsync(_lat.Value, _lon!.Value, _imperial, _place ?? $"{_lat},{_lon}", ct);
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return _loop ?? Task.CompletedTask;
    }

    public SensorValue GetValue(string sensorPath)
    {
        var snap = _snapshot;
        if (snap == null)
            return sensorPath is "condition" or "location" or "status" ? "..." : SensorValue.Empty;

        return sensorPath switch
        {
            "temperature" => snap.Temperature,
            "feels-like" => snap.FeelsLike,
            "humidity" => snap.Humidity,
            "wind-speed" => snap.WindSpeed,
            "condition-code" => (float)snap.WeatherCode,
            "condition" => WmoCodes.Describe(snap.WeatherCode).Text,
            "location" => snap.Location,
            "status" => WmoCodes.Describe(snap.WeatherCode).Text,
            _ => SensorValue.Empty
        };
    }

    public IReadOnlyList<SensorInfo> GetAvailableSensors() =>
        SensorAttributeHelper.GetSensorsFromAttributes(GetType());

    public (float Min, float Max)? GetRange(string sensorPath) => sensorPath switch
    {
        "temperature" or "feels-like" => _imperial ? (0f, 110f) : (-20f, 45f),
        "humidity" => (0f, 100f),
        "wind-speed" => _imperial ? (0f, 75f) : (0f, 120f),
        "condition-code" => (0f, 99f),
        _ => null
    };

    public IReadOnlyList<SourceParamInfo> DescribeParameters() =>
    [
        new("place", "Place name", SourceParamKind.Text,
            Description: "City or place to geocode (ignored when latitude/longitude are set)"),
        new("latitude", "Latitude", SourceParamKind.Number),
        new("longitude", "Longitude", SourceParamKind.Number),
        new("units", "Units", SourceParamKind.Choice, Default: "metric",
            Options: [new("metric", "Metric (°C, km/h)"), new("imperial", "Imperial (°F, mph)")]),
        new("updateInterval", "Update interval (minutes)", SourceParamKind.Number, Default: "15"),
    ];

    [SourceAction("refresh", "Fetch current conditions now", IsDefault = true)]
    public void Refresh()
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => RefreshAsync(ct), ct);
    }

    public void OnButtonPress(string sensorPath, ButtonPressType pressType)
    {
        if (pressType == ButtonPressType.ShortPress)
            Refresh();
    }

    public bool HasCustomRenderer => true;

    public RenderFunc? GetCustomRenderer(string sensorPath) => sensorPath switch
    {
        "status" => WeatherCardRenderer.StatusCard(this),
        _ => null
    };

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _client.Dispose();
    }
}
