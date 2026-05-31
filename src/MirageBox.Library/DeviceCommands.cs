namespace MirageBox.Library;

public static class DeviceCommands
{
    /// <summary>
    /// CRT protocol commands for StreamDock device communication.
    /// 
    /// Parameters always start at data offset 10 (byte index 11 counting report_id).
    /// The padding between the command name and parameters varies based on command length.
    /// </summary>
    public enum Command
    {
        // Screen control
        /// <summary>Wake screen from sleep</summary>
        WakeScreen,
        /// <summary>Enter sleep/standby</summary>
        Sleep,
        /// <summary>Flush — apply pending image changes</summary>
        Flush,
        /// <summary>Set screen brightness (0–100)</summary>
        SetBrightness,

        // Button images
        /// <summary>Button image header with data length and key index</summary>
        ButtonImageData,
        /// <summary>Clear button image</summary>
        ClearButtonImage,
        /// <summary>Boot logo (persistent to flash)</summary>
        BootLogo,

        // Background / touchscreen
        /// <summary>Runtime background overlay</summary>
        SetBackgroundImage,
        /// <summary>Clear background layer</summary>
        ClearBackground,

        // Device control
        /// <summary>Switch device mode</summary>
        SwitchMode,
        /// <summary>Keep-alive heartbeat</summary>
        KeepAlive,
        /// <summary>Device config flags (LED follow, disconnect behavior, USB power, vibration, reset, boot video)</summary>
        ConfigFlags,

        // RGB LED strip (N4Pro, XL, M18)
        /// <summary>LED strip brightness (0–100)</summary>
        SetLedBrightness,
        /// <summary>Set LED colors (R,G,B per LED)</summary>
        SetLedColors,
        /// <summary>Reset LEDs to default</summary>
        ResetLeds,

        // Keyboard controls (K1Pro only)
        /// <summary>Keyboard backlight brightness (0–6)</summary>
        SetKeyboardBrightness,
        /// <summary>Keyboard lighting effect/speed (0–9 effects, 0–7 speed)</summary>
        SetKeyboardLighting,
        /// <summary>Keyboard RGB color (R, G, B)</summary>
        SetKeyboardColor,
        /// <summary>Keyboard OS mode (0x57='W' Windows, 0x4d='M' macOS)</summary>
        SetKeyboardMode,
    }

    // map: command -> (string, payload size)
    private static readonly Dictionary<Command, (string, int)> commandMap = new()
    {
        { Command.WakeScreen, ("DIS", 0) },
        { Command.Sleep, ("HAN", 0) },
        { Command.Flush, ("STP", 0) },
        { Command.SetBrightness, ("LIG", 3) },
        { Command.ButtonImageData, ("BAT", 5) },
        { Command.ClearButtonImage, ("CLE", 3) },
        { Command.BootLogo, ("LOG", 5) },
        { Command.SetBackgroundImage, ("BGPIC", 13) },
        { Command.ClearBackground, ("BGCLE", 1) },
        { Command.SwitchMode, ("MOD", 2) },
        { Command.KeepAlive, ("CONNECT", 0) },
        { Command.ConfigFlags, ("QUCMD", 6) },
        { Command.SetLedBrightness, ("LBLIG", 1) },
        { Command.SetLedColors, ("SETLB", 3) },
        { Command.ResetLeds, ("DELED", 0) },
        { Command.SetKeyboardBrightness, ("LLUM", 1) },
        { Command.SetKeyboardLighting, ("LMOD", 1) },
        { Command.SetKeyboardColor, ("COLOR", 3) },
        { Command.SetKeyboardMode, ("CPOS", 1) },
    };

    public static byte[] formatCommand(Command cmd, params byte[] parameters)
    {
        if (!commandMap.TryGetValue(cmd, out var cmdInfo))
            throw new ArgumentException($"Unknown command: {cmd}");

        var (cmdString, expectedLength) = cmdInfo;
        if (parameters.Length != expectedLength)
            throw new ArgumentException($"Command {cmd} expects {expectedLength} bytes of parameters, but got {parameters.Length}.");

        // CRT protocol: [report_id(1)] [CRT(3)] [0x00 0x00(2)] [CMD_NAME] [padding] [params at offset 10]
        // Total header: 1 (report_id) + 3 (CRT) + 2 (zeros) + cmd_name_length + padding = 10 bytes before params
        byte[] result = new byte[11 + parameters.Length]; // report_id(1) + CRT(3) + padding(2) + cmd(max 7) + params
        
        int offset = 0;
        result[offset++] = 0x00; // report_id
        
        // CRT header
        result[offset++] = 0x43; // 'C'
        result[offset++] = 0x52; // 'R'
        result[offset++] = 0x54; // 'T'
        
        // Two zero bytes
        result[offset++] = 0x00;
        result[offset++] = 0x00;
        
        // Command name
        var cmdBytes = System.Text.Encoding.ASCII.GetBytes(cmdString);
        Array.Copy(cmdBytes, 0, result, offset, cmdBytes.Length);
        offset += cmdBytes.Length;
        
        // Padding to align parameters at offset 10 (data offset, after report_id)
        // Offset calculation: report_id(1) + CRT(3) + zeros(2) + cmd_name_length = offset so far
        // We need to reach offset 10 before parameters start (in data portion)
        int headerWithoutParams = 1 + 3 + 2 + cmdBytes.Length; // total bytes before params
        int paddingNeeded = 10 - (3 + 2 + cmdBytes.Length); // padding in data portion (excluding report_id)
        
        for (int i = 0; i < paddingNeeded; i++)
            result[offset++] = 0x00;
        
        // Parameters at offset 10 (data offset, after report_id)
        Array.Copy(parameters, 0, result, offset, parameters.Length);
        
        return result;
    }

    // High-level command builders
    
    /// <summary>Wake screen from sleep</summary>
    public static byte[] WakeScreen() => formatCommand(Command.WakeScreen);
    
    /// <summary>Enter sleep/standby</summary>
    public static byte[] Sleep() => formatCommand(Command.Sleep);
    
    /// <summary>Flush — apply pending image changes</summary>
    public static byte[] Flush() => formatCommand(Command.Flush);
    
    /// <summary>Set screen brightness (0–100)</summary>
    public static byte[] SetBrightness(byte brightness) 
        => formatCommand(Command.SetBrightness, 0x00, 0x00, brightness);
    
    /// <summary>Button image header with data length and key index</summary>
    public static byte[] ButtonImageData(uint dataLength, byte keyIndex)
    {
        var lengthBytes = new byte[] 
        { 
            (byte)((dataLength >> 24) & 0xFF),
            (byte)((dataLength >> 16) & 0xFF),
            (byte)((dataLength >> 8) & 0xFF),
            (byte)(dataLength & 0xFF)
        };
        return formatCommand(Command.ButtonImageData, lengthBytes[0], lengthBytes[1], lengthBytes[2], lengthBytes[3], keyIndex);
    }
    
    /// <summary>Clear button image</summary>
    public static byte[] ClearButtonImage(byte key) 
        => formatCommand(Command.ClearButtonImage, 0x00, 0x00, 0x00, key);
    
    /// <summary>Boot logo (persistent to flash)</summary>
    public static byte[] BootLogo(uint dataLength, byte target)
    {
        var lengthBytes = new byte[] 
        { 
            (byte)((dataLength >> 24) & 0xFF),
            (byte)((dataLength >> 16) & 0xFF),
            (byte)((dataLength >> 8) & 0xFF),
            (byte)(dataLength & 0xFF)
        };
        return formatCommand(Command.BootLogo, lengthBytes[0], lengthBytes[1], lengthBytes[2], lengthBytes[3], target);
    }
    
    /// <summary>Runtime background overlay</summary>
    public static byte[] SetBackgroundImage(uint dataLength, ushort x, ushort y, ushort width, ushort height, byte fbLayer)
    {
        var lengthBytes = new byte[] 
        { 
            (byte)((dataLength >> 24) & 0xFF),
            (byte)((dataLength >> 16) & 0xFF),
            (byte)((dataLength >> 8) & 0xFF),
            (byte)(dataLength & 0xFF)
        };
        var xBytes = new byte[] { (byte)((x >> 8) & 0xFF), (byte)(x & 0xFF) };
        var yBytes = new byte[] { (byte)((y >> 8) & 0xFF), (byte)(y & 0xFF) };
        var wBytes = new byte[] { (byte)((width >> 8) & 0xFF), (byte)(width & 0xFF) };
        var hBytes = new byte[] { (byte)((height >> 8) & 0xFF), (byte)(height & 0xFF) };
        
        return formatCommand(Command.SetBackgroundImage, 
            lengthBytes[0], lengthBytes[1], lengthBytes[2], lengthBytes[3],
            xBytes[0], xBytes[1], yBytes[0], yBytes[1],
            wBytes[0], wBytes[1], hBytes[0], hBytes[1], fbLayer);
    }
    
    /// <summary>Clear background layer</summary>
    public static byte[] ClearBackground(byte position) 
        => formatCommand(Command.ClearBackground, position);
    
    /// <summary>Switch device mode</summary>
    public static byte[] SwitchMode(byte mode) 
        => formatCommand(Command.SwitchMode, 0x00, (byte)(mode + 0x30));
    
    /// <summary>Keep-alive heartbeat</summary>
    public static byte[] KeepAlive() => formatCommand(Command.KeepAlive);
    
    /// <summary>Device config flags</summary>
    public static byte[] ConfigFlags(byte ledFollow, byte keyLightOnDisconnect, byte checkUsbPower, 
        byte enableVibration, byte resetUsbReport, byte enableBootVideo)
        => formatCommand(Command.ConfigFlags, ledFollow, keyLightOnDisconnect, checkUsbPower, 
            enableVibration, resetUsbReport, enableBootVideo);
    
    /// <summary>LED strip brightness (0–100)</summary>
    public static byte[] SetLedBrightness(byte brightness) 
        => formatCommand(Command.SetLedBrightness, brightness);
    
    /// <summary>Set LED colors (R,G,B per LED)</summary>
    public static byte[] SetLedColors(params byte[] colors) 
        => formatCommand(Command.SetLedColors, colors);
    
    /// <summary>Reset LEDs to default</summary>
    public static byte[] ResetLeds() => formatCommand(Command.ResetLeds);
    
    /// <summary>Keyboard backlight brightness (0–6)</summary>
    public static byte[] SetKeyboardBrightness(byte brightness) 
        => formatCommand(Command.SetKeyboardBrightness, brightness);
    
    /// <summary>Keyboard lighting effect/speed (0–9 effects, 0–7 speed)</summary>
    public static byte[] SetKeyboardLighting(byte value) 
        => formatCommand(Command.SetKeyboardLighting, value);
    
    /// <summary>Keyboard RGB color (R, G, B)</summary>
    public static byte[] SetKeyboardColor(byte r, byte g, byte b) 
        => formatCommand(Command.SetKeyboardColor, r, g, b);
    
    /// <summary>Keyboard OS mode (0x57='W' Windows, 0x4d='M' macOS)</summary>
    public static byte[] SetKeyboardMode(byte mode) 
        => formatCommand(Command.SetKeyboardMode, mode);




}