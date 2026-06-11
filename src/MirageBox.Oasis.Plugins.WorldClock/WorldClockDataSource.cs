using MirageBox.Oasis.Core.DataSources;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Plugins.WorldClock;

[DataSource("zone", "Clock for a configured timezone", Category = "Time")]
[Sensor("time", SensorValueType.Text, "Current time in the zone")]
[Sensor("date", SensorValueType.Text, "Current date in the zone")]
[Sensor("zone-name", SensorValueType.Text, "Timezone display name")]
[Sensor("utc-offset", SensorValueType.Numeric, "UTC offset in hours")]
[Sensor("face", SensorValueType.Text, "Analog clock face (has sensor renderer)")]
public class WorldClockDataSource : IDataSource, IConfigurableSource
{
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _twentyFourHour = true;

    public string Name => "worldclock";

    internal DateTime ZoneNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _zone).DateTime;
    internal string ZoneLabel => _zone.Id;

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("timezone", out var tz) && tz is string tzs && !string.IsNullOrWhiteSpace(tzs))
        {
            try
            {
                _zone = TimeZoneInfo.FindSystemTimeZoneById(tzs.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                Console.Error.WriteLine($"[worldclock] unknown timezone '{tzs}', using local");
            }
        }
        if (config.TryGetValue("format", out var f) && f is string fs)
            _twentyFourHour = fs != "12h";
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public SensorValue GetValue(string sensorPath)
    {
        var now = ZoneNow;
        return sensorPath switch
        {
            "time" => now.ToString(_twentyFourHour ? "HH:mm" : "h:mm tt"),
            "date" => now.ToString("yyyy-MM-dd"),
            "zone-name" => _zone.Id,
            "utc-offset" => (float)_zone.GetUtcOffset(DateTimeOffset.UtcNow).TotalHours,
            "face" => now.ToString(_twentyFourHour ? "HH:mm" : "h:mm tt"),
            _ => SensorValue.Empty
        };
    }

    public IReadOnlyList<SensorInfo> GetAvailableSensors() =>
        SensorAttributeHelper.GetSensorsFromAttributes(GetType());

    public IReadOnlyList<SourceParamInfo> DescribeParameters() =>
    [
        new("timezone", "Timezone", SourceParamKind.Choice,
            Default: TimeZoneInfo.Local.Id,
            Options: TimeZoneInfo.GetSystemTimeZones()
                .Select(z => new SourceParamOption(z.Id, z.DisplayName))
                .ToList()),
        new("format", "Time format", SourceParamKind.Choice, Default: "24h",
            Options: [new("24h", "24-hour"), new("12h", "12-hour")]),
    ];

    public void OnButtonPress(string sensorPath, ButtonPressType pressType) { }

    public bool HasCustomRenderer => true;

    public RenderFunc? GetCustomRenderer(string sensorPath) =>
        sensorPath == "face" ? ClockFaceRenderer.AnalogFace(this) : null;

    public void Dispose() { }
}
