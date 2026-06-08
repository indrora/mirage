using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.DataSources.Builtin;

[DataSource("counter", "Interactive up/down counter")]
[Sensor("value", SensorValueType.Numeric, "Current counter value")]
public class CounterDataSource : IDataSource
{
    private float _value;
    private float _step = 1;
    private float _min = float.MinValue;
    private float _max = float.MaxValue;

    public string Name => "counter";
    public bool HasCustomRenderer => false;

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("initial", out var i))
            _value = Convert.ToSingle(i);
        if (config.TryGetValue("step", out var s))
            _step = Convert.ToSingle(s);
        if (config.TryGetValue("min", out var mn))
            _min = Convert.ToSingle(mn);
        if (config.TryGetValue("max", out var mx))
            _max = Convert.ToSingle(mx);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public SensorValue GetValue(string sensorPath) =>
        sensorPath == "value" ? new SensorValue(_value, null) : SensorValue.Empty;

    public IReadOnlyList<SensorInfo> GetAvailableSensors() =>
        SensorAttributeHelper.GetSensorsFromAttributes(GetType());

    public void OnButtonPress(string sensorPath, ButtonPressType pressType)
    {
        if (sensorPath != "value") return;
        switch (pressType)
        {
            case ButtonPressType.ShortPress:
            case ButtonPressType.EncoderCW:
                _value = Math.Min(_value + _step, _max);
                break;
            case ButtonPressType.LongPress:
            case ButtonPressType.EncoderCCW:
                _value = Math.Max(_value - _step, _min);
                break;
        }
    }

    public RenderFunc? GetCustomRenderer(string sensorPath) => null;
    public void Dispose() { }
}
