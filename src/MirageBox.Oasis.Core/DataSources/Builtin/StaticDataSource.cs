using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.DataSources.Builtin;

[DataSource("static", "Fixed values for display-only buttons")]
public class StaticDataSource : IDataSource
{
    private Dictionary<string, float> _values = new();

    public string Name => "static";
    public bool HasCustomRenderer => false;

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("values", out var valuesObj) && valuesObj is Dictionary<string, object?> dict)
        {
            foreach (var (k, v) in dict)
            {
                if (v is float f) _values[k] = f;
                else if (v is double d) _values[k] = (float)d;
            }
        }
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public SensorValue GetValue(string sensorPath) =>
        _values.TryGetValue(sensorPath, out var v) ? new SensorValue(v, null) : SensorValue.Empty;

    public IReadOnlyList<SensorInfo> GetAvailableSensors() =>
        _values.Keys.Select(k => new SensorInfo(k, SensorValueType.Numeric, "Static value")).ToList();

    public void OnButtonPress(string sensorPath, ButtonPressType pressType) { }
    public RenderFunc? GetCustomRenderer(string sensorPath) => null;
    public void Dispose() { }
}
