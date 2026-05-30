# Mirabox/Ajazz/Somfon .NET Library

A .NET 8.0 class library for interacting with Mirabox, Ajazz, and Somfon HID control surface devices using .NET events.

## Features

- 🎛️ **Button Events** - Detect button presses and releases
- 🔄 **Encoder Events** - Monitor encoder/knob rotations and presses
- 📊 **Device Discovery** - Automatically find connected devices
- ⚡ **Async API** - Full async/await support
- 🎨 **Device Control** - Set brightness, LED colors, and clear displays

## Quick Start

### Installation

Add the NuGet package to your project:
```bash
dotnet add package MirageBox
```

### Basic Usage

```csharp
using MirageBox;

// Discover devices
var devices = DeviceFactory.DiscoverDevices();
var device = devices.First();

// Initialize
await device.InitializeAsync();

// Set up event handlers
device.ButtonChanged += (s, e) => 
{
    Console.WriteLine($"Button {e.ButtonIndex}: {(e.IsPressed ? "Pressed" : "Released")}");
};

device.EncoderRotated += (s, e) => 
{
    Console.WriteLine($"Encoder {e.EncoderIndex}: Rotated {e.RotationDelta} steps");
};

// Start listening
await device.StartListeningAsync();

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
│   │   ├── DeviceEvents.cs       - Event arg classes
│   │   ├── DeviceFactory.cs      - Device discovery
│   │   ├── HidReportParser.cs    - HID protocol parsing
│   │   ├── IMirageDevice.cs      - Device interface
│   │   └── MirageDevice.cs       - Device implementation
│   └── MirageBox.Sample/
│       └── Program.cs             - Console demo
├── MirageBox.sln
├── global.json
└── README.md
```

## Dependencies

- [HidLibrary](https://github.com/mikeobrien/HidLibrary) - HID device communication

## Hardware Support

- **Mirabox / Ajazz Mirajazz** (VID: 0x294B, PID: 0x0171)
- Additional devices can be added to `DeviceFactory.KnownDevices`

## Protocol Reference

This implementation is based on the [Rust mirajazz library](https://github.com/4ndv/mirajazz).

## License

MIT License - See LICENSE file for details
