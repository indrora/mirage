namespace MirageBox;

using HidSharp;

/// <summary>
/// Describes the physical layout and wire-protocol button codes for a device.
/// <para>
/// <see cref="ImageButtonCodes"/> lists the HID control codes (AckPrefix protocol)
/// or bit-position indices (LegacyBitfield protocol) that correspond to buttons
/// with display screens, in the order they map to local image-button indices 0, 1, 2 …
/// </para>
/// <para>
/// <see cref="TactileButtonCodes"/> lists the codes for screen-less physical buttons,
/// in the order they map to local tactile-button indices 0, 1, 2 …
/// </para>
/// </summary>
public sealed record DeviceProfile(
    string Name,
    ushort VendorId,
    ushort ProductId,
    byte[] ImageButtonCodes,
    byte[] TactileButtonCodes,
    int EncoderCount,
    int LedCount,
    int ImageWidth,
    int ImageHeight,
    int RotationDegrees,
    int ScreenWidth,
    int ScreenHeight,
    byte HidReportId,
    DeviceProtocolVariant ProtocolVariant)
{
    public int ImageButtonCount  => ImageButtonCodes.Length;
    public int TactileButtonCount => TactileButtonCodes.Length;
    public int ButtonCount       => ImageButtonCount + TactileButtonCount;
}

/// <summary>
/// Provides factory methods for discovering and connecting to Mirabox/Ajazz/Somfon devices.
/// </summary>
public static class DeviceFactory
{
    // Sequence helpers — produce byte codes [start, start+1, ..., start+count-1]
    private static byte[] Seq(int start, int count)
    {
        var r = new byte[count];
        for (int i = 0; i < count; i++) r[i] = (byte)(start + i);
        return r;
    }

    private static readonly Dictionary<(ushort, ushort), DeviceProfile> DevicesByVidPid = new()
    {
        // StreamController SE / Somfon rebadge
        // 6 display buttons (codes 0x01-0x06) + 3 tactile side buttons (0x25, 0x30, 0x31)
        { (0x1500, 0x3001), new("StreamControllerSE", 0x1500, 0x3001,
            ImageButtonCodes:   [0x01,0x02,0x03,0x04,0x05,0x06],
            TactileButtonCodes: [0x25,0x30,0x31],
            EncoderCount: 3, LedCount: 0,
            ImageWidth: 64, ImageHeight: 64, RotationDegrees: 90,
            ScreenWidth: 320, ScreenHeight: 240, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // N4Pro — 2×5 display grid, 4 knobs, 4 LED rings
        { (0x5548, 0x1008), new("N4Pro", 0x5548, 0x1008,
            ImageButtonCodes:   Seq(0x01, 10),
            TactileButtonCodes: [],
            EncoderCount: 4, LedCount: 4,
            ImageWidth: 112, ImageHeight: 112, RotationDegrees: 180,
            ScreenWidth: 800, ScreenHeight: 480, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },
        { (0x5548, 0x1021), new("N4Pro", 0x5548, 0x1021,
            ImageButtonCodes:   Seq(0x01, 10),
            TactileButtonCodes: [],
            EncoderCount: 4, LedCount: 4,
            ImageWidth: 112, ImageHeight: 112, RotationDegrees: 180,
            ScreenWidth: 800, ScreenHeight: 480, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // N4 — 2×5 display grid, 4 knobs, no LEDs
        { (0x6602, 0x1001), new("N4", 0x6602, 0x1001,
            ImageButtonCodes:   Seq(0x01, 10),
            TactileButtonCodes: [],
            EncoderCount: 4, LedCount: 0,
            ImageWidth: 112, ImageHeight: 112, RotationDegrees: 180,
            ScreenWidth: 800, ScreenHeight: 480, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1007), new("N4", 0x6603, 0x1007,
            ImageButtonCodes:   Seq(0x01, 10),
            TactileButtonCodes: [],
            EncoderCount: 4, LedCount: 0,
            ImageWidth: 112, ImageHeight: 112, RotationDegrees: 180,
            ScreenWidth: 800, ScreenHeight: 480, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // XL — 4×8 display grid, 2 toggle encoders, 6 LEDs
        { (0x5548, 0x1028), new("XL", 0x5548, 0x1028,
            ImageButtonCodes:   Seq(0x01, 32),
            TactileButtonCodes: [],
            EncoderCount: 2, LedCount: 6,
            ImageWidth: 80, ImageHeight: 80, RotationDegrees: 180,
            ScreenWidth: 1024, ScreenHeight: 600, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },
        { (0x5548, 0x1031), new("XL", 0x5548, 0x1031,
            ImageButtonCodes:   Seq(0x01, 32),
            TactileButtonCodes: [],
            EncoderCount: 2, LedCount: 6,
            ImageWidth: 80, ImageHeight: 80, RotationDegrees: 180,
            ScreenWidth: 1024, ScreenHeight: 600, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // M18 — 3×5 display grid, no encoders, 24 LEDs
        { (0x6603, 0x1009), new("M18", 0x6603, 0x1009,
            ImageButtonCodes:   Seq(0x01, 15),
            TactileButtonCodes: [],
            EncoderCount: 0, LedCount: 24,
            ImageWidth: 64, ImageHeight: 64, RotationDegrees: 0,
            ScreenWidth: 480, ScreenHeight: 272, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1012), new("M18", 0x6603, 0x1012,
            ImageButtonCodes:   Seq(0x01, 15),
            TactileButtonCodes: [],
            EncoderCount: 0, LedCount: 24,
            ImageWidth: 64, ImageHeight: 64, RotationDegrees: 0,
            ScreenWidth: 480, ScreenHeight: 272, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // K1Pro — 2×3 display grid, 3 knobs
        { (0x6603, 0x1015), new("K1Pro", 0x6603, 0x1015,
            ImageButtonCodes:   [0x01,0x02,0x03,0x04,0x05,0x06],
            TactileButtonCodes: [],
            EncoderCount: 3, LedCount: 0,
            ImageWidth: 64, ImageHeight: 64, RotationDegrees: 90,
            ScreenWidth: 800, ScreenHeight: 480, HidReportId: 0x04,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1019), new("K1Pro", 0x6603, 0x1019,
            ImageButtonCodes:   [0x01,0x02,0x03,0x04,0x05,0x06],
            TactileButtonCodes: [],
            EncoderCount: 3, LedCount: 0,
            ImageWidth: 64, ImageHeight: 64, RotationDegrees: 90,
            ScreenWidth: 800, ScreenHeight: 480, HidReportId: 0x04,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // N1 — 3×5 display grid, 1 knob, portrait
        { (0x6603, 0x1011), new("N1", 0x6603, 0x1011,
            ImageButtonCodes:   Seq(0x01, 15),
            TactileButtonCodes: [],
            EncoderCount: 1, LedCount: 0,
            ImageWidth: 96, ImageHeight: 96, RotationDegrees: 0,
            ScreenWidth: 480, ScreenHeight: 854, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },
        { (0x6603, 0x1000), new("N1", 0x6603, 0x1000,
            ImageButtonCodes:   Seq(0x01, 15),
            TactileButtonCodes: [],
            EncoderCount: 1, LedCount: 0,
            ImageWidth: 96, ImageHeight: 96, RotationDegrees: 0,
            ScreenWidth: 480, ScreenHeight: 854, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // N3 — 2×3 display grid, 3 knobs
        { (0x6602, 0x1003), new("N3", 0x6602, 0x1003,
            ImageButtonCodes:   [0x01,0x02,0x03,0x04,0x05,0x06],
            TactileButtonCodes: [],
            EncoderCount: 3, LedCount: 0,
            ImageWidth: 64, ImageHeight: 64, RotationDegrees: -90,
            ScreenWidth: 320, ScreenHeight: 240, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.AckPrefix) },

        // Legacy Mirabox/Ajazz — 15 buttons, snapshot bitfield protocol.
        // Codes here are bit-position indices 0-14, not AckPrefix wire codes.
        { (0x294B, 0x0171), new("Mirabox", 0x294B, 0x0171,
            ImageButtonCodes:   Seq(0x00, 15),
            TactileButtonCodes: [],
            EncoderCount: 4, LedCount: 0,
            ImageWidth: 72, ImageHeight: 72, RotationDegrees: 0,
            ScreenWidth: 0, ScreenHeight: 0, HidReportId: 0x00,
            ProtocolVariant: DeviceProtocolVariant.LegacyBitfield) },
    };

    /// <summary>Discovers all connected StreamDock-compatible devices.</summary>
    public static IEnumerable<IMirageDevice> DiscoverDevices()
    {
        var devices = new List<IMirageDevice>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int deviceIndex = 0;

        foreach (var (vid, pid) in new HashSet<(ushort, ushort)>(DevicesByVidPid.Keys))
        {
            var profile = DevicesByVidPid[(vid, pid)];

            foreach (var hidDevice in DeviceList.Local.GetHidDevices(vid, pid))
            {
                var path = hidDevice.DevicePath ?? "";
                if (!seenPaths.Add(path)) continue;

                try
                {
                    var descriptor = hidDevice.GetReportDescriptor();
                    uint topUsage = descriptor.DeviceItems.FirstOrDefault()?.Usages.GetAllValues().FirstOrDefault() ?? 0u;
                    int usagePage = (int)(topUsage >> 16);
                    if ((usagePage & 0xFF00) != 0xFF00) continue;
                }
                catch { continue; }

                string serialNumber;
                try
                {
                    var raw = hidDevice.GetSerialNumber();
                    serialNumber = string.IsNullOrWhiteSpace(raw)
                        ? $"{profile.Name}-{deviceIndex}"
                        : raw;
                }
                catch { serialNumber = $"{profile.Name}-{deviceIndex}"; }

                Console.WriteLine($"Found device: VID={vid:X4} PID={pid:X4} Serial='{serialNumber}' Path='{path}' Profile='{profile.Name}'");

                devices.Add(new MirageDevice(hidDevice, serialNumber, profile));
                deviceIndex++;
            }
        }

        return devices;
    }

    public static IMirageDevice? FindDeviceBySerial(string serialNumber)
        => DiscoverDevices().FirstOrDefault(d => d.SerialNumber == serialNumber);

    public static IMirageDevice? GetDeviceAt(int index)
    {
        var devices = DiscoverDevices().ToList();
        return index >= 0 && index < devices.Count ? devices[index] : null;
    }

    public static int GetDeviceCount() => DiscoverDevices().Count();
}
