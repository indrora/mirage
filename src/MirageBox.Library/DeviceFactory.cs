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
        DeviceProtocolVariant ProtocolVariant);

    /// <summary>
    /// Known device identifiers for various Mirage-compatible devices.
    /// </summary>
    public static class KnownDevices
    {
        /// <summary>Mirabox / Ajazz Mirajazz</summary>
        public const ushort MiraboxVendorId = 0x294B;
        public const ushort MiraboxProductId = 0x0171;
        
        /// <summary>Somfon devices</summary>
        public const ushort SomfonVendorId = 0x2E8E;
        public const ushort SomfonVendorId2 = 0x1500; // Some Somfon models use a different VID
        
        /// <summary>Ajazz devices</summary>
        public const ushort AjazzVendorId = 0x3256;

        /// <summary>Stream Controller SE compatible model (strmctrl)</summary>
        public const ushort StreamControllerSeVendorId = 0x1500;
        public const ushort StreamControllerSeProductId = 0x3001;
    }

    private static readonly DeviceProfile[] Profiles =
    {
        // Stream Controller SE layout from strmctrl: 6 display buttons + 3 side buttons + 3 knobs.
        new("StreamControllerSE", KnownDevices.StreamControllerSeVendorId, KnownDevices.StreamControllerSeProductId, 9, 3, DeviceProtocolVariant.StreamControllerSe),
        // Existing Mirage devices using snapshot/bitfield reports.
        new("Mirabox", KnownDevices.MiraboxVendorId, KnownDevices.MiraboxProductId, 15, 4, DeviceProtocolVariant.LegacyBitfield),
    };

    /// <summary>
    /// Discovers all connected Mirabox/Ajazz/Somfon devices.
    /// </summary>
    public static IEnumerable<IMirageDevice> DiscoverDevices()
    {
        var devices = new List<IMirageDevice>();
        int deviceIndex = 0;

        foreach (var profile in Profiles)
        {
            var matchedDevices = HidDevices.Enumerate(profile.VendorId, profile.ProductId);
            foreach (var device in matchedDevices)
            {
                var serialNumber = $"{profile.Name}-{deviceIndex}";
                var mirageDevice = new MirageDevice(
                    device,
                    serialNumber,
                    profile.ButtonCount,
                    profile.EncoderCount,
                    profile.ProtocolVariant);

                devices.Add(mirageDevice);
                deviceIndex++;
            }

            // Some vendors reuse VIDs with different PIDs. Keep loose VID-only fallback for Somfon.
            if (profile.Name == "Mirabox")
            {
                var somfonDevices = HidDevices.Enumerate(KnownDevices.SomfonVendorId)
                    .Concat(HidDevices.Enumerate(KnownDevices.SomfonVendorId2));

                foreach (var device in somfonDevices)
                {
                    // 0x1500:0x3001 is handled by the StreamControllerSE profile.
                    if (device.Attributes.VendorId == KnownDevices.StreamControllerSeVendorId
                        && device.Attributes.ProductId == KnownDevices.StreamControllerSeProductId)
                    {
                        continue;
                    }

                    var serialNumber = $"Somfon-{deviceIndex}";
                    var mirageDevice = new MirageDevice(
                        device,
                        serialNumber,
                        buttonCount: 12,
                        encoderCount: 3,
                        protocolVariant: DeviceProtocolVariant.LegacyBitfield);

                    devices.Add(mirageDevice);
                    deviceIndex++;
                }
            }
        }

        return devices;
    }


    /// <summary>
    /// Discovers a specific device by serial number.
    /// </summary>
    public static IMirageDevice? FindDeviceBySerial(string serialNumber)
    {
        return DiscoverDevices().FirstOrDefault(d => d.SerialNumber == serialNumber);
    }

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
    public static int GetDeviceCount()
    {
        return DiscoverDevices().Count();
    }
}
