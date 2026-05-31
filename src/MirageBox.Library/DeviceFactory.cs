namespace MirageBox;

using HidLibrary;

/// <summary>
/// Provides factory methods for discovering and connecting to Mirabox/Ajazz/Somfon devices.
/// </summary>
public static class DeviceFactory
{
    private sealed record DeviceProfile(
        string Name,
        ushort VendorId,
        ushort ProductId,
        int ButtonCount,
        int EncoderCount,
        int LedCount,
        int ImageWidth,
        int ImageHeight,
        int RotationDegrees,
        byte HidReportId,
        DeviceProtocolVariant ProtocolVariant);

    /// <summary>
    /// Known device profiles from the StreamDock SDK, ordered by priority.
    /// All image sizes and rotations are per the CRT wire protocol reference.
    /// </summary>
    /// <summary>
    /// VID/PID pairs that all map to the same logical device (to avoid duplicates).
    /// Each tuple is (VendorId, ProductId).
    /// </summary>
    private static readonly Dictionary<(ushort, ushort), DeviceProfile> DevicesByVidPid = new()
    {
        // StreamController SE / Somfon rebadge (6 display + 3 side buttons, 3 knobs)
        { (0x1500, 0x3001), new("StreamControllerSE", 0x1500, 0x3001, 9, 3, 0, 64, 64, 90, 0x00, DeviceProtocolVariant.AckPrefix) },

        // N4Pro — 2×5 buttons, 4 knobs, 4 LED rings (firmware 0x1008 and 0x1021 are same device)
        { (0x5548, 0x1008), new("N4Pro", 0x5548, 0x1008, 10, 4, 4, 112, 112, 180, 0x00, DeviceProtocolVariant.AckPrefix) },
        { (0x5548, 0x1021), new("N4Pro", 0x5548, 0x1021, 10, 4, 4, 112, 112, 180, 0x00, DeviceProtocolVariant.AckPrefix) },

        // N4 — 2×5 buttons, 4 knobs, no LEDs (two firmware revisions)
        { (0x6602, 0x1001), new("N4", 0x6602, 0x1001, 10, 4, 0, 112, 112, 180, 0x00, DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1007), new("N4", 0x6603, 0x1007, 10, 4, 0, 112, 112, 180, 0x00, DeviceProtocolVariant.AckPrefix) },

        // XL — 4×8 buttons, 2 toggle encoders, 6 LEDs (two firmware revisions)
        { (0x5548, 0x1028), new("XL", 0x5548, 0x1028, 32, 2, 6, 80, 80, 180, 0x00, DeviceProtocolVariant.AckPrefix) },
        { (0x5548, 0x1031), new("XL", 0x5548, 0x1031, 32, 2, 6, 80, 80, 180, 0x00, DeviceProtocolVariant.AckPrefix) },

        // M18 — 3×5 buttons, no encoders, 24 LEDs (two firmware revisions)
        { (0x6603, 0x1009), new("M18", 0x6603, 0x1009, 15, 0, 24, 64, 64, 0, 0x00, DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1012), new("M18", 0x6603, 0x1012, 15, 0, 24, 64, 64, 0, 0x00, DeviceProtocolVariant.AckPrefix) },

        // K1Pro — 2×3 buttons, 3 knobs; uses HID report ID 0x04 (two firmware revisions)
        { (0x6603, 0x1015), new("K1Pro", 0x6603, 0x1015, 6, 3, 0, 64, 64, 90, 0x04, DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1019), new("K1Pro", 0x6603, 0x1019, 6, 3, 0, 64, 64, 90, 0x04, DeviceProtocolVariant.AckPrefix) },

        // N1 — 3×5 buttons, 1 knob, portrait orientation (two firmware revisions)
        { (0x6603, 0x1011), new("N1", 0x6603, 0x1011, 15, 1, 0, 96, 96, 0, 0x00, DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1000), new("N1", 0x6603, 0x1000, 15, 1, 0, 96, 96, 0, 0x00, DeviceProtocolVariant.AckPrefix) },

        // N3 — 2×3 buttons, 3 knobs
        { (0x6602, 0x1003), new("N3", 0x6602, 0x1003, 6, 3, 0, 64, 64, -90, 0x00, DeviceProtocolVariant.AckPrefix) },

        // Legacy Mirabox/Ajazz (snapshot bitfield protocol)
        { (0x294B, 0x0171), new("Mirabox", 0x294B, 0x0171, 15, 4, 0, 72, 72, 0, 0x00, DeviceProtocolVariant.LegacyBitfield) },
    };

    /// <summary>
    /// Discovers all connected StreamDock-compatible devices.
    /// </summary>
    public static IEnumerable<IMirageDevice> DiscoverDevices()
    {
        var devices = new List<IMirageDevice>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenProfileNames = new HashSet<string>();
        int deviceIndex = 0;

        // Build a set of unique VID/PID pairs to enumerate each only once.
        var uniqueVidPids = new HashSet<(ushort, ushort)>(DevicesByVidPid.Keys);

        foreach (var (vid, pid) in uniqueVidPids)
        {
            var profile = DevicesByVidPid[(vid, pid)];
            
            foreach (var hidDevice in HidDevices.Enumerate(vid, pid))
            {
                // Deduplicate by path. If path is empty, use profile+index as fallback.
                var path = hidDevice.DevicePath ?? "";
                if (!seenPaths.Add(path))
                    continue;

                var serialNumber = $"{profile.Name}-{deviceIndex}";
                
                byte[] hidSerial;

                hidDevice.ReadSerialNumber(out hidSerial);
                // turn the byte array into a readable string, if possible
                if (hidSerial.Length > 0)
                {
                    var hidSerialStr = System.Text.Encoding.UTF8.GetString(hidSerial).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(hidSerialStr))
                        serialNumber = hidSerialStr;
                }

                // Vendor-defined usage pages are 0xFF00–0xFFFF; keyboard/system pages are low values.
                // Windows also blocks access to the keyboard HID interface, so skip it regardless.
                if (((ushort)hidDevice.Capabilities.UsagePage & 0xFF00) != 0xFF00)
                    continue;

                Console.WriteLine($"Found device: VID={vid:X4} PID={pid:X4} Serial='{serialNumber}' Path='{path}' Profile='{profile.Name}'");

                devices.Add(new MirageDevice(
                    hidDevice,
                    serialNumber,
                    profile.ButtonCount,
                    profile.EncoderCount,
                    profile.ImageWidth,
                    profile.ImageHeight,
                    profile.RotationDegrees,
                    profile.ProtocolVariant));

                deviceIndex++;
            }
        }

        return devices;
    }

    /// <summary>
    /// Discovers a specific device by serial number.
    /// </summary>
    public static IMirageDevice? FindDeviceBySerial(string serialNumber)
        => DiscoverDevices().FirstOrDefault(d => d.SerialNumber == serialNumber);

    /// <summary>
    /// Discovers a specific device by index (0-based).
    /// </summary>
    public static IMirageDevice? GetDeviceAt(int index)
    {
        var devices = DiscoverDevices().ToList();
        return index >= 0 && index < devices.Count ? devices[index] : null;
    }

    /// <summary>
    /// Gets the count of discovered devices.
    /// </summary>
    public static int GetDeviceCount() => DiscoverDevices().Count();
}
