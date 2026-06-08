# MirageBox .NET Control Surface Library

A .NET 9.0 library for interacting with professional HID control surface devices including StreamDeck-style devices, Mirabox, Ajazz, and other hardware. Supports device discovery, button/encoder events, image rendering, and full device control via async APIs.

## Features

- 🎛️ **Multi-Device Support** - StreamDeck, StreamControllerSE, N4/N4Pro/N3/N1, XL, M18, K1Pro, Mirabox, and more
- 🎨 **Image Rendering** - Per-button image rendering with SkiaSharp
- 🎯 **Hardware Events** - Button presses, encoder rotations, tactile inputs
- 📊 **Device Discovery** - Automatic enumeration of connected devices  
- ⚡ **Fully Async** - Complete async/await support
- 🔧 **Hardware Control** - Brightness, LED colors, display management, device modes
- 🧪 **Simulator Support** - Test without hardware using the built-in simulator

## Requirements

- .NET 9.0 SDK or later
- SkiaSharp dependencies (platform-specific)

## Building

```bash
dotnet build
```

## Development

The project consists of three main components:

- **MirageBox.Library** - Core control surface API
- **MirageBox.TinyGauges** - SkiaSharp-based graphics library for rendering gauge components
- **MirageBox.Oasis** - Graphics rendering demo and performance benchmark (run with `dotnet run --project src/MirageBox.Oasis`)

Run the graphics demo:
```bash
dotnet run --project src/MirageBox.Oasis
```

Or use the simulator for testing without hardware:
```bash
dotnet run --project src/MirageBox.Oasis -- --simulator
```

## Quick Start

```csharp
using MirageBox;

// Discover devices
var devices = DeviceFactory.DiscoverDevices();
if (devices.Count == 0)
{
    Console.WriteLine("No devices found!");
    return;
}

var device = devices.First();
Console.WriteLine($"Device: {device.Name}, Buttons: {device.ButtonCount}, Encoders: {device.EncoderCount}");

// Initialize the device
await device.InitializeAsync();

// Subscribe to button events
device.ButtonChanged += (s, e) => 
{
    Console.WriteLine($"Button {e.ButtonIndex}: {(e.IsPressed ? "Pressed" : "Released")}");
};

// Subscribe to encoder events
device.EncoderRotated += (s, e) => 
{
    Console.WriteLine($"Encoder {e.EncoderIndex}: {e.RotationDelta} steps");
};

device.EncoderPressed += (s, e) =>
{
    Console.WriteLine($"Encoder {e.EncoderIndex}: Pressed");
};

// Start listening for events
await device.StartListeningAsync();

// Do your thing...
await Task.Delay(30000); // Run for 30 seconds

// Clean up
await device.StopListeningAsync();
device.Dispose();
```

## API Overview

### Device Discovery

- `DeviceFactory.DiscoverDevices()` - Find all connected devices
- `DeviceFactory.FindDeviceBySerial(string)` - Find device by serial number
- `DeviceFactory.GetDeviceAt(int)` - Get device at index

### Events

- `ButtonChanged` - Fires when a button state changes
- `EncoderPressed` - Fires when an encoder is pressed
- `EncoderReleased` - Fires when an encoder is released
- `EncoderRotated` - Fires when an encoder is rotated

### Device Control

- `InitializeAsync()` - Initialize the device
- `StartListeningAsync()` - Begin listening for input events
- `StopListeningAsync()` - Stop listening for input events
- `SetBrightnessAsync(byte)` - Set display brightness (0-100)
- `SetLedBrightnessAsync(byte)` - Set encoder LED brightness (0-100)
- `SetLedColorsAsync(byte[][])` - Set individual encoder LED colors
- `ClearButtonDisplayAsync(int)` - Clear a button's display
- `ClearAllDisplaysAsync()` - Clear all button displays
- `KeepAliveAsync()` - Send a keep-alive heartbeat

## Event Args

### ButtonEventArgs
- `ButtonIndex` - Which button changed (0-based)
- `IsPressed` - true if pressed, false if released
- `AllButtonStates` - Current state of all buttons

### EncoderEventArgs
- `EncoderIndex` - Which encoder changed (0-based)
- `IsPressed` - true if pressed (for press/release events)
- `RotationDelta` - Rotation amount (positive = clockwise, negative = counter-clockwise)
- `AllEncoderPressStates` - Current press state of all encoders

## Project Structure

```
MirageBox/
├── src/
│   ├── MirageBox.Library/
│   │   ├── DeviceCommands.cs         - Protocol command definitions
│   │   ├── DeviceEvents.cs           - Event argument classes
│   │   ├── DeviceFactory.cs          - Device discovery & profiles
│   │   ├── HidReportParser.cs        - HID protocol parsing
│   │   ├── IMirageDevice.cs          - Device interface
│   │   ├── MirageDevice.cs           - Device implementation
│   │   ├── SimulatorDevice.cs        - Software simulator
│   │   └── Protocol.md               - Protocol documentation
│   ├── MirageBox.Oasis/
│   │   ├── Program.cs                - Graphics demo/benchmark
│   │   ├── GaugePanelComponent.cs    - UI components
│   │   └── ResourceLoader.cs         - Asset loading
│   └── MirageBox.TinyGauges/
│       ├── AnimationController.cs    - Animation framework
│       ├── Graphics.cs               - Rendering utilities
│       ├── Theme.cs                  - Visual theming
│       └── ITinyGuage.cs             - Component interface
├── MirageBox.sln
├── global.json
└── README.md
```

## Dependencies

- [HIDSharp](https://github.com/Jcw87/HIDSharp) - HID device communication
- [SkiaSharp](https://github.com/mono/SkiaSharp) - 2D graphics rendering
- [Silk.NET](https://www.silk.net/) - SDL bindings (for advanced graphics)

## Supported Devices

The library supports a wide range of professional HID control surface devices:

| Device | Buttons | Encoders | LEDs | Screen | Notes |
|--------|---------|----------|------|--------|-------|
| **StreamControllerSE** | 6 display + 3 tactile | 3 | - | 64×64 | Somfon rebadge |
| **N4Pro** | 10 display | 4 | 4 | 112×112 | LED rings, 2×5 grid |
| **N4** | 10 display | 4 | - | 112×112 | 2×5 grid |
| **XL** | 32 display | 2 | 6 | 80×80 | 4×8 grid |
| **M18** | 18 display | 2 | 3 | 128×64 | 3×6 grid |
| **K1Pro** | 20 display | 1 | 2 | 128×128 | 4×5 grid |
| **N1** | 4 display | 1 | - | 128×64 | Compact |
| **N3** | 9 display | 1 | - | 128×64 | 3×3 grid |
| **Mirabox** | 1 display | 3 | - | 128×64 | Original Mirabox |

Each device supports:
- Per-button image rendering with per-button screen rotation
- Configurable brightness and LED colors
- Keep-alive heartbeats
- Multiple protocol variants (legacy bitfield and ack-prefix)

## Protocol Reference

This implementation is based on the [Rust mirajazz library](https://github.com/4ndv/mirajazz).

## License

MIT License - See LICENSE file for details
