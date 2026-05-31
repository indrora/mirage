namespace MirageBox;

/// <summary>
/// Parses HID reports from Mirabox/Ajazz/Somfon devices.
/// </summary>
internal static class HidReportParser
{
    private static readonly HashSet<byte> StreamControllerPressControls =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06,
        0x25, 0x30, 0x31,
        0x35, 0x33, 0x34
    ];

    private static readonly HashSet<byte> StreamControllerRotationControls =
    [
        0x51, 0x50,
        0x91, 0x90,
        0x61, 0x60
    ];

    /// <summary>
    /// Protocol version constant for device initialization.
    /// </summary>
    public const byte ProtocolVersion = 3;

    /// <summary>
    /// Parses a raw HID report into device input information.
    /// </summary>
    public static DeviceInput? ParseReport(byte[] report, int buttonCount, int encoderCount, DeviceProtocolVariant variant)
    {
        if (report == null || report.Length < 2)
            return null;

        if (variant == DeviceProtocolVariant.AckPrefix)
            return ParseAckPrefixReport(report, buttonCount, encoderCount);

        return ParseBitfieldReport(report, buttonCount, encoderCount);
    }

    private static DeviceInput? ParseBitfieldReport(byte[] report, int buttonCount, int encoderCount)
    {
        try
        {
            int offset = 1; // Skip report ID

            var buttonStates = new bool[buttonCount];
            for (int i = 0; i < buttonCount && offset + i / 8 < report.Length; i++)
            {
                int byteIndex = offset + i / 8;
                int bitIndex = i % 8;
                buttonStates[i] = (report[byteIndex] & (1 << bitIndex)) != 0;
            }

            offset += (buttonCount + 7) / 8;
            var encoderStates = new bool[encoderCount];
            for (int i = 0; i < encoderCount && offset + i / 8 < report.Length; i++)
            {
                int byteIndex = offset + i / 8;
                int bitIndex = i % 8;
                encoderStates[i] = (report[byteIndex] & (1 << bitIndex)) != 0;
            }

            offset += (encoderCount + 7) / 8;
            var encoderRotations = new sbyte[encoderCount];
            for (int i = 0; i < encoderCount && offset + i < report.Length; i++)
            {
                encoderRotations[i] = (sbyte)report[offset + i];
            }

            return DeviceInput.FromSnapshot(buttonStates, encoderStates, encoderRotations);
        }
        catch
        {
            return null;
        }
    }

    // Parses ACK-prefix input format (protocol v1+).
    // Input reports start with "ACK" (0x41 0x43 0x4B); event code at offset 9, state at offset 10.
    // Protocol v3 uses state 0x01=press, 0x02=release. Earlier versions use 0=release, 1=press.
    private static DeviceInput? ParseAckPrefixReport(byte[] report, int buttonCount, int encoderCount)
    {
        if (report.Length < 2)
            return null;

        // Preferred offsets from the Go implementation first.
        var preferredOffsets = new[] { 9, 10, 8, 11, 7, 12 };
        foreach (var offset in preferredOffsets)
        {
            if (TryParseStreamControllerControl(report, offset, buttonCount, encoderCount, out var parsed))
                return parsed;
        }

        // Fallback: scan the whole report for any known control code.
        for (int i = 0; i < report.Length - 1; i++)
        {
            if (TryParseStreamControllerControl(report, i, buttonCount, encoderCount, out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool TryParseStreamControllerControl(
        byte[] report,
        int controlOffset,
        int buttonCount,
        int encoderCount,
        out DeviceInput? parsed)
    {
        parsed = null;

        if (controlOffset < 0 || controlOffset + 1 >= report.Length)
            return false;

        byte control = report[controlOffset];
        byte state = report[controlOffset + 1];

        if (!StreamControllerPressControls.Contains(control) && !StreamControllerRotationControls.Contains(control))
            return false;

        // 0x01..0x06 are display button presses.
        if (control >= 0x01 && control <= 0x06)
        {
            // Accept state 0x00/0x01 (v0/v1 protocol) and 0x01/0x02 (v3 protocol).
            // Pressed = 0x01; released = 0x00 or 0x02.
            if (state > 2)
                return false;

            int displayIndex = control - 1;
            if (displayIndex >= buttonCount)
                return false;

            parsed = DeviceInput.FromDeltaButton(buttonCount, encoderCount, displayIndex, state == 1);
            return true;
        }

        if (control == 0x25 || control == 0x30 || control == 0x31)
        {
            if (state > 2)
                return false;

            int buttonIndex = control switch
            {
                0x25 => 6,
                0x30 => 7,
                _ => 8
            };

            if (buttonIndex >= buttonCount)
                return false;

            parsed = DeviceInput.FromDeltaButton(buttonCount, encoderCount, buttonIndex, state == 1);
            return true;
        }

        if (control == 0x35 || control == 0x33 || control == 0x34)
        {
            if (state > 2)
                return false;

            int encoderIndex = control switch
            {
                0x35 => 0,
                0x33 => 1,
                _ => 2
            };

            if (encoderIndex >= encoderCount)
                return false;

            parsed = DeviceInput.FromDeltaEncoderPress(buttonCount, encoderCount, encoderIndex, state == 1);
            return true;
        }

        if (control == 0x51 || control == 0x50 || control == 0x91 || control == 0x90 || control == 0x61 || control == 0x60)
        {
            int encoderIndex = control switch
            {
                0x51 or 0x50 => 0,
                0x91 or 0x90 => 1,
                _ => 2
            };

            if (encoderIndex >= encoderCount)
                return false;

            int delta = (control & 0x01) == 1 ? 1 : -1;
            parsed = DeviceInput.FromDeltaEncoderRotation(buttonCount, encoderCount, encoderIndex, delta);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates an initialization command for the device.
    /// </summary>
    public static byte[] CreateInitCommand()
    {
        var cmd = new List<byte> { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x44, 0x49, 0x53 };
        return cmd.ToArray();
    }

    /// <summary>
    /// Creates a brightness control command.
    /// </summary>
    public static byte[] CreateBrightnessCommand(byte percent)
    {
        percent = Math.Clamp(percent, (byte)0, (byte)100);
        var cmd = new List<byte> { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, percent };
        return cmd.ToArray();
    }

    /// <summary>
    /// Creates an LED brightness control command.
    /// </summary>
    public static byte[] CreateLedBrightnessCommand(byte percent)
    {
        percent = Math.Clamp(percent, (byte)0, (byte)100);
        var cmd = new List<byte> { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x42, 0x4C, 0x49, 0x47, percent };
        return cmd.ToArray();
    }

    /// <summary>
    /// Creates an LED color command.
    /// </summary>
    public static byte[] CreateLedColorCommand(byte[][] colors)
    {
        var cmd = new List<byte> { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x45, 0x54, 0x4C, 0x42 };
        foreach (var color in colors)
        {
            if (color.Length >= 3)
            {
                cmd.Add(color[0]); // R
                cmd.Add(color[1]); // G
                cmd.Add(color[2]); // B
            }
        }
        return cmd.ToArray();
    }

    /// <summary>
    /// Creates a keep-alive command.
    /// </summary>
    public static byte[] CreateKeepAliveCommand()
    {
        return new byte[] { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4F, 0x4E, 0x4E, 0x45, 0x43, 0x54 };
    }

    /// <summary>
    /// Creates a clear button display command.
    /// </summary>
    public static byte[] CreateClearButtonCommand(int buttonIndex)
    {
        // Params at data offset 10: [0x00, 0x00, 0x00, key_index (1-based)].
        return new byte[] { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)(buttonIndex + 1) };
    }

    /// <summary>
    /// Creates a clear all displays command.
    /// </summary>
    public static byte[] CreateClearAllCommand()
    {
        // Params: [0x00, 0x00, 0x00, 0xFF] — 0xFF clears all keys.
        return new byte[] { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF };
    }

    /// <summary>
    /// Creates a command that prepares device image upload for a display panel.
    /// </summary>
    public static byte[] CreateSetImageCommand(int buttonIndex, int imageLength)
    {
        if (buttonIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(buttonIndex));

        if (imageLength < 0)
            throw new ArgumentOutOfRangeException(nameof(imageLength));

        // Params at data offset 10: uint32_be(image_data_length), key_index (1-based).
        return new byte[]
        {
            0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x42, 0x41, 0x54, 
            (byte)(imageLength >> 24),
            (byte)(imageLength >> 16),
            (byte)(imageLength >> 8),
            (byte)(imageLength & 0xFF),
            (byte)(buttonIndex + 1)
        };
    }

    /// <summary>
    /// Creates a command that commits pending image operations.
    /// </summary>
    public static byte[] CreateStopCommand()
    {
        return new byte[] { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x54, 0x50 };
    }
}

/// <summary>
/// Represents parsed device input data.
/// </summary>
internal record DeviceInput(
    bool[] ButtonStates,
    bool[] EncoderPressStates,
    sbyte[] EncoderRotations,
    bool UseStateDiff,
    int? ChangedButtonIndex,
    bool? ChangedButtonState,
    int? ChangedEncoderPressIndex,
    bool? ChangedEncoderPressState)
{
    public static DeviceInput FromSnapshot(bool[] buttonStates, bool[] encoderStates, sbyte[] encoderRotations)
        => new(buttonStates, encoderStates, encoderRotations, true, null, null, null, null);

    public static DeviceInput FromDeltaButton(int buttonCount, int encoderCount, int buttonIndex, bool isPressed)
        => new(new bool[buttonCount], new bool[encoderCount], new sbyte[encoderCount], false, buttonIndex, isPressed, null, null);

    public static DeviceInput FromDeltaEncoderPress(int buttonCount, int encoderCount, int encoderIndex, bool isPressed)
        => new(new bool[buttonCount], new bool[encoderCount], new sbyte[encoderCount], false, null, null, encoderIndex, isPressed);

    public static DeviceInput FromDeltaEncoderRotation(int buttonCount, int encoderCount, int encoderIndex, int delta)
    {
        var rotations = new sbyte[encoderCount];
        rotations[encoderIndex] = (sbyte)delta;
        return new(new bool[buttonCount], new bool[encoderCount], rotations, false, null, null, null, null);
    }
}

public enum DeviceProtocolVariant
{
    /// <summary>
    /// Legacy snapshot bitfield input format used by original Mirabox devices.
    /// </summary>
    LegacyBitfield,

    /// <summary>
    /// ACK-prefix input format used by StreamDock / CRT-protocol devices (protocol v1+).
    /// Input reports begin with "ACK" (0x41 0x43 0x4B); event code at byte 9, state at byte 10.
    /// Protocol v3 devices use state 0x01=press, 0x02=release.
    /// </summary>
    AckPrefix
}
