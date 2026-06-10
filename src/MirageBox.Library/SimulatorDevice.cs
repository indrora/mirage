namespace MirageBox;

using Silk.NET.Maths;
using Silk.NET.SDL;
using SkiaSharp;
using SdlApi = Silk.NET.SDL.Sdl;
using SysThread = System.Threading.Thread;

/// <summary>
/// A software simulator that implements IMirageDevice using an SDL2 window.
/// Display panels are shown side-by-side; tactile buttons appear below as circles
/// triggered by keyboard keys 1…N. The mouse scroll wheel drives encoder 0.
/// </summary>
public sealed class SimulatorDevice : IMirageDevice
{
    private const int Bezel = 8;
    private const int CircleRadius = 24;
    private const int CircleAreaHeight = CircleRadius * 2 + 32;

    private readonly int _displayButtons;
    private readonly int _sideButtons;
    private readonly int _totalButtons;
    private readonly int _imgSize;
    private readonly int _cellSize;
    private readonly int _windowW;
    private readonly int _windowH;
    private readonly byte[] _placeholder;

    private readonly object _lock = new();
    private readonly byte[]?[] _images;
    private readonly bool[] _dirty;
    private readonly bool[] _buttonStates;
    private readonly bool[] _encoderStates = new bool[1];
    private int _lastPressedButton = -1;
    private volatile bool _disposed;
    private SysThread? _thread;

    /// <param name="displayButtons">Number of image buttons (display panels). Default 4.</param>
    /// <param name="sideButtons">Number of tactile buttons (keyboard-driven circles). Default 3.</param>
    /// <param name="imgSize">Button image size in pixels (square). Default 128.</param>
    public SimulatorDevice(int displayButtons = 4, int sideButtons = 3, int imgSize = 128)
    {
        _displayButtons = Math.Max(1, displayButtons);
        _sideButtons    = Math.Max(0, sideButtons);
        _totalButtons   = _displayButtons + _sideButtons;
        _imgSize        = Math.Max(16, imgSize);
        _cellSize       = _imgSize + 2 * Bezel;
        _windowW        = _displayButtons * _cellSize;
        _windowH        = _cellSize + (_sideButtons > 0 ? CircleAreaHeight : 0);

        _images       = new byte[_displayButtons][];
        _dirty        = new bool[_displayButtons];
        _buttonStates = new bool[_totalButtons];
        _placeholder  = BuildPlaceholder(_imgSize);
    }


    public DeviceProfile Profile
    {
        get
        {
            return new DeviceProfile(
                "Simulator", 0xDEAD, 0xBEEF,
                new byte[] { },
                new byte[] { },
                0,
                0,
                _imgSize,
                _imgSize,
                0,
                _imgSize*_displayButtons,
                _imgSize,
                0,
                DeviceProtocolVariant.AckPrefix);
        }
    }

    private static byte[] BuildPlaceholder(int size)
    {
        var buf = new byte[size * size * 4];
        for (int i = 0; i < buf.Length; i += 4)
        { buf[i] = 40; buf[i + 1] = 40; buf[i + 2] = 40; buf[i + 3] = 255; }
        return buf;
    }

    public int VendorId => 0;
    public int ProductId => 0;
    public string SerialNumber => "SIM-0";
    public int ImageButtonCount  => _displayButtons;
    public int TactileButtonCount => _sideButtons;
    public int EncoderCount => 1;
    public int ImageWidth => _imgSize;
    public int ImageHeight => _imgSize;

    public event EventHandler<ImageButtonEventArgs>?  ImageButtonChanged;
    public event EventHandler<TactileButtonEventArgs>? TactileButtonChanged;
    public event EventHandler<EncoderButtonEventArgs>? EncoderButtonChanged;
    public event EventHandler<EncoderEventArgs>?      EncoderRotated;
    public event EventHandler? Disconnected;

    public Task InitializeAsync()
    {
        _thread = new SysThread(RunSdlLoop) { IsBackground = true, Name = "SimulatorWindow" };
        _thread.Start();
        return Task.CompletedTask;
    }

    public Task StartListeningAsync() => Task.CompletedTask;
    public Task StopListeningAsync() => Task.CompletedTask;
    public Task SetBrightnessAsync(byte percent) => Task.CompletedTask;
    public Task SetLedBrightnessAsync(byte percent) => Task.CompletedTask;
    public Task SetLedColorsAsync(byte[][] colors) => Task.CompletedTask;
    public Task KeepAliveAsync() => Task.CompletedTask;
    public Task FlushAsync() => Task.CompletedTask;

    public Task ClearButtonDisplayAsync(int buttonIndex)
    {
        if (buttonIndex < 0 || buttonIndex >= _displayButtons) return Task.CompletedTask;
        lock (_lock) { _images[buttonIndex] = null; _dirty[buttonIndex] = true; }
        return Task.CompletedTask;
    }

    public Task ClearAllDisplaysAsync()
    {
        lock (_lock)
        {
            for (int i = 0; i < _displayButtons; i++) { _images[i] = null; _dirty[i] = true; }
        }
        return Task.CompletedTask;
    }

    public Task SetButtonImageAsync(int buttonIndex, byte[] imageData)
    {
        StoreImage(buttonIndex, imageData);
        return Task.CompletedTask;
    }

    public Task SetButtonImageNoFlushAsync(int buttonIndex, byte[] imageData)
    {
        StoreImage(buttonIndex, imageData);
        return Task.CompletedTask;
    }

    private void StoreImage(int idx, byte[] jpeg)
    {
        if (idx < 0 || idx >= _displayButtons) return;
        using var bmp = SKBitmap.Decode(jpeg);
        if (bmp is null) return;

        SKBitmap src;
        bool disposeSrc = false;
        if (bmp.Width != _imgSize || bmp.Height != _imgSize)
        {
            src = bmp.Resize(new SKImageInfo(_imgSize, _imgSize), SKFilterQuality.Medium);
            disposeSrc = true;
        }
        else if (bmp.ColorType != SKColorType.Rgba8888)
        {
            src = bmp.Copy(SKColorType.Rgba8888);
            disposeSrc = true;
        }
        else
        {
            src = bmp;
        }

        try
        {
            var pixels = new byte[_imgSize * _imgSize * 4];
            src.GetPixelSpan().CopyTo(pixels);
            lock (_lock) { _images[idx] = pixels; _dirty[idx] = true; }
        }
        finally
        {
            if (disposeSrc) src.Dispose();
        }
    }

    private unsafe void RunSdlLoop()
    {
        using var sdl = SdlApi.GetApi();

        sdl.SetHint("SDL_COCOA_REQUIRE_MAIN_THREAD", "0");

        if (sdl.Init(SdlApi.InitVideo) < 0) return;

        var window = sdl.CreateWindow(
            "Mirage Simulator",
            0x2FFF0000, 0x2FFF0000,
            _windowW, _windowH,
            (uint)WindowFlags.Shown);
        if (window == null) { sdl.Quit(); return; }

        var renderer = sdl.CreateRenderer(window, -1,
            (uint)(RendererFlags.Software | RendererFlags.Presentvsync));
        if (renderer == null) { sdl.DestroyWindow(window); sdl.Quit(); return; }

        var textures = new Texture*[_displayButtons];
        for (int i = 0; i < _displayButtons; i++)
        {
            textures[i] = sdl.CreateTexture(renderer,
                (uint)PixelFormatEnum.Abgr8888,
                (int)TextureAccess.Streaming,
                _imgSize, _imgSize);
            fixed (byte* p = _placeholder)
                sdl.UpdateTexture(textures[i], null, p, _imgSize * 4);
        }

        const uint FrameMs = 1000u / 30u; // ~33 ms per frame

        Event ev = default;
        while (!_disposed)
        {
            uint frameStart = sdl.GetTicks();

            while (sdl.PollEvent(&ev) != 0)
            {
                switch (ev.Type)
                {
                    case (uint)EventType.Quit:
                        _disposed = true;
                        Disconnected?.Invoke(this, EventArgs.Empty);
                        break;
                    case (uint)EventType.Mousebuttondown:
                        HandleMouseDown(ev.Button.Button, ev.Button.X, ev.Button.Y);
                        break;
                    case (uint)EventType.Mousebuttonup:
                        HandleMouseUp(ev.Button.Button);
                        break;
                    case (uint)EventType.Mousewheel:
                        HandleWheel(ev.Wheel.Y);
                        break;
                    case (uint)EventType.Keydown:
                        HandleKeyDown(ev.Key.Keysym.Sym);
                        break;
                    case (uint)EventType.Keyup:
                        HandleKeyUp(ev.Key.Keysym.Sym);
                        break;
                }
            }

            if (_disposed) break;

            lock (_lock)
            {
                for (int i = 0; i < _displayButtons; i++)
                {
                    if (!_dirty[i]) continue;
                    var px = _images[i] ?? _placeholder;
                    fixed (byte* p = px)
                        sdl.UpdateTexture(textures[i], null, p, _imgSize * 4);
                    _dirty[i] = false;
                }
            }

            sdl.SetRenderDrawColor(renderer, 0x1A, 0x1A, 0x1A, 0xFF);
            sdl.RenderClear(renderer);

            // Display panels
            for (int i = 0; i < _displayButtons; i++)
            {
                var dst = new Rectangle<int>(i * _cellSize + Bezel, Bezel, _imgSize, _imgSize);
                sdl.RenderCopy(renderer, textures[i], null, &dst);
            }

            // Side buttons as circles
            bool[] sideStates;
            lock (_lock) { sideStates = (bool[])_buttonStates.Clone(); }

            int circleCy = _cellSize + CircleAreaHeight / 2;
            for (int i = 0; i < _sideButtons; i++)
            {
                int circleCx = _windowW * (2 * i + 1) / (_sideButtons * 2);
                bool pressed = sideStates[_displayButtons + i];
                if (pressed)
                    sdl.SetRenderDrawColor(renderer, 0xE0, 0xE0, 0xE0, 0xFF);
                else
                    sdl.SetRenderDrawColor(renderer, 0x50, 0x50, 0x50, 0xFF);
                DrawFilledCircle(sdl, renderer, circleCx, circleCy, CircleRadius);
            }

            sdl.RenderPresent(renderer);

            uint elapsed = sdl.GetTicks() - frameStart;
            if (elapsed < FrameMs)
                sdl.Delay(FrameMs - elapsed);
        }

        for (int i = 0; i < _displayButtons; i++) sdl.DestroyTexture(textures[i]);
        sdl.DestroyRenderer(renderer);
        sdl.DestroyWindow(window);
        sdl.Quit();
    }

    private unsafe void DrawFilledCircle(SdlApi sdl, Renderer* renderer, int cx, int cy, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            int dx = (int)Math.Sqrt((double)(radius * radius - dy * dy));
            var row = new Rectangle<int>(cx - dx, cy + dy, dx * 2 + 1, 1);
            sdl.RenderFillRect(renderer, &row);
        }
    }

    private void HandleMouseDown(byte button, int x, int y)
    {
        if (button == SdlApi.ButtonLeft)
        {
            if (y >= _cellSize) return; // clicks in circle area are keyboard-only
            int xInCell = x % _cellSize;
            if (xInCell < Bezel || xInCell >= Bezel + _imgSize) return;
            if (y < Bezel || y >= Bezel + _imgSize) return;
            int idx = x / _cellSize;
            if ((uint)idx >= _displayButtons) return;
            _lastPressedButton = idx;
            bool[] states;
            lock (_lock) { _buttonStates[idx] = true; states = (bool[])_buttonStates[.._displayButtons].Clone(); }
            ImageButtonChanged?.Invoke(this, new ImageButtonEventArgs(idx, true, states));
        }
        else if (button == SdlApi.ButtonMiddle)
        {
            bool[] states;
            lock (_lock) { _encoderStates[0] = true; states = (bool[])_encoderStates.Clone(); }
            EncoderButtonChanged?.Invoke(this, new EncoderButtonEventArgs(0, true, states));
        }
    }

    private void HandleMouseUp(byte button)
    {
        if (button == SdlApi.ButtonLeft && _lastPressedButton >= 0)
        {
            int idx = _lastPressedButton;
            _lastPressedButton = -1;
            bool[] states;
            lock (_lock) { _buttonStates[idx] = false; states = (bool[])_buttonStates[.._displayButtons].Clone(); }
            ImageButtonChanged?.Invoke(this, new ImageButtonEventArgs(idx, false, states));
        }
        else if (button == SdlApi.ButtonMiddle)
        {
            bool[] states;
            lock (_lock) { _encoderStates[0] = false; states = (bool[])_encoderStates.Clone(); }
            EncoderButtonChanged?.Invoke(this, new EncoderButtonEventArgs(0, false, states));
        }
    }

    private void HandleWheel(int deltaY)
    {
        int delta = Math.Sign(deltaY);
        if (delta != 0)
            EncoderRotated?.Invoke(this, new EncoderEventArgs(0, delta));
    }

    private void HandleKeyDown(int sym)
    {
        int sideIdx = KeyToSideIndex(sym);
        if (sideIdx < 0) return;
        int buttonIdx = _displayButtons + sideIdx;
        bool[] states;
        lock (_lock) { _buttonStates[buttonIdx] = true; states = (bool[])_buttonStates[_displayButtons..].Clone(); }
        TactileButtonChanged?.Invoke(this, new TactileButtonEventArgs(sideIdx, true, states));
    }

    private void HandleKeyUp(int sym)
    {
        int sideIdx = KeyToSideIndex(sym);
        if (sideIdx < 0) return;
        int buttonIdx = _displayButtons + sideIdx;
        bool[] states;
        lock (_lock) { _buttonStates[buttonIdx] = false; states = (bool[])_buttonStates[_displayButtons..].Clone(); }
        TactileButtonChanged?.Invoke(this, new TactileButtonEventArgs(sideIdx, false, states));
    }

    // Maps '1'..'9' to side-button indices 0..8, clamped to the configured count.
    private int KeyToSideIndex(int sym)
    {
        if (sym < '1' || sym > '9') return -1;
        int idx = sym - '1';
        return idx < _sideButtons ? idx : -1;
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
