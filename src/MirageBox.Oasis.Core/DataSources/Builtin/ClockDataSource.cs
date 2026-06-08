using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.DataSources.Builtin;

[DataSource("clock", "System clock — time and date")]
[Sensor("time", SensorValueType.Text, "Current time (HH:mm)")]
[Sensor("date", SensorValueType.Text, "Current date")]
[Sensor("hour", SensorValueType.Numeric, "Hour (0-23)")]
[Sensor("minute", SensorValueType.Numeric, "Minute (0-59)")]
[Sensor("second", SensorValueType.Numeric, "Second (0-59)")]
public class ClockDataSource : IDataSource
{
    private string _timeFormat = "HH:mm";
    private string _dateFormat = "yyyy-MM-dd";

    public string Name => "clock";
    public bool HasCustomRenderer => false;

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("timeFormat", out var tf) && tf is string tfs)
            _timeFormat = tfs;
        if (config.TryGetValue("dateFormat", out var df) && df is string dfs)
            _dateFormat = dfs;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public SensorValue GetValue(string sensorPath)
    {
        var now = DateTime.Now;
        return sensorPath switch
        {
            "time" => now.ToString(_timeFormat),
            "date" => now.ToString(_dateFormat),
            "hour" => (float)now.Hour,
            "minute" => (float)now.Minute,
            "second" => (float)now.Second,
            _ => SensorValue.Empty
        };
    }

    public IReadOnlyList<SensorInfo> GetAvailableSensors() =>
        SensorAttributeHelper.GetSensorsFromAttributes(GetType());

    public void OnButtonPress(string sensorPath, ButtonPressType pressType) { }
    public RenderFunc? GetCustomRenderer(string sensorPath) => null;
    public void Dispose() { }
}
