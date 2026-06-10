namespace MirageBox;

/// <summary>
/// Represents a connected Mirabox/Ajazz/Somfon HID device (or simulator).
/// </summary>
public interface IMirageDevice : IDisposable
{
    int VendorId { get; }
    int ProductId { get; }
    string SerialNumber { get; }

    /// <summary>Number of buttons that have display screens.</summary>
    int ImageButtonCount { get; }

    /// <summary>Number of physical buttons without display screens.</summary>
    int TactileButtonCount { get; }

    int EncoderCount { get; }
    
    /// <summary>
    /// A device profile 
    /// </summary>
    DeviceProfile Profile { get; }

    /// <summary>Native image width expected by <see cref="SetButtonImageAsync"/>.</summary>
    int ImageWidth { get; }

    /// <summary>Native image height expected by <see cref="SetButtonImageAsync"/>.</summary>
    int ImageHeight { get; }

    /// <summary>Raised when a button with a display screen is pressed or released.</summary>
    event EventHandler<ImageButtonEventArgs>? ImageButtonChanged;

    /// <summary>Raised when a screen-less physical button is pressed or released.</summary>
    event EventHandler<TactileButtonEventArgs>? TactileButtonChanged;

    /// <summary>Raised when an encoder/knob is pressed or released.</summary>
    event EventHandler<EncoderButtonEventArgs>? EncoderButtonChanged;

    /// <summary>Raised when an encoder/knob is rotated.</summary>
    event EventHandler<EncoderEventArgs>? EncoderRotated;

    /// <summary>Raised when the device is disconnected or otherwise becomes unavailable.</summary>
    event EventHandler? Disconnected;

    Task InitializeAsync();
    Task StartListeningAsync();
    Task StopListeningAsync();

    Task SetBrightnessAsync(byte percent);
    Task SetLedBrightnessAsync(byte percent);
    Task SetLedColorsAsync(byte[][] colors);

    /// <summary>
    /// Clears the display of an image button.
    /// </summary>
    /// <param name="buttonIndex">Zero-based index within image buttons.</param>
    Task ClearButtonDisplayAsync(int buttonIndex);

    Task ClearAllDisplaysAsync();

    /// <summary>
    /// Sets the image for an image-button display panel and flushes.
    /// </summary>
    /// <param name="buttonIndex">Zero-based index within image buttons.</param>
    /// <param name="imageData">JPEG bytes at <see cref="ImageWidth"/>×<see cref="ImageHeight"/>.</param>
    Task SetButtonImageAsync(int buttonIndex, byte[] imageData);

    /// <summary>Sets the image without flushing; call <see cref="FlushAsync"/> when done.</summary>
    Task SetButtonImageNoFlushAsync(int buttonIndex, byte[] imageData);

    Task FlushAsync();
    Task KeepAliveAsync();
}
