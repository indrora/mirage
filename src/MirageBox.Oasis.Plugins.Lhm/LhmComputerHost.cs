using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace MirageBox.Oasis.Plugins.Lhm;

/// <summary>A point-in-time reading of one LHM sensor (Min/Max are historical since open).</summary>
internal readonly record struct LhmReading(float? Value, float? Min, float? Max);

/// <summary>Static metadata for one discovered LHM sensor.</summary>
internal record LhmSensorEntry(
    string BasePath,
    string HwSlug,
    HardwareType HardwareType,
    string HardwareName,
    string SensorName,
    SensorType SensorType);

/// <summary>
/// Owns the single LibreHardwareMonitor <see cref="Computer"/> for the process.
/// Opening the Computer loads the ring0 driver and enumerates hardware — both
/// heavyweight — so all LhmDataSource instances share it via refcounting.
/// A background thread polls hardware and publishes immutable snapshots;
/// consumers (the render loop) never touch LHM objects directly.
/// </summary>
internal static class LhmComputerHost
{
    private static readonly object Gate = new();
    private static int _refCount;
    private static Computer? _computer;
    private static Thread? _updateThread;
    private static CancellationTokenSource? _cts;
    private static ISensor[] _sensors = Array.Empty<ISensor>();
    private static Dictionary<ISensor, string> _sensorPaths = new();

    private static volatile IReadOnlyList<LhmSensorEntry> _entries = Array.Empty<LhmSensorEntry>();
    private static volatile Dictionary<string, LhmReading> _snapshot = new();

    /// <summary>Poll interval; the smallest value requested by any source wins.</summary>
    private static int _updateIntervalMs = 1000;

    public static bool IsElevated { get; } = DetectElevation();

    private static bool DetectElevation()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Sensor metadata table, fixed after open.</summary>
    public static IReadOnlyList<LhmSensorEntry> Entries => _entries;

    /// <summary>Latest readings keyed by sensor base path.</summary>
    public static IReadOnlyDictionary<string, LhmReading> Snapshot => _snapshot;

    public static void RequestUpdateInterval(int milliseconds)
    {
        lock (Gate)
            _updateIntervalMs = Math.Clamp(Math.Min(_updateIntervalMs, milliseconds), 100, 60_000);
    }

    public static void Acquire()
    {
        lock (Gate)
        {
            if (_refCount++ > 0) return;

            Console.WriteLine($"[lhm] Opening LibreHardwareMonitor (elevated: {(IsElevated ? "yes" : "no")})");
            if (!IsElevated)
                Console.Error.WriteLine("[lhm] Not running as administrator — CPU, motherboard and embedded-controller sensors will be unavailable or incomplete.");

            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsControllerEnabled = true,
                IsBatteryEnabled = true,
                IsPsuEnabled = true,
            };
            _computer.Open();
            BuildSensorTable(_computer);

            // The thread disposes the CTS itself on exit: Release() may give up
            // joining mid-cycle (slow SMART scans), and disposing under the
            // thread would crash its WaitHandle access.
            _cts = new CancellationTokenSource();
            var cts = _cts;
            _updateThread = new Thread(() =>
            {
                try { UpdateLoop(cts.Token); }
                finally { cts.Dispose(); }
            })
            {
                IsBackground = true,
                Name = "lhm-update",
            };
            _updateThread.Start();
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (_refCount == 0) return;
            if (--_refCount > 0) return;

            _cts?.Cancel();
            _updateThread?.Join(TimeSpan.FromSeconds(5));
            _updateThread = null;
            _cts = null;

            _computer?.Close();   // unloads the ring0 driver
            _computer = null;
            _sensors = Array.Empty<ISensor>();
            _sensorPaths = new Dictionary<ISensor, string>();
            _entries = Array.Empty<LhmSensorEntry>();
            _snapshot = new Dictionary<string, LhmReading>();
            Console.WriteLine("[lhm] Closed LibreHardwareMonitor");
        }
    }

    private static void BuildSensorTable(Computer computer)
    {
        var entries = new List<LhmSensorEntry>();
        var sensors = new List<ISensor>();
        var paths = new Dictionary<ISensor, string>();
        var usedHwSlugs = new HashSet<string>();

        foreach (var hardware in computer.Hardware)
            VisitHardware(hardware, entries, sensors, paths, usedHwSlugs);

        _entries = entries;
        _sensors = sensors.ToArray();
        _sensorPaths = paths;
    }

    private static void VisitHardware(IHardware hardware,
        List<LhmSensorEntry> entries, List<ISensor> sensors,
        Dictionary<ISensor, string> paths, HashSet<string> usedHwSlugs)
    {
        hardware.Update();
        var hwSlug = Dedupe(Slug(hardware.Name), usedHwSlugs);

        var usedSensorPaths = new HashSet<string>();
        foreach (var sensor in hardware.Sensors)
        {
            var typeSlug = Slug(sensor.SensorType.ToString());
            var basePath = Dedupe($"{hwSlug}/{typeSlug}/{Slug(sensor.Name)}", usedSensorPaths);

            entries.Add(new LhmSensorEntry(basePath, hwSlug, hardware.HardwareType, hardware.Name,
                sensor.Name, sensor.SensorType));
            sensors.Add(sensor);
            paths[sensor] = basePath;
        }

        foreach (var sub in hardware.SubHardware)
            VisitHardware(sub, entries, sensors, paths, usedHwSlugs);
    }

    private static void UpdateLoop(CancellationToken ct)
    {
        long tick = 0;
        while (!ct.IsCancellationRequested)
        {
            var computer = _computer;
            if (computer == null) return;

            foreach (var hardware in computer.Hardware)
            {
                // SMART queries are slow; poll storage less often.
                if (hardware.HardwareType == HardwareType.Storage && tick % 5 != 0)
                    continue;
                try
                {
                    UpdateRecursive(hardware);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[lhm] Update failed for {hardware.Name}: {ex.Message}");
                }
            }

            var snapshot = new Dictionary<string, LhmReading>(_sensors.Length);
            foreach (var sensor in _sensors)
            {
                if (_sensorPaths.TryGetValue(sensor, out var path))
                    snapshot[path] = new LhmReading(sensor.Value, sensor.Min, sensor.Max);
            }
            _snapshot = snapshot;

            tick++;
            int interval;
            lock (Gate) interval = _updateIntervalMs;
            if (ct.WaitHandle.WaitOne(interval)) return;
        }
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    internal static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastDash = true;
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.Length > 0 ? sb.ToString() : "unnamed";
    }

    private static string Dedupe(string slug, HashSet<string> used)
    {
        var candidate = slug;
        for (var i = 2; !used.Add(candidate); i++)
            candidate = $"{slug}-{i}";
        return candidate;
    }
}
