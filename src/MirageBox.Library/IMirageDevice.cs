namespace MirageBox;

/// <summary>
/// Represents a connected Mirabox/Ajazz/Somfon HID device.
/// </summary>
public interface IMirageDevice : IDisposable
{
    /// <summary>
    /// Gets the vendor ID of the device.
    /// </summary>
    int VendorId { get; }

    /// <summary>
    /// Gets the product ID of the device.
    /// </summary>
    int ProductId { get; }

    /// <summary>
    /// Gets the serial number of the device.
    /// </summary>
    string SerialNumber { get; }

    /// <summary>
    /// Gets the number of buttons on the device.
    /// </summary>
    int ButtonCount { get; }

    /// <summary>
    /// Gets the number of encoders/knobs on the device.
    /// </summary>
    int EncoderCount { get; }

    /// <summary>
    /// Gets the native image width (in pixels) for this device's display panels.
    /// </summary>
    int ImageWidth { get; }

    /// <summary>
    /// Gets the native image height (in pixels) for this device's display panels.
    /// </summary>
    int ImageHeight { get; }

    /// <summary>
    /// Raised when a button is pressed or released.
    /// </summary>
    event EventHandler<ButtonEventArgs>? ButtonChanged;

    /// <summary>
    /// Raised when an encoder/knob is pressed.
    /// </summary>
    event EventHandler<EncoderEventArgs>? EncoderPressed;

    /// <summary>
    /// Raised when an encoder/knob is released.
    /// </summary>
    event EventHandler<EncoderEventArgs>? EncoderReleased;

    /// <summary>
    /// Raised when an encoder/knob is rotated.
    /// </summary>
    event EventHandler<EncoderEventArgs>? EncoderRotated;

    /// <summary>
    /// Initializes the device for communication.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Starts listening for input from the device.
    /// </summary>
    Task StartListeningAsync();

    /// <summary>
    /// Stops listening for input from the device.
    /// </summary>
    Task StopListeningAsync();

    /// <summary>
    /// Sets the brightness of the device display.
    /// </summary>
    /// <param name="percent">Brightness level from 0 to 100.</param>
    Task SetBrightnessAsync(byte percent);

    /// <summary>
    /// Sets the brightness of the encoder LED rings.
    /// </summary>
    /// <param name="percent">LED brightness level from 0 to 100.</param>
    Task SetLedBrightnessAsync(byte percent);

    /// <summary>
    /// Sets the color of each encoder LED individually.
    /// </summary>
    /// <param name="colors">Array of RGB colors, one per encoder LED.</param>
    Task SetLedColorsAsync(byte[][] colors);

    /// <summary>
    /// Clears the display of a button.
    /// </summary>
    /// <param name="buttonIndex">The button index to clear.</param>
    Task ClearButtonDisplayAsync(int buttonIndex);

    /// <summary>
    /// Clears all button displays on the device.
    /// </summary>
    Task ClearAllDisplaysAsync();

    /// <summary>
    /// Sets the image for a display panel button and flushes.
    /// </summary>
    /// <param name="buttonIndex">Zero-based display panel index.</param>
    /// <param name="imageData">JPEG image bytes.</param>
    Task SetButtonImageAsync(int buttonIndex, byte[] imageData);

    /// <summary>
    /// Sets the image for a display panel button without flushing.
    /// </summary>
    Task SetButtonImageNoFlushAsync(int buttonIndex, byte[] imageData);

    /// <summary>
    /// Flushes pending image changes to the display.
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// Keeps the device alive with a periodic heartbeat.
    /// </summary>
    Task KeepAliveAsync();
}
