using MirageBox;
using MirageBox.Oasis;
using MirageBox.TinyGauges;
using SkiaSharp;
using System.Diagnostics;

Console.WriteLine("=== Mirabox/Ajazz/Somfon .NET Control Surface Demo ===\n");

Console.WriteLine("Discovering devices...");
var devices = DeviceFactory.DiscoverDevices().ToList();

if (devices.Count == 0)
{
    Console.WriteLine("No devices found. Please connect a Mirabox/Ajazz/Somfon device.");
    return;
}

Console.WriteLine($"Found {devices.Count} device(s).\n");

for (int i = 0; i < devices.Count; i++)
{
    var d = devices[i];
    Console.WriteLine($"[{i}] 0x{d.VendorId:X4}:0x{d.ProductId:X4}  Serial={d.SerialNumber}  Buttons={d.ButtonCount}  Encoders={d.EncoderCount}");
}
Console.WriteLine();

var device = devices[0];
Console.WriteLine("Using device [0] for graphics-speed test.\n");

var typeface = ResourceLoader.TryLoadTypeface("arbata.ttf");
if (typeface is null)
{
    Console.WriteLine("[gfx] arbata.ttf not loaded; using default system typeface.");
}

const int PanelCount = 6;
var values = new int[PanelCount];
var gaugeTypeIndex = new int[PanelCount];
var themeIndex = new int[PanelCount];
var rangeIndex = new int[PanelCount];
float rainbowHue = 0f;

var stateLock = new object();
var renderLock = new SemaphoreSlim(1, 1);
int selectedPanel = 0;

long uploadCount = 0;
double totalUploadMs = 0;
var recentUploadMs = new Queue<double>();
const int recentUploadWindow = 200;
var metricsLock = new object();
var metricsStopwatch = Stopwatch.StartNew();
var statsCts = new CancellationTokenSource();
var animationCts = new CancellationTokenSource();

var animController = new AnimationController { TargetFrameRate = 60 };
animController.OnTick = dt =>
{
    lock (stateLock)
    {
        rainbowHue = (rainbowHue + (float)(dt * 140.0)) % 360f;
    }
};

void UpdateAnimatedSlots()
{
    for (int i = 0; i < PanelCount; i++)
    {
        bool active;
        lock (stateLock)
        {
            active = themeIndex[i] == 0 || i == selectedPanel;
        }
        animController.SetSlotEnabled(i, active);
    }
}

try
{
    Console.WriteLine("Initializing [0]...");
    await device.InitializeAsync();
    await device.SetBrightnessAsync(80);
    await device.SetLedBrightnessAsync(100);

    for (int panel = 0; panel < Math.Min(6, device.ButtonCount); panel++)
    {
        int captured = panel;
        animController.AddSlot(captured, () => RenderPanelAsync(captured));
        await RenderPanelAsync(panel);
    }
    UpdateAnimatedSlots();

    device.ButtonChanged += (s, e) =>
    {
        if (!e.IsPressed)
            return;

        // Top 6 buttons select active panel. Bottom buttons (6/7/8) change aspects.
        if (e.ButtonIndex is >= 0 and < 6)
        {
            int previous;
            lock (stateLock)
            {
                previous = selectedPanel;
                selectedPanel = e.ButtonIndex;
            }

            _ = RenderPanelAsync(previous);
            _ = RenderPanelAsync(selectedPanel);
            UpdateAnimatedSlots();
            Console.WriteLine($"Selected panel: {selectedPanel + 1}");
            return;
        }

        int panel;
        lock (stateLock)
        {
            panel = selectedPanel;
            if (e.ButtonIndex == 6)
            {
                gaugeTypeIndex[panel] = (gaugeTypeIndex[panel] + 1) % 4;
            }
            else if (e.ButtonIndex == 7)
            {
                themeIndex[panel] = (themeIndex[panel] + 1) % 4;
            }
            else if (e.ButtonIndex == 8)
            {
                rangeIndex[panel] = (rangeIndex[panel] + 1) % 3;
                values[panel] = 0;
            }
            else
            {
                return;
            }
        }

        _ = RenderPanelAsync(panel);
        UpdateAnimatedSlots();
        Console.WriteLine($"Panel {panel + 1} aspects: type={gaugeTypeIndex[panel]} theme={themeIndex[panel]} range={rangeIndex[panel]}");
    };

    device.EncoderRotated += (s, e) =>
    {
        if (e.EncoderIndex != 0)
            return;

        int panel;
        int newValue;

        lock (stateLock)
        {
            panel = selectedPanel;
            var (min, max) = GetRange(rangeIndex[panel]);
            values[panel] = Math.Clamp(values[panel] + e.RotationDelta, min, max);
            newValue = values[panel];
        }

        _ = RenderPanelAsync(panel);
        //Console.WriteLine($"Panel {panel + 1} value: {newValue}");
    };

    await device.StartListeningAsync();
    _ = PrintStatsLoopAsync(statsCts.Token);
    _ = animController.RunAsync(animationCts.Token);

    Console.WriteLine("Ready:");
    Console.WriteLine("- Press panel buttons 1-6 to select a value.");
    Console.WriteLine("- Turn big knob (encoder 1) to change selected value.");
    Console.WriteLine("- Upload performance stats print every 2 seconds.\n");

    await Task.Delay(Timeout.Infinite);

    async Task RenderPanelAsync(int panel, bool skipIfBusy = false)
    {
        if (panel < 0 || panel >= Math.Min(6, device.ButtonCount))
            return;

        if (skipIfBusy)
        {
            if (!renderLock.Wait(0))
                return;
        }
        else
        {
            await renderLock.WaitAsync();
        }

        try
        {
            byte[] imageData;
            lock (stateLock)
            {
                var gauge = BuildGaugeForPanel(panel, typeface, selectedPanel, values, gaugeTypeIndex, themeIndex, rangeIndex, rainbowHue);
                imageData = gauge.RenderJpeg(100);
            }

            var sw = Stopwatch.StartNew();
            await device.SetButtonImageAsync(panel, imageData);
            sw.Stop();

            lock (metricsLock)
            {
                uploadCount++;
                totalUploadMs += sw.Elapsed.TotalMilliseconds;
                recentUploadMs.Enqueue(sw.Elapsed.TotalMilliseconds);
                while (recentUploadMs.Count > recentUploadWindow)
                {
                    recentUploadMs.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Render error on panel {panel + 1}: {ex.Message}");
        }
        finally
        {
            renderLock.Release();
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
    animationCts.Cancel();
    await device.StopListeningAsync();
    device.Dispose();
}

static ITinyGauge BuildGaugeForPanel(
    int panel,
    SKTypeface? typeface,
    int selectedPanel,
    int[] values,
    int[] gaugeTypeIndex,
    int[] themeIndex,
    int[] rangeIndex,
    float rainbowHue)
{
    var gauge = gaugeTypeIndex[panel] switch
    {
        0 => TinyGaugeFactory.CreateRadial(),
        1 => TinyGaugeFactory.CreateFillTank(),
        2 => TinyGaugeFactory.CreateNumeric(),
        _ => TinyGaugeFactory.CreateBar()
    };

    var (min, max) = GetRange(rangeIndex[panel]);
    var (primary, secondary, background, text) = GetTheme(themeIndex[panel], rainbowHue);

    gauge
        .SetSize(72, 72)
        .SetRange(min, max)
        .SetValue(values[panel])
        .SetLabel($"Panel {panel + 1}")
        .SetTypeface(typeface)
        .SetPrimaryColor(primary)
        .SetSecondaryColor(secondary)
        .SetBackgroundColor(background)
        .SetTextColor(text);

    if (panel == selectedPanel)
    {
        gauge.SetSecondaryColor(primary.WithAlpha(150));
    }

    return gauge;
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
