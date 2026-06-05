namespace MirageBox;

internal enum ButtonCategory { Image, Tactile }

/// <summary>
/// Parses HID reports from Mirabox/Ajazz/Somfon devices.
/// </summary>
internal static class HidReportParser
{
    // Encoder press control codes (device-independent, per CRT wire spec)
    private static readonly HashSet<byte> EncoderPressCodes = [0x35, 0x33, 0x34];

    // Encoder rotation control codes (device-independent)
    private static readonly HashSet<byte> EncoderRotationCodes =
        [0x51, 0x50, 0x91, 0x90, 0x61, 0x60];

    public const byte ProtocolVersion = 3;

    /// <summary>
    /// Parses a raw HID report into device input.
    /// </summary>
    /// <param name="imageButtonCodes">
    /// Ordered list of wire codes that map to image buttons; index in this array
    /// becomes the local image-button index in <see cref="DeviceInput"/>.
    /// </param>
    /// <param name="tactileButtonCodes">
    /// Ordered list of wire codes that map to tactile buttons.
    /// </param>
    public static DeviceInput? ParseReport(
        byte[] report,
        byte[] imageButtonCodes,
        byte[] tactileButtonCodes,
        int encoderCount,
        DeviceProtocolVariant variant)
    {
        if (report == null || report.Length < 2)
            return null;

        if (variant == DeviceProtocolVariant.AckPrefix)
            return ParseAckPrefixReport(report, imageButtonCodes, tactileButtonCodes, encoderCount);

        // LegacyBitfield: all buttons are image buttons; codes are bit-position indices.
        return ParseBitfieldReport(report, imageButtonCodes.Length, encoderCount);
    }

    private static DeviceInput? ParseBitfieldReport(byte[] report, int imageButtonCount, int encoderCount)
    {
        try
        {
            int offset = 1;
            var imageStates = new bool[imageButtonCount];
            for (int i = 0; i < imageButtonCount && offset + i / 8 < report.Length; i++)
                imageStates[i] = (report[offset + i / 8] & (1 << (i % 8))) != 0;

            offset += (imageButtonCount + 7) / 8;
            var encoderPressStates = new bool[encoderCount];
            for (int i = 0; i < encoderCount && offset + i / 8 < report.Length; i++)
                encoderPressStates[i] = (report[offset + i / 8] & (1 << (i % 8))) != 0;

            offset += (encoderCount + 7) / 8;
            var encoderRotations = new sbyte[encoderCount];
            for (int i = 0; i < encoderCount && offset + i < report.Length; i++)
                encoderRotations[i] = (sbyte)report[offset + i];

            return DeviceInput.FromSnapshot(imageStates, encoderPressStates, encoderRotations);
        }
        catch { return null; }
    }

    private static DeviceInput? ParseAckPrefixReport(
        byte[] report,
        byte[] imageButtonCodes,
        byte[] tactileButtonCodes,
        int encoderCount)
    {
        if (report.Length < 2) return null;

        var preferredOffsets = new[] { 9, 10, 8, 11, 7, 12 };
        foreach (var offset in preferredOffsets)
        {
            if (TryParseControl(report, offset, imageButtonCodes, tactileButtonCodes, encoderCount, out var parsed))
                return parsed;
        }

        for (int i = 0; i < report.Length - 1; i++)
        {
            if (TryParseControl(report, i, imageButtonCodes, tactileButtonCodes, encoderCount, out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool TryParseControl(
        byte[] report, int controlOffset,
        byte[] imageButtonCodes, byte[] tactileButtonCodes,
        int encoderCount,
        out DeviceInput? parsed)
    {
        parsed = null;
        if (controlOffset < 0 || controlOffset + 1 >= report.Length) return false;

        byte control = report[controlOffset];
        byte state   = report[controlOffset + 1];

        // Image button?
        int imgIdx = Array.IndexOf(imageButtonCodes, control);
        if (imgIdx >= 0)
        {
            if (state > 2) return false;
            parsed = DeviceInput.FromDeltaButton(
                imageButtonCodes.Length, tactileButtonCodes.Length, encoderCount,
                imgIdx, ButtonCategory.Image, state == 1);
            return true;
        }

        // Tactile button?
        int tactIdx = Array.IndexOf(tactileButtonCodes, control);
        if (tactIdx >= 0)
        {
            if (state > 2) return false;
            parsed = DeviceInput.FromDeltaButton(
                imageButtonCodes.Length, tactileButtonCodes.Length, encoderCount,
                tactIdx, ButtonCategory.Tactile, state == 1);
            return true;
        }

        // Encoder press?
        if (EncoderPressCodes.Contains(control))
        {
            if (state > 2) return false;
            int encoderIndex = control switch { 0x35 => 0, 0x33 => 1, _ => 2 };
            if (encoderIndex >= encoderCount) return false;
            parsed = DeviceInput.FromDeltaEncoderPress(
                imageButtonCodes.Length, tactileButtonCodes.Length, encoderCount,
                encoderIndex, state == 1);
            return true;
        }

        // Encoder rotation?
        if (EncoderRotationCodes.Contains(control))
        {
            int encoderIndex = control switch
            {
                0x51 or 0x50 => 0,
                0x91 or 0x90 => 1,
                _            => 2
            };
            if (encoderIndex >= encoderCount) return false;
            int delta = (control & 0x01) == 1 ? 1 : -1;
            parsed = DeviceInput.FromDeltaEncoderRotation(
                imageButtonCodes.Length, tactileButtonCodes.Length, encoderCount,
                encoderIndex, delta);
            return true;
        }

        return false;
    }

    // ── Command builders (unchanged) ────────────────────────────────────────

    public static byte[] CreateInitCommand()
        => [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x44, 0x49, 0x53];

    public static byte[] CreateBrightnessCommand(byte percent)
    {
        percent = Math.Clamp(percent, (byte)0, (byte)100);
        return [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, percent];
    }

    public static byte[] CreateLedBrightnessCommand(byte percent)
    {
        percent = Math.Clamp(percent, (byte)0, (byte)100);
        return [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x42, 0x4C, 0x49, 0x47, percent];
    }

    public static byte[] CreateLedColorCommand(byte[][] colors)
    {
        var cmd = new List<byte> { 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x45, 0x54, 0x4C, 0x42 };
        foreach (var color in colors)
            if (color.Length >= 3) { cmd.Add(color[0]); cmd.Add(color[1]); cmd.Add(color[2]); }
        return cmd.ToArray();
    }

    public static byte[] CreateKeepAliveCommand()
        => [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4F, 0x4E, 0x4E, 0x45, 0x43, 0x54];

    public static byte[] CreateClearButtonCommand(int buttonIndex)
        => [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)(buttonIndex + 1)];

    public static byte[] CreateClearAllCommand()
        => [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF];

    public static byte[] CreateSetImageCommand(int buttonIndex, int imageLength)
    {
        if (buttonIndex < 0) throw new ArgumentOutOfRangeException(nameof(buttonIndex));
        if (imageLength < 0) throw new ArgumentOutOfRangeException(nameof(imageLength));
        return
        [
            0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x42, 0x41, 0x54,
            (byte)(imageLength >> 24),
            (byte)(imageLength >> 16),
            (byte)(imageLength >> 8),
            (byte)(imageLength & 0xFF),
            (byte)(buttonIndex + 1)
        ];
    }

    public static byte[] CreateStopCommand()
        => [0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x54, 0x50];
}

/// <summary>Parsed device input from a single HID report.</summary>
internal record DeviceInput(
    bool[] ImageButtonStates,
    bool[] TactileButtonStates,
    bool[] EncoderPressStates,
    sbyte[] EncoderRotations,
    bool UseStateDiff,
    int? ChangedButtonIndex,
    ButtonCategory? ChangedButtonCategory,
    bool? ChangedButtonState,
    int? ChangedEncoderPressIndex,
    bool? ChangedEncoderPressState)
{
    public static DeviceInput FromSnapshot(bool[] imageStates, bool[] encoderStates, sbyte[] encoderRotations)
        => new(imageStates, Array.Empty<bool>(), encoderStates, encoderRotations,
               true, null, null, null, null, null);

    public static DeviceInput FromDeltaButton(
        int imageCount, int tactileCount, int encoderCount,
        int localIndex, ButtonCategory category, bool isPressed)
        => new(new bool[imageCount], new bool[tactileCount], new bool[encoderCount], new sbyte[encoderCount],
               false, localIndex, category, isPressed, null, null);

    public static DeviceInput FromDeltaEncoderPress(
        int imageCount, int tactileCount, int encoderCount,
        int encoderIndex, bool isPressed)
        => new(new bool[imageCount], new bool[tactileCount], new bool[encoderCount], new sbyte[encoderCount],
               false, null, null, null, encoderIndex, isPressed);

    public static DeviceInput FromDeltaEncoderRotation(
        int imageCount, int tactileCount, int encoderCount,
        int encoderIndex, int delta)
    {
        var rotations = new sbyte[encoderCount];
        rotations[encoderIndex] = (sbyte)delta;
        return new(new bool[imageCount], new bool[tactileCount], new bool[encoderCount], rotations,
                   false, null, null, null, null, null);
    }
}

public enum DeviceProtocolVariant
{
    LegacyBitfield,
    AckPrefix
}
