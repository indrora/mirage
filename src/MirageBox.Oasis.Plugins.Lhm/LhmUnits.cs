using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace MirageBox.Oasis.Plugins.Lhm;

/// <summary>Unit strings and text formatting for LibreHardwareMonitor sensor types.</summary>
internal static class LhmUnits
{
    public static string UnitFor(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Level => "%",
        SensorType.Control => "%",
        SensorType.Humidity => "%",
        SensorType.Fan => "RPM",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Frequency => "Hz",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.Energy => "mWh",
        SensorType.Flow => "L/h",
        SensorType.Noise => "dBA",
        SensorType.TimeSpan => "s",
        SensorType.Factor => "",
        _ => "",
    };

    /// <summary>Formats a value with its unit, e.g. "54.2 °C", "1450 RPM", "12.3 MB/s".</summary>
    public static string Format(SensorType type, float value)
    {
        if (type == SensorType.Throughput)
            return FormatThroughput(value);

        var unit = UnitFor(type);
        var text = type switch
        {
            SensorType.Fan or SensorType.Clock or SensorType.Frequency
                or SensorType.Load or SensorType.Level or SensorType.Control
                => value.ToString("0", CultureInfo.InvariantCulture),
            _ => value.ToString("0.#", CultureInfo.InvariantCulture),
        };
        return unit.Length > 0 ? $"{text} {unit}" : text;
    }

    private static string FormatThroughput(float bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_000_000_000f => $"{bytesPerSecond / 1_000_000_000f:0.#} GB/s",
            >= 1_000_000f => $"{bytesPerSecond / 1_000_000f:0.#} MB/s",
            >= 1_000f => $"{bytesPerSecond / 1_000f:0.#} KB/s",
            _ => $"{bytesPerSecond:0} B/s",
        };
    }
}
