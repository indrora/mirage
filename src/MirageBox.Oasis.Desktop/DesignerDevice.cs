namespace MirageBox.Oasis.Desktop;
using HidSharp;

/// <summary>
/// A no-op <see cref="IMirageDevice"/> for the Avalonia XAML designer.
/// Mimics an Otagle DreamSteck: 3 display buttons, 3 tactile, 1 encoder, 72×72 images.
/// </summary>
internal sealed class DesignerDevice : IMirageDevice
{
    
    
    public int VendorId => 0xDEAD;
    public int ProductId => 0xBEEF;
    public string SerialNumber => "DESIGNER-0000";

    public int ImageButtonCount => 3;
    public int TactileButtonCount => 3;
    public int EncoderCount => 1;
    public int ImageWidth => 72;
    public int ImageHeight => 72;

    public DeviceProfile Profile
    {
        get
        {
            return new DeviceProfile(
                "Designer", 0xDEAD, 0xBEEF,
                new byte[] { 0x1, 0x2, 0x3 },
                new byte[] { 4, 5, 6 },
                1,
                0,
                72,
                72,
                0,
                320,
                240,
                0,
                DeviceProtocolVariant.AckPrefix);
        }
    }

    public event EventHandler<ImageButtonEventArgs>? ImageButtonChanged;
    public event EventHandler<TactileButtonEventArgs>? TactileButtonChanged;
    public event EventHandler<EncoderButtonEventArgs>? EncoderButtonChanged;
    public event EventHandler<EncoderEventArgs>? EncoderRotated;
    public event EventHandler? Disconnected;
    public Task InitializeAsync() => Task.CompletedTask;
    public Task StartListeningAsync() => Task.CompletedTask;
    public Task StopListeningAsync() => Task.CompletedTask;
    public Task SetBrightnessAsync(byte percent) => Task.CompletedTask;
    public Task SetLedBrightnessAsync(byte percent) => Task.CompletedTask;
    public Task SetLedColorsAsync(byte[][] colors) => Task.CompletedTask;
    public Task ClearButtonDisplayAsync(int buttonIndex) => Task.CompletedTask;
    public Task ClearAllDisplaysAsync() => Task.CompletedTask;
    public Task SetButtonImageAsync(int buttonIndex, byte[] imageData) => Task.CompletedTask;
    public Task SetButtonImageNoFlushAsync(int buttonIndex, byte[] imageData) => Task.CompletedTask;
    public Task FlushAsync() => Task.CompletedTask;
    public Task KeepAliveAsync() => Task.CompletedTask;
    public void Dispose() { }
}
