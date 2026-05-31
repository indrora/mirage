namespace MirageBox;

using HidLibrary;
using SkiaSharp;

/// <summary>
/// Represents a connected Mirabox/Ajazz/Somfon HID device.
/// </summary>
public class MirageDevice : IMirageDevice
{
    private readonly IHidDevice _hidDevice;
    private readonly string _serialNumber;
    private readonly int _buttonCount;
    private readonly int _encoderCount;
    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private readonly int _rotationDegrees;
    private readonly DeviceProtocolVariant _protocolVariant;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool[] _lastButtonStates;
    private bool[] _lastEncoderStates;
    private bool _disposed;

    /// <summary>
    /// Gets the vendor ID of the device.
    /// </summary>
    public int VendorId => _hidDevice.Attributes.VendorId;

    /// <summary>
    /// Gets the product ID of the device.
    /// </summary>
    public int ProductId => _hidDevice.Attributes.ProductId;

    /// <summary>
    /// Gets the serial number of the device.
    /// </summary>
    public string SerialNumber => _serialNumber;

    /// <summary>
    /// Gets the number of buttons on the device.
    /// </summary>
    public int ButtonCount => _buttonCount;

    /// <summary>
    /// Gets the native image width (in pixels) expected by this device's display panels.
    /// </summary>
    public int ImageWidth => _imageWidth;

    /// <summary>
    /// Gets the native image height (in pixels) expected by this device's display panels.
    /// </summary>
    public int ImageHeight => _imageHeight;

    /// <summary>
    /// Gets the number of encoders/knobs on the device.
    /// </summary>
    public int EncoderCount => _encoderCount;

    /// <summary>
    /// Raised when a button is pressed or released.
    /// </summary>
    public event EventHandler<ButtonEventArgs>? ButtonChanged;

    /// <summary>
    /// Raised when an encoder/knob is pressed.
    /// </summary>
    public event EventHandler<EncoderEventArgs>? EncoderPressed;

    /// <summary>
    /// Raised when an encoder/knob is released.
    /// </summary>
    public event EventHandler<EncoderEventArgs>? EncoderReleased;

    /// <summary>
    /// Raised when an encoder/knob is rotated.
    /// </summary>
    public event EventHandler<EncoderEventArgs>? EncoderRotated;

    /// <summary>
    /// Initializes a new instance of the <see cref="MirageDevice"/> class.
    /// </summary>
    /// <param name="hidDevice">The underlying HID device.</param>
    /// <param name="serialNumber">The serial number of the device.</param>
    /// <param name="buttonCount">The number of buttons on the device.</param>
    /// <param name="encoderCount">The number of encoders on the device.</param>
    /// <param name="imageWidth">Native display panel image width in pixels.</param>
    /// <param name="imageHeight">Native display panel image height in pixels.</param>
    /// <param name="rotationDegrees">Clockwise degrees to rotate images before upload (0, 90, 180, or -90).</param>
    /// <param name="protocolVariant">Input report protocol variant for this device.</param>
    public MirageDevice(
        IHidDevice hidDevice,
        string serialNumber,
        int buttonCount,
        int encoderCount,
        int imageWidth,
        int imageHeight,
        int rotationDegrees,
        DeviceProtocolVariant protocolVariant)
    {
        _hidDevice = hidDevice ?? throw new ArgumentNullException(nameof(hidDevice));
        _buttonCount = buttonCount;
        _encoderCount = encoderCount;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        _rotationDegrees = rotationDegrees;
        _protocolVariant = protocolVariant;
        _serialNumber = serialNumber ?? "Unknown";
        _lastButtonStates = new bool[buttonCount];
        _lastEncoderStates = new bool[encoderCount];
    }

    /// <summary>
    /// Initializes the device for communication.
    /// </summary>
    public async Task InitializeAsync()
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            if (!_hidDevice.IsOpen)
                _hidDevice.OpenDevice();

            // Send initialization commands
            var initCmd = HidReportParser.CreateInitCommand();
            _hidDevice.Write(initCmd);
        });

        await Task.Delay(100); // Brief delay for device to process
    }

    /// <summary>
    /// Starts listening for input from the device.
    /// </summary>
    public Task StartListeningAsync()
    {
        ThrowIfDisposed();

        if (_listenerTask is not null)
            return Task.CompletedTask;

        _listenerCts = new CancellationTokenSource();
        _listenerTask = ListenerLoop(_listenerCts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops listening for input from the device.
    /// </summary>
    public async Task StopListeningAsync()
    {
        ThrowIfDisposed();

        if (_listenerCts is null)
            return;

        _listenerCts.Cancel();
        if (_listenerTask is not null)
            await _listenerTask;

        _listenerCts.Dispose();
        _listenerCts = null;
        _listenerTask = null;
    }

    /// <summary>
    /// Sets the brightness of the device display.
    /// </summary>
    public async Task SetBrightnessAsync(byte percent)
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            var cmd = HidReportParser.CreateBrightnessCommand(percent);
            _hidDevice.Write(cmd);
        });
    }

    /// <summary>
    /// Sets the brightness of the encoder LED rings.
    /// </summary>
    public async Task SetLedBrightnessAsync(byte percent)
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            var cmd = HidReportParser.CreateLedBrightnessCommand(percent);
            _hidDevice.Write(cmd);
        });
    }

    /// <summary>
    /// Sets the color of each encoder LED individually.
    /// </summary>
    public async Task SetLedColorsAsync(byte[][] colors)
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            var cmd = HidReportParser.CreateLedColorCommand(colors);
            _hidDevice.Write(cmd);
        });
    }

    /// <summary>
    /// Clears the display of a button.
    /// </summary>
    public async Task ClearButtonDisplayAsync(int buttonIndex)
    {
        ThrowIfDisposed();

        if (buttonIndex < 0 || buttonIndex >= _buttonCount && buttonIndex != 0xFF)
            throw new ArgumentOutOfRangeException(nameof(buttonIndex));

        await Task.Run(() =>
        {
            var cmd = HidReportParser.CreateClearButtonCommand(buttonIndex);
            _hidDevice.Write(cmd);
        });
    }

    /// <summary>
    /// Clears all button displays on the device.
    /// </summary>
    public async Task ClearAllDisplaysAsync()
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            var cmd = HidReportParser.CreateClearAllCommand();
            _hidDevice.Write(cmd);
        });
    }

    /// <summary>
    /// Sets the image for a display panel button.
    /// </summary>
    public async Task SetButtonImageAsync(int buttonIndex, byte[] imageData)
    {
        ThrowIfDisposed();

        if (buttonIndex < 0 || buttonIndex >= _buttonCount)
            throw new ArgumentOutOfRangeException(nameof(buttonIndex));

        if (imageData is null || imageData.Length == 0)
            throw new ArgumentException("Image data must not be null or empty.", nameof(imageData));

        var payload = TransformImageForDevice(imageData);

        await Task.Run(() =>
        {
            var beginCmd = HidReportParser.CreateSetImageCommand(buttonIndex, payload.Length);
            _hidDevice.Write(beginCmd);

            WriteImagePayload(payload);

            var stopCmd = HidReportParser.CreateStopCommand();
            _hidDevice.Write(stopCmd);
        });
    }

    /// <summary>
    /// Keeps the device alive with a periodic heartbeat.
    /// </summary>
    public async Task KeepAliveAsync()
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            var cmd = HidReportParser.CreateKeepAliveCommand();
            _hidDevice.Write(cmd);
        });
    }

    private void WriteImagePayload(byte[] imageData)
    {
        int reportLength = GetOutputReportByteLength();
        int payloadLength = reportLength - 1; // byte 0 is report ID

        int offset = 0;
        while (offset < imageData.Length)
        {
            int chunkLength = Math.Min(payloadLength, imageData.Length - offset);
            var report = new byte[reportLength];
            report[0] = 0x00;
            Buffer.BlockCopy(imageData, offset, report, 1, chunkLength);
            _hidDevice.Write(report);
            offset += chunkLength;
        }
    }

    private int GetOutputReportByteLength()
    {
        int reportLength = _hidDevice.Capabilities.OutputReportByteLength;
        if (reportLength <= 1)
            return 65;

        return reportLength;
    }

    private byte[] TransformImageForDevice(byte[] imageData)
    {
        if (_rotationDegrees == 0)
            return imageData;

        try
        {
            return RotateJpeg(imageData, _rotationDegrees);
        }
        catch
        {
            // Keep original payload if decode/encode fails.
            return imageData;
        }
    }

    private static byte[] RotateJpeg(byte[] imageData, int degrees)
    {
        // Normalize to 0/90/180/270.
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0)
            return imageData;

        using var src = SKBitmap.Decode(imageData);
        if (src is null)
            return imageData;

        bool transposed = degrees == 90 || degrees == 270;
        using var dst = new SKBitmap(
            transposed ? src.Height : src.Width,
            transposed ? src.Width  : src.Height,
            src.ColorType, src.AlphaType);

        for (int sy = 0; sy < src.Height; sy++)
        {
            for (int sx = 0; sx < src.Width; sx++)
            {
                var pixel = src.GetPixel(sx, sy);
                (int dx, int dy) = degrees switch
                {
                    90  => (src.Height - 1 - sy, sx),            // 90° CW
                    180 => (src.Width  - 1 - sx, src.Height - 1 - sy), // 180°
                    270 => (sy, src.Width - 1 - sx),              // 90° CCW
                    _   => (sx, sy)
                };
                dst.SetPixel(dx, dy, pixel);
            }
        }

        using var image = SKImage.FromBitmap(dst);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return encoded.ToArray();
    }

    private async Task ListenerLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var report = await Task.Run(() => _hidDevice.Read(), cancellationToken);

                if (report == null || report.Data == null)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                var input = HidReportParser.ParseReport(report.Data, _buttonCount, _encoderCount, _protocolVariant);
                if (input is null)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                if (input.UseStateDiff)
                {
                    // Snapshot-based protocol: compare full state arrays.
                    for (int i = 0; i < _buttonCount && i < input.ButtonStates.Length; i++)
                    {
                        if (input.ButtonStates[i] != _lastButtonStates[i])
                        {
                            _lastButtonStates[i] = input.ButtonStates[i];
                            OnButtonChanged(new ButtonEventArgs(i, input.ButtonStates[i], (bool[])_lastButtonStates.Clone()));
                        }
                    }

                    for (int i = 0; i < _encoderCount && i < input.EncoderPressStates.Length; i++)
                    {
                        if (input.EncoderPressStates[i] != _lastEncoderStates[i])
                        {
                            _lastEncoderStates[i] = input.EncoderPressStates[i];
                            if (input.EncoderPressStates[i])
                                OnEncoderPressed(new EncoderEventArgs(i, true, (bool[])_lastEncoderStates.Clone()));
                            else
                                OnEncoderReleased(new EncoderEventArgs(i, false, (bool[])_lastEncoderStates.Clone()));
                        }
                    }
                }
                else
                {
                    // Event-delta protocol: apply only the changed control from this packet.
                    if (input.ChangedButtonIndex.HasValue && input.ChangedButtonState.HasValue)
                    {
                        int index = input.ChangedButtonIndex.Value;
                        bool pressed = input.ChangedButtonState.Value;
                        _lastButtonStates[index] = pressed;
                        OnButtonChanged(new ButtonEventArgs(index, pressed, (bool[])_lastButtonStates.Clone()));
                    }

                    if (input.ChangedEncoderPressIndex.HasValue && input.ChangedEncoderPressState.HasValue)
                    {
                        int index = input.ChangedEncoderPressIndex.Value;
                        bool pressed = input.ChangedEncoderPressState.Value;
                        _lastEncoderStates[index] = pressed;

                        if (pressed)
                            OnEncoderPressed(new EncoderEventArgs(index, true, (bool[])_lastEncoderStates.Clone()));
                        else
                            OnEncoderReleased(new EncoderEventArgs(index, false, (bool[])_lastEncoderStates.Clone()));
                    }
                }

                for (int i = 0; i < _encoderCount && i < input.EncoderRotations.Length; i++)
                {
                    if (input.EncoderRotations[i] != 0)
                    {
                        OnEncoderRotated(new EncoderEventArgs(i, input.EncoderRotations[i]));
                    }
                }

                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in listener loop: {ex.Message}");
        }
    }

    protected virtual void OnButtonChanged(ButtonEventArgs e)
    {
        ButtonChanged?.Invoke(this, e);
    }

    protected virtual void OnEncoderPressed(EncoderEventArgs e)
    {
        EncoderPressed?.Invoke(this, e);
    }

    protected virtual void OnEncoderReleased(EncoderEventArgs e)
    {
        EncoderReleased?.Invoke(this, e);
    }

    protected virtual void OnEncoderRotated(EncoderEventArgs e)
    {
        EncoderRotated?.Invoke(this, e);
    }

    /// <summary>
    /// Disposes the device and closes the HID connection.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            try
            {
                _listenerCts?.Cancel();
                _listenerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* Ignore */ }

            _listenerCts?.Dispose();
            _hidDevice?.CloseDevice();
            _hidDevice?.Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
