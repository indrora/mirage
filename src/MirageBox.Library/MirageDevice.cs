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
    private bool[] _imageButtonStates;
    private bool[] _tactileButtonStates;
    private bool[] _lastEncoderStates;
    private bool _disposed;

    public DeviceProfile Profile => _profile;

    public int VendorId => _hidDevice.VendorID;
    public int ProductId => _hidDevice.ProductID;
    public string SerialNumber => _serialNumber;
    public int ImageButtonCount  => _profile.ImageButtonCount;
    public int TactileButtonCount => _profile.TactileButtonCount;
    public int EncoderCount => _profile.EncoderCount;
    public int ImageWidth  => _profile.ImageWidth;
    public int ImageHeight => _profile.ImageHeight;

    public event EventHandler<ImageButtonEventArgs>?  ImageButtonChanged;
    public event EventHandler<TactileButtonEventArgs>? TactileButtonChanged;
    public event EventHandler<EncoderButtonEventArgs>? EncoderButtonChanged;
    public event EventHandler<EncoderEventArgs>?      EncoderRotated;
    public event EventHandler?                        Disconnected;

    public MirageDevice(HidDevice hidDevice, string serialNumber, DeviceProfile profile)
    {
        _hidDevice = hidDevice ?? throw new ArgumentNullException(nameof(hidDevice));
        _profile   = profile   ?? throw new ArgumentNullException(nameof(profile));
        _serialNumber = serialNumber ?? "Unknown";
        _imageButtonStates  = new bool[_profile.ImageButtonCount];
        _tactileButtonStates = new bool[_profile.TactileButtonCount];
        _lastEncoderStates  = new bool[_profile.EncoderCount];
    }

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await Task.Run(() =>
        {
            if (_stream == null) _stream = _hidDevice.Open();
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.WakeScreen));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.KeepAlive));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.ClearButtonImage, 0x00, 0x00, 0xFF, 0x00));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.Flush));
            WriteReport(DeviceCommands.formatCommand(DeviceCommands.Command.SetBrightness, 0, 0, 0x32));
        });
    }

    public Task StartListeningAsync()
    {
        ThrowIfDisposed();
        if (_listenerTask is not null) return Task.CompletedTask;
        _listenerCts = new CancellationTokenSource();
        _listenerTask = ListenerLoop(_listenerCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopListeningAsync()
    {
        ThrowIfDisposed();
        if (_listenerCts is null) return;
        _listenerCts.Cancel();
        if (_listenerTask is not null) await _listenerTask;
        _listenerCts.Dispose();
        _listenerCts = null;
        _listenerTask = null;
    }

    public async Task SetBrightnessAsync(byte percent)
    {
        ThrowIfDisposed();
        await Task.Run(() => WriteReport(HidReportParser.CreateBrightnessCommand(percent)));
    }

    public async Task SetLedBrightnessAsync(byte percent)
    {
        ThrowIfDisposed();
        await Task.Run(() => WriteReport(HidReportParser.CreateLedBrightnessCommand(percent)));
    }

    public async Task SetLedColorsAsync(byte[][] colors)
    {
        ThrowIfDisposed();
        await Task.Run(() => WriteReport(HidReportParser.CreateLedColorCommand(colors)));
    }

    public async Task ClearButtonDisplayAsync(int buttonIndex)
    {
        ThrowIfDisposed();
        if (buttonIndex < 0 || (buttonIndex >= ImageButtonCount && buttonIndex != 0xFF))
            throw new ArgumentOutOfRangeException(nameof(buttonIndex));
        await Task.Run(() => WriteReport(HidReportParser.CreateClearButtonCommand(buttonIndex)));
    }

    public async Task ClearAllDisplaysAsync()
    {
        ThrowIfDisposed();
        await Task.Run(() => WriteReport(HidReportParser.CreateClearAllCommand()));
    }

    public async Task SetButtonImageAsync(int buttonIndex, byte[] imageData)
    {
        await SetButtonImageNoFlushAsync(buttonIndex, imageData);
        await FlushAsync();
    }

    public async Task SetButtonImageNoFlushAsync(int buttonIndex, byte[] imageData)
    {
        ThrowIfDisposed();
        if (buttonIndex < 0 || buttonIndex >= ImageButtonCount)
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

    public async Task FlushAsync()
    {
        ThrowIfDisposed();
        await Task.Run(() => WriteReport(HidReportParser.CreateStopCommand()));
    }

    public async Task KeepAliveAsync()
    {
        ThrowIfDisposed();
        await Task.Run(() => WriteReport(HidReportParser.CreateKeepAliveCommand()));
    }

    private void WriteReport(byte[] cmd)
    {
        EnsureStream();
        int reportLength = GetOutputReportByteLength();
        var report = new byte[reportLength];
        Buffer.BlockCopy(cmd, 0, report, 0, Math.Min(cmd.Length, reportLength));
        _stream!.Write(report);
    }

    private void WriteImagePayload(byte[] imageData)
    {
        EnsureStream();
        int reportLength = GetOutputReportByteLength();
        int payloadLength = reportLength - 1;
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
        try { int len = _hidDevice.GetMaxOutputReportLength(); return len > 1 ? len : 65; }
        catch { return 65; }
    }

    private void EnsureStream()
    {
        if (_stream == null) _stream = _hidDevice.Open();
    }

    private byte[] TransformImageForDevice(byte[] imageData)
    {
        if (_profile.RotationDegrees == 0) return imageData;
        try { return RotateJpeg(imageData, _profile.RotationDegrees); }
        catch { return imageData; }
    }

    private static byte[] RotateJpeg(byte[] imageData, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0) return imageData;

        using var src = SKBitmap.Decode(imageData);
        if (src is null) return imageData;

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

                if (count <= 0) { await Task.Delay(10, cancellationToken); continue; }

                var reportData = new byte[count];
                Buffer.BlockCopy(buffer, 0, reportData, 0, count);

                var input = HidReportParser.ParseReport(
                    reportData,
                    _profile.ImageButtonCodes,
                    _profile.TactileButtonCodes,
                    _profile.EncoderCount,
                    _profile.ProtocolVariant);

                if (input is null) { await Task.Delay(10, cancellationToken); continue; }

                if (input.UseStateDiff)
                {
                    // Snapshot path (LegacyBitfield) — all buttons are image buttons.
                    for (int i = 0; i < _profile.ImageButtonCount && i < input.ImageButtonStates.Length; i++)
                    {
                        if (input.ImageButtonStates[i] != _imageButtonStates[i])
                        {
                            _imageButtonStates[i] = input.ImageButtonStates[i];
                            OnImageButtonChanged(new ImageButtonEventArgs(i, _imageButtonStates[i], (bool[])_imageButtonStates.Clone()));
                        }
                    }

                    for (int i = 0; i < _profile.EncoderCount && i < input.EncoderPressStates.Length; i++)
                    {
                        if (input.EncoderPressStates[i] != _lastEncoderStates[i])
                        {
                            _lastEncoderStates[i] = input.EncoderPressStates[i];
                            OnEncoderButtonChanged(new EncoderButtonEventArgs(i, _lastEncoderStates[i], (bool[])_lastEncoderStates.Clone()));
                        }
                    }
                }
                else
                {
                    // Delta path (AckPrefix)
                    if (input.ChangedButtonIndex.HasValue &&
                        input.ChangedButtonState.HasValue &&
                        input.ChangedButtonCategory.HasValue)
                    {
                        int idx     = input.ChangedButtonIndex.Value;
                        bool pressed = input.ChangedButtonState.Value;

                        if (input.ChangedButtonCategory == ButtonCategory.Image)
                        {
                            _imageButtonStates[idx] = pressed;
                            OnImageButtonChanged(new ImageButtonEventArgs(idx, pressed, (bool[])_imageButtonStates.Clone()));
                        }
                        else
                        {
                            _tactileButtonStates[idx] = pressed;
                            OnTactileButtonChanged(new TactileButtonEventArgs(idx, pressed, (bool[])_tactileButtonStates.Clone()));
                        }
                    }

                    if (input.ChangedEncoderPressIndex.HasValue && input.ChangedEncoderPressState.HasValue)
                    {
                        int idx     = input.ChangedEncoderPressIndex.Value;
                        bool pressed = input.ChangedEncoderPressState.Value;
                        _lastEncoderStates[idx] = pressed;
                        OnEncoderButtonChanged(new EncoderButtonEventArgs(idx, pressed, (bool[])_lastEncoderStates.Clone()));
                    }
                }

                for (int i = 0; i < _profile.EncoderCount && i < input.EncoderRotations.Length; i++)
                {
                    if (input.EncoderRotations[i] != 0)
                        OnEncoderRotated(new EncoderEventArgs(i, input.EncoderRotations[i]));
                }

                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Device listener error: {ex.Message}");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    protected virtual void OnImageButtonChanged(ImageButtonEventArgs e)   => ImageButtonChanged?.Invoke(this, e);
    protected virtual void OnTactileButtonChanged(TactileButtonEventArgs e) => TactileButtonChanged?.Invoke(this, e);
    protected virtual void OnEncoderButtonChanged(EncoderButtonEventArgs e) => EncoderButtonChanged?.Invoke(this, e);
    protected virtual void OnEncoderRotated(EncoderEventArgs e)           => EncoderRotated?.Invoke(this, e);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            try { _listenerCts?.Cancel(); _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _listenerCts?.Dispose();
            _stream?.Close();
            _stream?.Dispose();
            _stream = null;
        }
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
    }
}
