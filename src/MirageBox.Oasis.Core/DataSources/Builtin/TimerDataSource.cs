using System.Diagnostics;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.DataSources.Builtin;

[DataSource("timer", "Interactive stopwatch timer")]
[Sensor("elapsed", SensorValueType.Text, "Elapsed time as formatted string")]
[Sensor("elapsedSeconds", SensorValueType.Numeric, "Elapsed time in seconds")]
[Sensor("running", SensorValueType.Numeric, "1 if running, 0 if stopped")]
public class TimerDataSource : IDataSource
{
    private readonly Stopwatch _stopwatch = new();
    private string _format = @"mm\:ss";

    public string Name => "timer";
    public bool HasCustomRenderer => false;

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("format", out var f) && f is string fs)
            _format = fs;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public SensorValue GetValue(string sensorPath) => sensorPath switch
    {
        "elapsed" => _stopwatch.Elapsed.ToString(_format),
        "elapsedSeconds" => (float)_stopwatch.Elapsed.TotalSeconds,
        "running" => _stopwatch.IsRunning ? 1f : 0f,
        _ => SensorValue.Empty
    };

    public IReadOnlyList<SensorInfo> GetAvailableSensors() =>
        SensorAttributeHelper.GetSensorsFromAttributes(GetType());

    public void OnButtonPress(string sensorPath, ButtonPressType pressType)
    {
        switch (pressType)
        {
            case ButtonPressType.ShortPress:
                if (_stopwatch.IsRunning) _stopwatch.Stop();
                else _stopwatch.Start();
                break;
            case ButtonPressType.LongPress:
                _stopwatch.Reset();
                break;
        }
    }

    public RenderFunc? GetCustomRenderer(string sensorPath) => null;
    public void Dispose() { }
}
