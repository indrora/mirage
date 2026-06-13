namespace MirageBox.Oasis.Core.Config;

using MirageBox;

/// <summary>
/// Builds usable starter configurations from whatever hardware is attached.
/// <para>
/// This exists because a config that doesn't mention a device means the device never
/// appears anywhere: the engine only connects devices listed in <see cref="OasisConfig.Devices"/>,
/// and the desktop editor only shows config entries. On a fresh machine (no
/// <c>~/.mirage/config.json</c>) that produced a blank UI with a perfectly good control
/// surface plugged in and ignored. Anything that needs a "sensible default" — first launch,
/// Reset-to-defaults, a newly plugged device — should come through here so the behavior
/// stays consistent.
/// </para>
/// <para>
/// A starter config is deliberately not empty: every adopted device gets the builtin clock
/// on button 0 so the hardware visibly renders something the moment it connects, which
/// doubles as an end-to-end smoke test of the device path.
/// </para>
/// </summary>
public static class DefaultConfigFactory
{
    /// <summary>Name used for both the builtin clock data source and its Text gauge.</summary>
    private const string ClockName = "clock";

    /// <summary>
    /// Builds a starter config containing one device entry + "main" scene per attached
    /// device, with the builtin clock source/gauge assigned to button 0 of each.
    /// </summary>
    public static OasisConfig Create(IEnumerable<IMirageDevice> hardware)
    {
        var config = new OasisConfig();
        foreach (var device in hardware)
            AddHardwareDevice(config, device);
        return config;
    }

    /// <summary>
    /// Adds one physical device (plus a blank "main" scene with the clock on button 0)
    /// into an existing config. Idempotent: a device is identified by its serial number,
    /// and a second call for an already-referenced serial is a no-op.
    /// </summary>
    /// <returns>The config key chosen for the device, or null if it was already present.</returns>
    public static string? AddHardwareDevice(OasisConfig config, IMirageDevice device)
    {
        if (config.Devices.Values.Any(d => !d.Simulator && d.Serial == device.SerialNumber))
            return null;

        EnsureClockDefaults(config);

        var name = UniqueDeviceName(config, device.Profile.Name);

        // Mirror of DeviceViewModel.ApplyHardwareProfile: geometry comes from the live
        // device so the editor's slot grid matches the hardware without a config round-trip.
        config.Devices[name] = new DeviceConfig
        {
            Serial = device.SerialNumber,
            Simulator = false,
            Buttons = device.ImageButtonCount,
            Tactile = device.TactileButtonCount,
            ImageSize = device.ImageWidth,
        };

        config.Scenes[name] = new DeviceSceneConfig
        {
            ActiveScene = "main",
            List = new Dictionary<string, SceneConfig>
            {
                ["main"] = new()
                {
                    Buttons = new Dictionary<string, ButtonAssignmentConfig>
                    {
                        ["0"] = new() { Gauge = ClockName },
                    },
                },
            },
        };

        return name;
    }

    /// <summary>
    /// Makes sure the builtin clock source and its Text gauge exist. Existing entries are
    /// left alone — if the user already has something named "clock", we respect it rather
    /// than clobbering their config with ours.
    /// </summary>
    private static void EnsureClockDefaults(OasisConfig config)
    {
        if (!config.DataSources.ContainsKey(ClockName))
            config.DataSources[ClockName] = new DataSourceConfig { Plugin = "__builtin:clock" };

        if (!config.Gauges.ContainsKey(ClockName))
            config.Gauges[ClockName] = new GaugeConfig
            {
                Source = ClockName,
                Sensor = "time",
                Renderer = new RendererConfig { Type = "Text" },
                Label = "Time",
            };
    }

    /// <summary>
    /// Picks a config key from the device's profile name: "streamcontrollerse", then
    /// "streamcontrollerse2", "streamcontrollerse3", … on collision. Profile names are
    /// short identifiers already, so lowercasing is all the sanitizing they need.
    /// </summary>
    private static string UniqueDeviceName(OasisConfig config, string profileName)
    {
        var baseName = profileName.ToLowerInvariant();
        var name = baseName;
        for (int suffix = 2; config.Devices.ContainsKey(name); suffix++)
            name = $"{baseName}{suffix}";
        return name;
    }
}
