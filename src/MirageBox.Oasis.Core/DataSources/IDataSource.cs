using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.DataSources;

public readonly record struct SensorValue(float? Numeric, string? Text)
{
    public static implicit operator SensorValue(float v) => new(v, null);
    public static implicit operator SensorValue(string s) => new(null, s);
    public bool IsNumeric => Numeric.HasValue;
    public bool IsText => Text != null;
    public static SensorValue Empty => new(null, null);
}

public enum SensorValueType { Numeric, Text }

public enum ButtonPressType { ShortPress, LongPress, EncoderCW, EncoderCCW }

public record SensorInfo(string Path, SensorValueType Type, string Description, bool RequiresElevation = false);

public record SourceActionInfo(string Name, string Description, string? ParamName = null, string? ParamDefault = null, bool IsDefault = false);

/// <summary>
/// Optional capability: a data source that can report a display range for a
/// sensor (e.g. derived from historically observed min/max values).
/// </summary>
public interface IRangedDataSource
{
    (float Min, float Max)? GetRange(string sensorPath);
}

public interface IDataSource : IDisposable
{
    string Name { get; }

    Task InitializeAsync(IReadOnlyDictionary<string, object?> config);
    Task StartAsync(CancellationToken ct);
    Task StopAsync();

    SensorValue GetValue(string sensorPath);

    IReadOnlyList<SensorInfo> GetAvailableSensors();

    void OnButtonPress(string sensorPath, ButtonPressType pressType);

    bool HasCustomRenderer { get; }

    RenderFunc? GetCustomRenderer(string sensorPath);
}
