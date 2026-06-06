using MirageBox;
using MirageBox.Oasis;
using MirageBox.TinyGauges;
using SkiaSharp;
using System.Diagnostics;

Console.WriteLine("=== Mirabox/Ajazz/Somfon .NET Control Surface Demo ===\n");

IMirageDevice device;

if (args.Contains("--simulator"))
{
    int simButtons = ParseIntArg(args, "--sim-buttons", 4);
    int simTactile = ParseIntArg(args, "--sim-tactile", 3);
    int simSize    = ParseIntArg(args, "--sim-size",    128);
    Console.WriteLine($"Using simulator device ({simButtons} display, {simTactile} tactile, {simSize}px).");
    device = new SimulatorDevice(simButtons, simTactile, simSize);
}
else
{
    Console.WriteLine("Discovering devices...");
    var devices = DeviceFactory.DiscoverDevices().ToList();

    if (devices.Count == 0)
    {
        Console.WriteLine("No devices found. Please connect a Mirabox/Ajazz/Somfon device.");
        Console.WriteLine("Tip: run with --simulator to use the software simulator.");
        return;
    }

    Console.WriteLine($"Found {devices.Count} device(s).\n");

    for (int i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        Console.WriteLine($"[{i}] 0x{d.VendorId:X4}:0x{d.ProductId:X4}  Serial={d.SerialNumber}  ImageButtons={d.ImageButtonCount}  TactileButtons={d.TactileButtonCount}  Encoders={d.EncoderCount}");
    }
    Console.WriteLine();

    device = devices[0];
    Console.WriteLine("Using device [0] for graphics-speed test.\n");
}


// Attempt to load embedded typeface resource
var tfFilename = ParseStringArg(args, "--font", "prophet.ttf");
var typeface = ResourceLoader.TryLoadTypeface(tfFilename);
if (typeface is null)
{
    Console.WriteLine($"[gfx] {tfFilename} not loaded; using default system typeface.");
}

int PanelCount = device.ImageButtonCount;
var values = new int[PanelCount];
var gaugeTypeIndex = new int[PanelCount];
var themeIndex = new int[PanelCount];
var rangeIndex = new int[PanelCount];
float rainbowHue = 0f;

var stateLock = new object();
int selectedPanel = 0;
var dirty = new bool[PanelCount];
for (int i = 0; i < PanelCount; i++) dirty[i] = true;

long uploadCount = 0;
double totalUploadMs = 0;
var recentUploadMs = new Queue<double>();
const int recentUploadWindow = 200;
var metricsLock = new object();
var metricsStopwatch = Stopwatch.StartNew();
var statsCts = new CancellationTokenSource();
var renderCts = new CancellationTokenSource();

RenderFunc[] styles;

styles =
[
    Styles.Radial(180, 180),
    Styles.Radial(135, 270),
    Styles.TankFill(),
    Styles.Numeric(),
    Styles.Bar(),
    Styles.ImageFade(
        ResourceLoader.TryLoadBitmap("comp04.png") ?? throw new Exception("Failed to load comp04.png"),
        ResourceLoader.TryLoadBitmap("comp05.png") ?? throw new Exception("Failed to load comp05.png")
    ),
    Styles.Image(ResourceLoader.TryLoadBitmap("dopelives.png") ?? throw new Exception("Failed to load dopelives.png")),
    Styles.Image(ResourceLoader.TryLoadBitmap("enby.png") ?? throw new Exception("Failed to load enby.png")),
    Styles.Perimeter(),
    Styles.FullRing(),
    Styles.LiquidTank(),
    Styles.BigDial(),
    Styles.NumberBar(),
    Styles.LedRing(),
    Styles.ArcScale(),
    Styles.DualRing(),
    Styles.ValueScale(),
    Styles.SegmentBar(),
    Styles.Battery(),
    Styles.Thermometer()
];

try
{
    Console.WriteLine("Initializing [0]...");
    await device.InitializeAsync();
    await device.SetBrightnessAsync(10);


    device.ImageButtonChanged += (s, e) =>
    {
        if (!e.IsPressed) return;
        int previous;
        lock (stateLock)
        {
            previous = selectedPanel;
            selectedPanel = e.ButtonIndex;
            dirty[previous] = true;
            if (e.ButtonIndex < PanelCount) dirty[e.ButtonIndex] = true;
        }
        Console.WriteLine($"Selected panel: {e.ButtonIndex + 1}");
    };

    device.TactileButtonChanged += (s, e) =>
    {
        if (!e.IsPressed) return;
        lock (stateLock)
        {
            int panel = selectedPanel;
            if (e.ButtonIndex == 0)
                gaugeTypeIndex[panel] = (gaugeTypeIndex[panel] + 1) % styles.Length;
            else if (e.ButtonIndex == 1)
                themeIndex[panel] = (themeIndex[panel] + 1) % 4;
            else if (e.ButtonIndex == 2)
            {
                rangeIndex[panel] = (rangeIndex[panel] + 1) % 3;
                values[panel] = 0;
            }
            else return;
            dirty[panel] = true;
            Console.WriteLine($"Panel {panel + 1} aspects: type={gaugeTypeIndex[panel]} theme={themeIndex[panel]} range={rangeIndex[panel]}");
        }
    };

    device.EncoderRotated += (s, e) =>
    {
        if (e.EncoderIndex != 0) return;
        lock (stateLock)
        {
            int panel = selectedPanel;
            var (min, max) = GetRange(rangeIndex[panel]);
            values[panel] = Math.Clamp(values[panel] + e.RotationDelta, min, max);
            dirty[panel] = true;
        }
    };

    device.EncoderButtonChanged += (s, e) =>
    {
        if (e.EncoderIndex != 0 || !e.IsPressed) return;
        lock (stateLock)
        {
            gaugeTypeIndex[selectedPanel] = (gaugeTypeIndex[selectedPanel] + 1) % styles.Length;
            dirty[selectedPanel] = true;
            Console.WriteLine($"Panel {selectedPanel + 1} aspects: type={gaugeTypeIndex[selectedPanel]} theme={themeIndex[selectedPanel]} range={rangeIndex[selectedPanel]}");
        }
    };

    var disconnectCts = new CancellationTokenSource();
    device.Disconnected += (_, _) =>
    {
        Console.WriteLine("\nDevice disconnected.");
        disconnectCts.Cancel();
    };

    await device.StartListeningAsync();
    _ = PrintStatsLoopAsync(statsCts.Token);
    _ = RenderLoopAsync(renderCts.Token);

    Console.WriteLine("Ready:");
    Console.WriteLine("- Press panel buttons 1-6 to select a value.");
    Console.WriteLine("- Turn big knob (encoder 1) to change selected value.");
    Console.WriteLine("- Upload performance stats print every 2 seconds.\n");

    try { await Task.Delay(Timeout.Infinite, disconnectCts.Token); }
    catch (OperationCanceledException) { }

    async Task RenderLoopAsync(CancellationToken token)
    {
        const int targetFps = 30;
        var frameDuration = TimeSpan.FromSeconds(1.0 / targetFps);

        while (!token.IsCancellationRequested)
        {
            var frameStart = Stopwatch.GetTimestamp();

                rainbowHue = (rainbowHue + (float)(frameDuration.TotalSeconds * 140.0)) % 360f;
 
            bool anyUploaded = false;
            var sw = Stopwatch.StartNew();


            for (int panel = 0; panel < Math.Min(PanelCount, device.ImageButtonCount); panel++)
            {
                try
                {
                    byte[] imageData = RenderGaugeToJpeg(panel, typeface, selectedPanel, values, gaugeTypeIndex, styles, themeIndex, rangeIndex, rainbowHue, device.ImageWidth, device.ImageHeight);

                    await device.SetButtonImageNoFlushAsync(panel, imageData);
                    anyUploaded = true;

                    lock (metricsLock)
                        uploadCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Render error on panel {panel + 1}: {ex.Message}");
                }
            }

            if (anyUploaded)
            {
                await device.FlushAsync();
                sw.Stop();

                lock (metricsLock)
                {
                    totalUploadMs += sw.Elapsed.TotalMilliseconds;
                    recentUploadMs.Enqueue(sw.Elapsed.TotalMilliseconds);
                    while (recentUploadMs.Count > recentUploadWindow)
                        recentUploadMs.Dequeue();
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(frameStart);
            var remaining = frameDuration - elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, token);
        }
    }

    async Task PrintStatsLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);

            long count;
            double avgMs;
            double p95Ms;
            double ups;

            lock (metricsLock)
            {
                count = uploadCount;
                avgMs = count == 0 ? 0 : totalUploadMs / count;
                ups = count == 0 ? 0 : count / Math.Max(0.001, metricsStopwatch.Elapsed.TotalSeconds);

                if (recentUploadMs.Count == 0)
                {
                    p95Ms = 0;
                }
                else
                {
                    var arr = recentUploadMs.ToArray();
                    Array.Sort(arr);
                    int idx = (int)Math.Ceiling(arr.Length * 0.95) - 1;
                    idx = Math.Clamp(idx, 0, arr.Length - 1);
                    p95Ms = arr[idx];
                }
            }

            Console.WriteLine($"[gfx] uploads={count} avg={avgMs:F1}ms p95={p95Ms:F1}ms rate={ups:F2}/s");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    Console.WriteLine("\nStopping...");
    statsCts.Cancel();
    renderCts.Cancel();
    await device.StopListeningAsync();
    device.Dispose();
}

static byte[] RenderGaugeToJpeg(
    int panel,
    SKTypeface? typeface,
    int selectedPanel,
    int[] values,
    int[] gaugeTypeIndex,
    RenderFunc[] styles,
    int[] themeIndex,
    int[] rangeIndex,
    float rainbowHue,
    int imageWidth = 64,
    int imageHeight = 64)
{
    var (min, max) = GetRange(rangeIndex[panel]);
    var (primary, secondary, background, text) = GetTheme(themeIndex[panel], rainbowHue);

    // Override secondary color if this panel is selected
    if (panel == selectedPanel)
    {
        secondary = primary.WithAlpha(150);
    }

    var theme = new Theme
    {
        Typeface = typeface,
        PrimaryColor = primary,
        SecondaryColor = secondary,
        BackgroundColor = background,
        TextColor = text,
        Accents = Array.Empty<SKColor>()
    };

    var rangedValue = new RangedValue(min, max, values[panel]);
    var label = $"Panel {panel + 1}";

    // Use default typeface if none provided
    var effectiveTypeface = typeface ?? SKTypeface.Default;

    // Select the appropriate renderer
    RenderFunc renderer = styles[gaugeTypeIndex[panel]] ?? Styles.Numeric();

    // Create gauge config with renderer
    var config = new GaugeConfig(
        Value: rangedValue,
        Label: label,
        Typeface: effectiveTypeface,
        Theme: theme,
        Renderer: renderer
    );

    // Create bitmap and canvas for rendering
    var bitmap = new SKBitmap(imageWidth, imageHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        var bounds = new SKRect(0, 0, imageWidth, imageHeight);
        // Render using the config's renderer function
        config.Renderer(canvas, config.Theme, config.Typeface, bounds, config.Label, config.Value);
    }

    // Encode to JPEG
    using var image = SKImage.FromBitmap(bitmap);
    using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
    bitmap.Dispose();

    return encoded.ToArray();
}

static (int min, int max) GetRange(int idx)
{
    return idx switch
    {
        0 => (-100, 100),
        1 => (0, 100),
        _ => (-1000, 1000)
    };
}

static (SKColor primary, SKColor secondary, SKColor background, SKColor text) GetTheme(int idx, float hue)
{
    return idx switch
    {
        0 => (SKColor.FromHsv(hue % 360f, 90f, 100f), new SKColor(70, 78, 94), new SKColor(18, 22, 30), new SKColor(243, 246, 255)),
        1 => (new SKColor(120, 220, 255), new SKColor(42, 62, 74), new SKColor(8, 18, 24), new SKColor(224, 246, 255)),
        2 => (new SKColor(255, 186, 72), new SKColor(84, 62, 40), new SKColor(24, 16, 8), new SKColor(255, 243, 224)),
        _ => (new SKColor(151, 242, 156), new SKColor(48, 84, 52), new SKColor(10, 24, 12), new SKColor(235, 255, 236))
    };
}

static int ParseIntArg(string[] args, string name, int defaultValue)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name && int.TryParse(args[i + 1], out int v))
            return v;
    return defaultValue;
}

static string ParseStringArg(string[] args, string name, string defaultValue)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name)
            return args[i + 1];
    return defaultValue;
}
