namespace MirageBox;

using Silk.NET.Maths;
using Silk.NET.SDL;
using SkiaSharp;
using SdlApi = Silk.NET.SDL.Sdl;
using SysThread = System.Threading.Thread;

/// <summary>
/// A software simulator that implements IMirageDevice using an SDL2 window.
/// Four button panels are shown side-by-side; the mouse scroll wheel drives encoder 0.
/// </summary>
public sealed class SimulatorDevice : IMirageDevice
{
    private const int Buttons = 4;
    private const int ImgSize = 128;
    private const int Bezel = 8;
    private const int CellSize = ImgSize + 2 * Bezel;
    private const int WindowW = Buttons * CellSize;
    private const int WindowH = CellSize;

    private readonly object _lock = new();
    private readonly byte[]?[] _images = new byte[Buttons][];
    private readonly bool[] _dirty = new bool[Buttons];
    private readonly bool[] _buttonStates = new bool[Buttons];
    private readonly bool[] _encoderStates = new bool[1];
    private int _lastPressedButton = -1;
    private volatile bool _disposed;
    private SysThread? _thread;

    public int VendorId => 0;
    public int ProductId => 0;
    public string SerialNumber => "SIM-0";
    public int ButtonCount => Buttons;
    public int EncoderCount => 1;
    public int ImageWidth => ImgSize;
    public int ImageHeight => ImgSize;

    public event EventHandler<ButtonEventArgs>? ButtonChanged;
    public event EventHandler<EncoderEventArgs>? EncoderPressed;
    public event EventHandler<EncoderEventArgs>? EncoderReleased;
    public event EventHandler<EncoderEventArgs>? EncoderRotated;
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
        if (buttonIndex < 0 || buttonIndex >= Buttons) return Task.CompletedTask;
        lock (_lock) { _images[buttonIndex] = null; _dirty[buttonIndex] = true; }
        return Task.CompletedTask;
    }

    public Task ClearAllDisplaysAsync()
    {
        lock (_lock)
        {
            for (int i = 0; i < Buttons; i++) { _images[i] = null; _dirty[i] = true; }
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
        if (idx < 0 || idx >= Buttons) return;
        using var bmp = SKBitmap.Decode(jpeg);
        if (bmp is null) return;

        SKBitmap src;
        bool disposeSrc = false;
        if (bmp.Width != ImgSize || bmp.Height != ImgSize)
        {
            src = bmp.Resize(new SKImageInfo(ImgSize, ImgSize), SKFilterQuality.Medium);
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
            var pixels = new byte[ImgSize * ImgSize * 4];
            src.GetPixelSpan().CopyTo(pixels);
            lock (_lock) { _images[idx] = pixels; _dirty[idx] = true; }
        }
        finally
        {
            if (disposeSrc) src.Dispose();
        }
    }

    // Dark gray placeholder shown for empty button slots.
    private static readonly byte[] Placeholder = CreatePlaceholder();
    private static byte[] CreatePlaceholder()
    {
        var buf = new byte[ImgSize * ImgSize * 4];
        for (int i = 0; i < buf.Length; i += 4)
        { buf[i] = 40; buf[i + 1] = 40; buf[i + 2] = 40; buf[i + 3] = 255; }
        return buf;
    }

    private unsafe void RunSdlLoop()
    {
        using var sdl = SdlApi.GetApi();

        // Allow SDL to run off the main thread on macOS.
        sdl.SetHint("SDL_COCOA_REQUIRE_MAIN_THREAD", "0");

        if (sdl.Init(SdlApi.InitVideo) < 0) return;

        var window = sdl.CreateWindow(
            "Mirage Simulator",
            0x2FFF0000, 0x2FFF0000,  // SDL_WINDOWPOS_CENTERED
            WindowW, WindowH,
            (uint)WindowFlags.Shown);
        if (window == null) { sdl.Quit(); return; }

        var renderer = sdl.CreateRenderer(window, -1,
            (uint)(RendererFlags.Software | RendererFlags.Presentvsync));
        if (renderer == null) { sdl.DestroyWindow(window); sdl.Quit(); return; }

        var textures = new Texture*[Buttons];
        for (int i = 0; i < Buttons; i++)
        {
            textures[i] = sdl.CreateTexture(renderer,
                (uint)PixelFormatEnum.Abgr8888,
                (int)TextureAccess.Streaming,
                ImgSize, ImgSize);
            fixed (byte* p = Placeholder)
                sdl.UpdateTexture(textures[i], null, p, ImgSize * 4);
        }

        Event ev = default;
        while (!_disposed)
        {
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
                }
            }

            if (_disposed) break;

            lock (_lock)
            {
                for (int i = 0; i < Buttons; i++)
                {
                    if (!_dirty[i]) continue;
                    var px = _images[i] ?? Placeholder;
                    fixed (byte* p = px)
                        sdl.UpdateTexture(textures[i], null, p, ImgSize * 4);
                    _dirty[i] = false;
                }
            }

            sdl.SetRenderDrawColor(renderer, 0x1A, 0x1A, 0x1A, 0xFF);
            sdl.RenderClear(renderer);

            for (int i = 0; i < Buttons; i++)
            {
                var dst = new Rectangle<int>(i * CellSize + Bezel, Bezel, ImgSize, ImgSize);
                sdl.RenderCopy(renderer, textures[i], null, &dst);
            }

            sdl.RenderPresent(renderer);
        }

        for (int i = 0; i < Buttons; i++) sdl.DestroyTexture(textures[i]);
        sdl.DestroyRenderer(renderer);
        sdl.DestroyWindow(window);
        sdl.Quit();
    }

    private void HandleMouseDown(byte button, int x, int y)
    {
        if (button == SdlApi.ButtonLeft)
        {
            int xInCell = x % CellSize;
            if (xInCell < Bezel || xInCell >= Bezel + ImgSize) return;
            if (y < Bezel || y >= Bezel + ImgSize) return;
            int idx = x / CellSize;
            if ((uint)idx >= Buttons) return;
            _lastPressedButton = idx;
            bool[] states;
            lock (_lock) { _buttonStates[idx] = true; states = (bool[])_buttonStates.Clone(); }
            ButtonChanged?.Invoke(this, new ButtonEventArgs(idx, true, states));
        }
        else if (button == SdlApi.ButtonMiddle)
        {
            Console.WriteLine("Encoder pressed");
            bool[] states;
            lock (_lock) { _encoderStates[0] = true; states = (bool[])_encoderStates.Clone(); }
            EncoderPressed?.Invoke(this, new EncoderEventArgs(0, true, states));
        }
    }

    private void HandleMouseUp(byte button)
    {
        if (button == SdlApi.ButtonLeft && _lastPressedButton >= 0)
        {
            int idx = _lastPressedButton;
            _lastPressedButton = -1;
            bool[] states;
            lock (_lock) { _buttonStates[idx] = false; states = (bool[])_buttonStates.Clone(); }
            ButtonChanged?.Invoke(this, new ButtonEventArgs(idx, false, states));
        }
        else if (button == SdlApi.ButtonMiddle)
        {
            Console.WriteLine("Encoder released");
            bool[] states;
            lock (_lock) { _encoderStates[0] = false; states = (bool[])_encoderStates.Clone(); }
            EncoderReleased?.Invoke(this, new EncoderEventArgs(0, false, states));
        }
    }

    private void HandleWheel(int deltaY)
    {
        int delta = Math.Sign(deltaY);
        if (delta != 0)
            EncoderRotated?.Invoke(this, new EncoderEventArgs(0, delta));
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
