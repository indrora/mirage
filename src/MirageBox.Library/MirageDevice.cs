namespace MirageBox;

using HidSharp;
using MirageBox.Library;
using SkiaSharp;

/// <summary>
/// Represents a connected Mirabox/Ajazz/Somfon HID device.
/// </summary>
public class MirageDevice : IMirageDevice
{
    private readonly HidDevice _hidDevice;
    private HidStream? _stream;
    private readonly string _serialNumber;
    private readonly DeviceProfile _profile;
    private DeviceProtocolVariant ProtocolVariant => _profile.ProtocolVariant;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool[] _lastButtonStates;
    private bool[] _lastEncoderStates;
    private bool _disposed;


    public DeviceProfile Profile => _profile;

    /// <summary>
    /// Gets the vendor ID of the device.
    /// </summary>
    public int VendorId => _hidDevice.VendorID;

    /// <summary>
    /// Gets the product ID of the device.
    /// </summary>
    public int ProductId => _hidDevice.ProductID;

    /// <summary>
    /// Gets the serial number of the device.
    /// </summary>
    public string SerialNumber => _serialNumber;

    /// <summary>
    /// Gets the number of buttons on the device.
    /// </summary>
    public int ButtonCount => _profile.ButtonCount;

    /// <summary>
    /// Gets the native image width (in pixels) expected by this device's display panels.
    /// </summary>
    public int ImageWidth => _profile.ImageWidth;

    /// <summary>
    /// Gets the native image height (in pixels) expected by this device's display panels.
    /// </summary>
    public int ImageHeight => _profile.ImageHeight;

    /// <summary>
    /// Gets the number of encoders/knobs on the device.
    /// </summary>
    public int EncoderCount => _profile.EncoderCount;

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
    public MirageDevice(
        HidDevice hidDevice,
        string serialNumber,
        DeviceProfile profile)
    {
        _hidDevice = hidDevice ?? throw new ArgumentNullException(nameof(hidDevice));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _serialNumber = serialNumber ?? "Unknown";
        _lastButtonStates = new bool[_profile.ButtonCount];
        _lastEncoderStates = new bool[_profile.EncoderCount];
    }

    /// <summary>
    /// Initializes the device for communication.
    /// </summary>
    public async Task InitializeAsync()
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            if (_stream == null)
                _stream = _hidDevice.Open();

            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.WakeScreen));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.KeepAlive));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.ClearButtonImage, 0x00, 0x00, 0xFF, 0x00));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.Flush));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.SetBrightness, 0, 0, 0x32));
        });
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
            WriteReport(HidReportParser.CreateBrightnessCommand(percent));
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
            WriteReport(HidReportParser.CreateLedBrightnessCommand(percent));
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
            WriteReport(HidReportParser.CreateLedColorCommand(colors));
        });
    }

    /// <summary>
    /// Clears the display of a button.
    /// </summary>
    public async Task ClearButtonDisplayAsync(int buttonIndex)
    {
        ThrowIfDisposed();

        if (buttonIndex < 0 || buttonIndex >= ButtonCount && buttonIndex != 0xFF)
            throw new ArgumentOutOfRangeException(nameof(buttonIndex));

        await Task.Run(() =>
        {
            WriteReport(HidReportParser.CreateClearButtonCommand(buttonIndex));
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
            WriteReport(HidReportParser.CreateClearAllCommand());
        });
    }

    /// <summary>
    /// Sets the image for a display panel button and flushes.
    /// </summary>
    public async Task SetButtonImageAsync(int buttonIndex, byte[] imageData)
    {
        await SetButtonImageNoFlushAsync(buttonIndex, imageData);
        await FlushAsync();
    }

    /// <summary>
    /// Sets the image for a display panel button without flushing.
    /// </summary>
    public async Task SetButtonImageNoFlushAsync(int buttonIndex, byte[] imageData)
    {
        ThrowIfDisposed();

        if (buttonIndex < 0 || buttonIndex >= ButtonCount)
            throw new ArgumentOutOfRangeException(nameof(buttonIndex));

        if (imageData is null || imageData.Length == 0)
            throw new ArgumentException("Image data must not be null or empty.", nameof(imageData));

        var payload = TransformImageForDevice(imageData);

        await Task.Run(() =>
        {
            WriteReport(HidReportParser.CreateSetImageCommand(buttonIndex, payload.Length));
            WriteImagePayload(payload);
        });
    }

    /// <summary>
    /// Flushes pending image changes to the display.
    /// </summary>
    public async Task FlushAsync()
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            WriteReport(HidReportParser.CreateStopCommand());
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
            WriteReport(HidReportParser.CreateKeepAliveCommand());
        });
    }

    private void WriteReport(byte[] cmd)
    {
        EnsureStream();
        int reportLength = GetOutputReportByteLength();
        var report = new byte[reportLength];
        int copyLen = Math.Min(cmd.Length, reportLength);
        Buffer.BlockCopy(cmd, 0, report, 0, copyLen);
        _stream!.Write(report);
    }

    private void WriteImagePayload(byte[] imageData)
    {
        EnsureStream();
        int reportLength = GetOutputReportByteLength();
        int payloadLength = reportLength - 1; // byte 0 is report ID

        int offset = 0;
        while (offset < imageData.Length)
        {
            int chunkLength = Math.Min(payloadLength, imageData.Length - offset);
            var report = new byte[reportLength];
            report[0] = 0x00;
            Buffer.BlockCopy(imageData, offset, report, 1, chunkLength);
            _stream!.Write(report);
            offset += chunkLength;
        }
    }

    private int GetOutputReportByteLength()
    {
        try
        {
            int len = _hidDevice.GetMaxOutputReportLength();
            return len > 1 ? len : 65;
        }
        catch
        {
            return 65;
        }
    }

    private void EnsureStream()
    {
        if (_stream == null)
            _stream = _hidDevice.Open();
    }

    private byte[] TransformImageForDevice(byte[] imageData)
    {
        if (_profile.RotationDegrees == 0)
            return imageData;

        try
        {
            return RotateJpeg(imageData, _profile.RotationDegrees);
        }
        catch
        {
            return imageData;
        }
    }

    private static byte[] RotateJpeg(byte[] imageData, int degrees)
    {
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
                    90  => (src.Height - 1 - sy, sx),
                    180 => (src.Width  - 1 - sx, src.Height - 1 - sy),
                    270 => (sy, src.Width - 1 - sx),
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
            EnsureStream();
            int inputLength = _hidDevice.GetMaxInputReportLength();
            var buffer = new byte[inputLength];

            while (!cancellationToken.IsCancellationRequested)
            {
                int count;
                try
                {
                    count = await Task.Run(() => _stream!.Read(buffer, 0, buffer.Length), cancellationToken);
                }
                catch (TimeoutException)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                if (count <= 0)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                var reportData = new byte[count];
                Buffer.BlockCopy(buffer, 0, reportData, 0, count);

                var input = HidReportParser.ParseReport(reportData, _profile.ButtonCount, _profile.EncoderCount, _profile.ProtocolVariant);
                if (input is null)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                if (input.UseStateDiff)
                {
                    for (int i = 0; i < _profile.ButtonCount && i < input.ButtonStates.Length; i++)
                    {
                        if (input.ButtonStates[i] != _lastButtonStates[i])
                        {
                            _lastButtonStates[i] = input.ButtonStates[i];
                            OnButtonChanged(new ButtonEventArgs(i, input.ButtonStates[i], (bool[])_lastButtonStates.Clone()));
                        }
                    }

                    for (int i = 0; i < _profile.EncoderCount && i < input.EncoderPressStates.Length; i++)
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

                for (int i = 0; i < _profile.EncoderCount && i < input.EncoderRotations.Length; i++)
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
            _stream?.Close();
            _stream?.Dispose();
            _stream = null;
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
