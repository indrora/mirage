using LibreHardwareMonitor.Hardware;
using MirageBox.Oasis.Core.DataSources;
using MirageBox.TinyGauges;
using SensorValue = MirageBox.Oasis.Core.DataSources.SensorValue;

namespace MirageBox.Oasis.Plugins.Lhm;

/// <summary>
/// Base class for the per-hardware-kind LHM sources. Each instance is a view
/// over the shared <see cref="LhmComputerHost"/> filtered to its hardware types.
///
/// Each instance is bound to a single device, so sensor paths are relative to
/// it: &lt;sensor-type&gt;/&lt;sensor-slug&gt;[/&lt;field&gt;] (e.g. "load/cpu-core-1/text")
/// with field one of value (default), units, text, min, max — plus per-source
/// status/elevated and status/hardware-count. Legacy device-prefixed paths
/// (&lt;hw-slug&gt;/...) are still accepted.
/// </summary>
public abstract class LhmDataSourceBase : IDataSource, IRangedDataSource, IConfigurableSource
{
    private static readonly HashSet<HardwareType> ElevationRequired =
    [
        HardwareType.Cpu,
        HardwareType.Motherboard,
        HardwareType.SuperIO,
        HardwareType.EmbeddedController,
    ];

    private readonly HashSet<HardwareType> _types;
    private bool _started;
    private string? _hardwareChoice;            // hw slug; null = first discovered device
    private HashSet<SensorType>? _aspectFilter; // null = all

    protected LhmDataSourceBase(string name, params HardwareType[] types)
    {
        Name = name;
        _types = [.. types];
    }

    public string Name { get; }

    public Task InitializeAsync(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("updateInterval", out var raw) && raw != null)
        {
            var ms = Convert.ToInt32(raw);
            LhmComputerHost.RequestUpdateInterval(ms);
        }

        // One device per instance; a stale comma list from an old config keeps its first entry.
        _hardwareChoice = config.TryGetValue("hardware", out var hw) && hw is string hws && !string.IsNullOrWhiteSpace(hws)
            ? hws.Split(',')[0].Trim().ToLowerInvariant()
            : null;

        var aspects = ParseList(config, "aspects");
        _aspectFilter = aspects == null
            ? null
            : Enum.GetValues<SensorType>()
                .Where(t => aspects.Contains(LhmComputerHost.Slug(t.ToString())))
                .ToHashSet();

        return Task.CompletedTask;
    }

    private static HashSet<string>? ParseList(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var raw) || raw is not string s || string.IsNullOrWhiteSpace(s))
            return null;
        var items = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.ToLowerInvariant())
            .ToHashSet();
        return items.Count > 0 ? items : null;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (!_started)
        {
            LhmComputerHost.Acquire();
            _started = true;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (_started)
        {
            LhmComputerHost.Release();
            _started = false;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public SensorValue GetValue(string sensorPath)
    {
        switch (sensorPath)
        {
            case "status/elevated":
                return LhmComputerHost.IsElevated ? "yes" : "no";
            case "status/hardware-count":
                return (float)Entries().Select(e => e.HardwareName).Distinct().Count();
        }

        var (basePath, field) = SplitField(sensorPath);
        var entry = FindEntry(basePath);
        if (entry == null) return SensorValue.Empty;

        if (field == "units")
            return LhmUnits.UnitFor(entry.SensorType);

        if (!LhmComputerHost.Snapshot.TryGetValue(entry.BasePath, out var reading))
            return SensorValue.Empty;

        return field switch
        {
            "value" => reading.Value is { } v ? new SensorValue(v, null) : SensorValue.Empty,
            "min" => reading.Min is { } lo ? new SensorValue(lo, null) : SensorValue.Empty,
            "max" => reading.Max is { } hi ? new SensorValue(hi, null) : SensorValue.Empty,
            "text" => reading.Value is { } tv
                ? new SensorValue(null, LhmUnits.Format(entry.SensorType, tv))
                : SensorValue.Empty,
            _ => SensorValue.Empty,
        };
    }

    public (float Min, float Max)? GetRange(string sensorPath)
    {
        var (basePath, field) = SplitField(sensorPath);
        if (field != "value") return null;
        var entry = FindEntry(basePath);
        if (entry == null) return null;
        if (LhmComputerHost.Snapshot.TryGetValue(entry.BasePath, out var reading)
            && reading is { Min: { } lo, Max: { } hi } && hi > lo)
            return (lo, hi);
        return null;
    }

    public IReadOnlyList<SensorInfo> GetAvailableSensors()
    {
        var sensors = new List<SensorInfo>
        {
            new("status/elevated", SensorValueType.Text,
                "Whether the process has administrative elevation (yes/no)"),
            new("status/hardware-count", SensorValueType.Numeric,
                "Number of hardware devices visible to this source"),
        };

        foreach (var entry in Entries())
        {
            var elevation = ElevationRequired.Contains(entry.HardwareType);
            var path = RelPath(entry);
            var desc = $"{entry.HardwareName}: {entry.SensorName} ({entry.SensorType})";
            sensors.Add(new SensorInfo(path, SensorValueType.Numeric, desc, elevation));
            sensors.Add(new SensorInfo($"{path}/text", SensorValueType.Text, $"{desc} — value with units", elevation));
            sensors.Add(new SensorInfo($"{path}/units", SensorValueType.Text, $"{desc} — unit string", elevation));
            sensors.Add(new SensorInfo($"{path}/min", SensorValueType.Numeric, $"{desc} — historical minimum", elevation));
            sensors.Add(new SensorInfo($"{path}/max", SensorValueType.Numeric, $"{desc} — historical maximum", elevation));
        }
        return sensors;
    }

    /// <summary>Path relative to this instance's device (BasePath minus the hw slug).</summary>
    private static string RelPath(LhmSensorEntry entry)
        => entry.BasePath[(entry.HwSlug.Length + 1)..];

    private LhmSensorEntry? FindEntry(string basePath)
        => Entries().FirstOrDefault(e => RelPath(e) == basePath || e.BasePath == basePath);

    public void OnButtonPress(string sensorPath, ButtonPressType pressType) { }

    public bool HasCustomRenderer => false;

    public RenderFunc? GetCustomRenderer(string sensorPath) => null;

    public IReadOnlyList<SourceParamInfo> DescribeParameters()
    {
        // Options come from real hardware, so the host must be open; borrow a
        // reference if this instance isn't started (e.g. transient UI probe).
        var borrowed = !_started;
        if (borrowed) LhmComputerHost.Acquire();
        try
        {
            // Unfiltered view: the picker must offer everything, including
            // options the current config has deselected.
            var all = LhmComputerHost.Entries.Where(e => _types.Contains(e.HardwareType)).ToList();

            // Discovery order, so the first option matches the runtime default.
            var hardwareOptions = all
                .Select(e => (e.HwSlug, e.HardwareName))
                .Distinct()
                .Select(h => new SourceParamOption(h.HwSlug, h.HardwareName))
                .ToList();

            var aspectOptions = all
                .Select(e => e.SensorType)
                .Distinct()
                .OrderBy(t => t.ToString())
                .Select(t =>
                {
                    var unit = LhmUnits.UnitFor(t);
                    var label = unit.Length > 0 ? $"{t} ({unit})" : t.ToString();
                    return new SourceParamOption(LhmComputerHost.Slug(t.ToString()), label);
                })
                .ToList();

            return
            [
                new SourceParamInfo("hardware", "Hardware", SourceParamKind.Choice,
                    Default: hardwareOptions.FirstOrDefault()?.Value,
                    Options: hardwareOptions,
                    Description: "The device this source instance exposes"),
                new SourceParamInfo("aspects", "Aspects", SourceParamKind.MultiChoice,
                    Options: aspectOptions, Description: "Sensor kinds to expose (none checked = all)"),
                new SourceParamInfo("updateInterval", "Update interval (ms)", SourceParamKind.Number,
                    Default: "1000", Description: "Poll interval in milliseconds"),
            ];
        }
        finally
        {
            if (borrowed) LhmComputerHost.Release();
        }
    }

    private IEnumerable<LhmSensorEntry> Entries()
    {
        var kind = LhmComputerHost.Entries.Where(e => _types.Contains(e.HardwareType));
        // One device per instance: explicit choice, or the first one discovered.
        var hw = _hardwareChoice ?? kind.FirstOrDefault()?.HwSlug;
        return kind.Where(e =>
            e.HwSlug == hw
            && (_aspectFilter == null || _aspectFilter.Contains(e.SensorType)));
    }

    private static (string BasePath, string Field) SplitField(string sensorPath)
    {
        var idx = sensorPath.LastIndexOf('/');
        if (idx > 0)
        {
            var tail = sensorPath[(idx + 1)..];
            if (tail is "value" or "units" or "text" or "min" or "max")
                return (sensorPath[..idx], tail);
        }
        return (sensorPath, "value");
    }
}
