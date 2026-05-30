# MirageBox .NET Development

## Setup

The project uses .NET 9.0. Ensure you have the .NET SDK installed.

```bash
dotnet --version
```

## Building

```bash
dotnet build
```

## Running the Sample

```bash
dotnet run --project src/MirageBox.Sample/MirageBox.Sample.csproj
```

## Project Structure

- **MirageBox.Library** - Core library with device handling and .NET events
  - `DeviceEvents.cs` - Event argument classes (ButtonEventArgs, EncoderEventArgs)
  - `DeviceFactory.cs` - Device discovery and factory methods
  - `HidReportParser.cs` - HID protocol parsing and command generation
  - `IMirageDevice.cs` - Device interface definition
  - `MirageDevice.cs` - Device implementation with event handling

- **MirageBox.Sample** - Console application demonstrating usage
  - Discovers and connects to devices
  - Demonstrates button and encoder event handling
  - Sets device brightness and LED colors

## Key Features

- **Button Events** - `ButtonChanged` event fires when buttons are pressed/released
- **Encoder Events** - `EncoderPressed`, `EncoderReleased`, `EncoderRotated` events
- **Device Control** - Set brightness, LED colors, clear displays
- **Async API** - Full async/await support for all operations
- **Device Discovery** - Automatic enumeration of connected devices

## Dependencies

- HidLibrary 3.3.40 - For HID device communication

## Testing

1. Connect a Mirabox/Ajazz/Somfon device
2. Run `dotnet run --project src/MirageBox.Sample/MirageBox.Sample.csproj`
3. The console will display:
   - Button press/release events
   - Encoder rotation events
   - Encoder press/release events

## References

- [Rust mirajazz implementation](https://github.com/4ndv/mirajazz)
- [HidLibrary Documentation](https://github.com/mikeobrien/HidLibrary)

