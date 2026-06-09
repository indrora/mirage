using MirageBox;
using MirageBox.Oasis;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.Engine;
using MirageBox.TinyGauges;
using SkiaSharp;
using System.Diagnostics;
using System.Text.Json;
using Config = MirageBox.Oasis.Core.Config;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        Oasis — MirageBox Control Surface Engine

        Usage:
          oasis [options]

        Options:
          --config <path>       Path to config.json (default: ~/.mirage/config.json)
          --simulator           Use simulator device (generates default config if none exists)
          --sim-buttons <n>     Simulator display button count (default: 6)
          --sim-tactile <n>     Simulator tactile button count (default: 3)
          --sim-size <px>       Simulator image size in pixels (default: 128)
          --demo                Run legacy tech demo mode
          --font <file>         Font file for demo mode (default: prophet.ttf)
          --help, -h            Show this help
        """);
    return;
}

if (args.Contains("--demo"))
{
    await RunDemo(args);
    return;
}

await RunEngine(args);

static async Task RunEngine(string[] args)
{
    var configPath = ParseStringArg(args, "--config", null)
                     ?? ConfigLoader.DefaultConfigPath;

    OasisConfig config;

    if (File.Exists(configPath))
    {
        Console.WriteLine($"Loading config from {configPath}");
        config = ConfigLoader.Load(configPath);
    }
    else if (args.Contains("--simulator"))
    {
        Console.WriteLine("No config found. Generating default config with simulator device...");
        config = GenerateDefaultConfig(args);
        var dir = Path.GetDirectoryName(configPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        ConfigLoader.Save(config, configPath);
        Console.WriteLine($"Saved default config to {configPath}");
    }
    else
    {
        Console.WriteLine("No config file found and no --simulator flag.");
        Console.WriteLine($"Expected config at: {configPath}");
        Console.WriteLine("Run with --simulator to generate a default config, or --demo for the old tech demo.");

        var discovered = DeviceFactory.DiscoverDevices().ToList();
        if (discovered.Count > 0)
        {
            Console.WriteLine($"\nFound {discovered.Count} device(s):");
            foreach (var d in discovered)
                Console.WriteLine($"  Serial={d.SerialNumber}  Buttons={d.ImageButtonCount}  Encoders={d.EncoderCount}");
            Console.WriteLine("\nCreate a config.json referencing one of these serials, or use --simulator.");
        }
        return;
    }

    Console.WriteLine($"Devices: {config.Devices.Count}  Sources: {config.DataSources.Count}  Gauges: {config.Gauges.Count}  Scenes: {config.Scenes.Count}");

    using var engine = new OasisEngine(config);
    var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await engine.StartAsync(cts.Token);
        Console.WriteLine("\nOasis running. Press Ctrl+C to stop.");
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    catch (OperationCanceledException) { }
    finally
    {
        Console.WriteLine("\nShutting down...");
        await engine.StopAsync();
    }
}

static OasisConfig GenerateDefaultConfig(string[] args)
{
    int simButtons = ParseIntArg(args, "--sim-buttons", 6);
    int simTactile = ParseIntArg(args, "--sim-tactile", 3);
    int simSize = ParseIntArg(args, "--sim-size", 128);

    return new OasisConfig
    {
        Devices = new()
        {
            ["main"] = new DeviceConfig
            {
                Simulator = true,
                Buttons = simButtons,
                Tactile = simTactile,
                ImageSize = simSize
            }
        },
        DataSources = new()
        {
            ["clock"] = new DataSourceConfig { Plugin = "__builtin:clock" },
            ["counter1"] = new DataSourceConfig
            {
                Plugin = "__builtin:counter",
                Config = JsonDocument.Parse("""{"initial":0,"step":1,"min":0,"max":100}""")
                    .RootElement.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.Clone())
            },
            ["timer1"] = new DataSourceConfig
            {
                Plugin = "__builtin:timer",
                Config = JsonDocument.Parse("""{"format":"mm\\:ss"}""")
                    .RootElement.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.Clone())
            }
        },
        Gauges = new()
        {
            ["clock"] = new Config.GaugeConfig
            {
                Source = "clock", Sensor = "time",
                Renderer = new RendererConfig { Type = "Text" },
                Label = "Time"
            },
            ["seconds"] = new Config.GaugeConfig
            {
                Source = "clock", Sensor = "second",
                Renderer = new RendererConfig { Type = "FullRing" },
                Label = "Sec", Min = 0, Max = 59
            },
            ["counter"] = new Config.GaugeConfig
            {
                Source = "counter1", Sensor = "value",
                Renderer = new RendererConfig { Type = "NumberBar" },
                Label = "Count", Min = 0, Max = 100
            },
            ["timer"] = new Config.GaugeConfig
            {
                Source = "timer1", Sensor = "elapsed",
                Renderer = new RendererConfig { Type = "Text" },
                Label = "Timer"
            },
            ["timerRing"] = new Config.GaugeConfig
            {
                Source = "timer1", Sensor = "elapsedSeconds",
                Renderer = new RendererConfig { Type = "FullRing" },
                Label = "Timer", Min = 0, Max = 300
            }
        },
        Scenes = new()
        {
            ["main"] = new DeviceSceneConfig
            {
                ActiveScene = "dashboard",
                Pinned = new()
                {
                    ["0"] = new ButtonAssignmentConfig
                    {
                        Gauge = "clock",
                        Action = new ActionConfig { Type = "switchScene", Parameters = new() { ["scene"] = JsonDocument.Parse("\"next\"").RootElement.Clone() } }
                    }
                },
                List = new()
                {
                    ["dashboard"] = new SceneConfig
                    {
                        Buttons = new()
                        {
                            ["1"] = new ButtonAssignmentConfig { Gauge = "seconds" },
                            ["2"] = new ButtonAssignmentConfig { Gauge = "counter" },
                            ["3"] = new ButtonAssignmentConfig { Gauge = "timer" },
                            ["4"] = new ButtonAssignmentConfig { Gauge = "timerRing" }
                        }
                    },
                    ["timers"] = new SceneConfig
                    {
                        Buttons = new()
                        {
                            ["1"] = new ButtonAssignmentConfig { Gauge = "timer" },
                            ["2"] = new ButtonAssignmentConfig { Gauge = "timerRing" },
                            ["3"] = new ButtonAssignmentConfig { Gauge = "counter" },
                            ["4"] = new ButtonAssignmentConfig { Gauge = "counter" }
                        }
                    }
                }
            }
        },
        Themes = new()
        {
            ["default"] = new ThemeConfig
            {
                Primary = "#4CAF50", Secondary = "#333333",
                Background = "#1A1A1A", Text = "#FFFFFF"
            }
        }
    };
}

// ── Legacy demo mode ──────────────────────────────────────────────────────

static async Task RunDemo(string[] args)
{
    Console.WriteLine("=== Oasis Tech Demo ===\n");

    IMirageDevice device;

    if (args.Contains("--simulator"))
    {
        int simButtons = ParseIntArg(args, "--sim-buttons", 4);
        int simTactile = ParseIntArg(args, "--sim-tactile", 3);
        int simSize = ParseIntArg(args, "--sim-size", 128);
        Console.WriteLine($"Simulator ({simButtons} display, {simTactile} tactile, {simSize}px).");
        device = new SimulatorDevice(simButtons, simTactile, simSize);
    }
    else
    {
        var devices = DeviceFactory.DiscoverDevices().ToList();
        if (devices.Count == 0)
        {
            Console.WriteLine("No devices found. Run with --simulator.");
            return;
        }
        device = devices[0];
        Console.WriteLine($"Using device Serial={device.SerialNumber}");
    }

    var typeface = ResourceLoader.TryLoadTypeface(ParseStringArg(args, "--font", "prophet.ttf"));

    int panelCount = device.ImageButtonCount;
    var values = new int[panelCount];
    var gaugeTypeIndex = new int[panelCount];
    var themeIndex = new int[panelCount];
    var rangeIndex = new int[panelCount];
    float rainbowHue = 0f;
    int selectedPanel = 0;
    var stateLock = new object();
    long uploadCount = 0;
    double totalUploadMs = 0;
    var recentUploadMs = new Queue<double>();
    var metricsLock = new object();
    var metricsStopwatch = Stopwatch.StartNew();

    RenderFunc[] styles =
    [
        Styles.Radial(180, 180), Styles.Radial(135, 270),
        Styles.TankFill(), Styles.Numeric(), Styles.Bar(),
        Styles.Perimeter(), Styles.FullRing(), Styles.LiquidTank(),
        Styles.BigDial(), Styles.NumberBar(), Styles.LedRing(),
        Styles.ArcScale(), Styles.DualRing(), Styles.ValueScale(),
        Styles.SegmentBar(), Styles.Battery(), Styles.Thermometer(),
        Styles.Text()
    ];

    await device.InitializeAsync();
    await device.SetBrightnessAsync(10);

    device.ImageButtonChanged += (_, e) =>
    {
        if (!e.IsPressed) return;
        lock (stateLock) { selectedPanel = e.ButtonIndex; }
    };

    device.TactileButtonChanged += (_, e) =>
    {
        if (!e.IsPressed) return;
        lock (stateLock)
        {
            if (e.ButtonIndex == 0) gaugeTypeIndex[selectedPanel] = (gaugeTypeIndex[selectedPanel] + 1) % styles.Length;
            else if (e.ButtonIndex == 1) themeIndex[selectedPanel] = (themeIndex[selectedPanel] + 1) % 4;
            else if (e.ButtonIndex == 2) { rangeIndex[selectedPanel] = (rangeIndex[selectedPanel] + 1) % 3; values[selectedPanel] = 0; }
        }
    };

    device.EncoderRotated += (_, e) =>
    {
        if (e.EncoderIndex != 0) return;
        lock (stateLock)
        {
            var (min, max) = GetRange(rangeIndex[selectedPanel]);
            values[selectedPanel] = Math.Clamp(values[selectedPanel] + e.RotationDelta, min, max);
        }
    };

    device.EncoderButtonChanged += (_, e) =>
    {
        if (e.EncoderIndex != 0 || !e.IsPressed) return;
        lock (stateLock) { gaugeTypeIndex[selectedPanel] = (gaugeTypeIndex[selectedPanel] + 1) % styles.Length; }
    };

    var disconnectCts = new CancellationTokenSource();
    device.Disconnected += (_, _) => { Console.WriteLine("\nDisconnected."); disconnectCts.Cancel(); };

    await device.StartListeningAsync();
    Console.WriteLine("Demo running. Ctrl+C to stop.\n");

    _ = Task.Run(async () =>
    {
        while (!disconnectCts.IsCancellationRequested)
        {
            await Task.Delay(2000, disconnectCts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            lock (metricsLock)
            {
                double avg = uploadCount == 0 ? 0 : totalUploadMs / uploadCount;
                double ups = uploadCount / Math.Max(0.001, metricsStopwatch.Elapsed.TotalSeconds);
                Console.WriteLine($"[gfx] uploads={uploadCount} avg={avg:F1}ms rate={ups:F2}/s");
            }
        }
    });

    var frameDuration = TimeSpan.FromSeconds(1.0 / 30);
    while (!disconnectCts.IsCancellationRequested)
    {
        var frameStart = Stopwatch.GetTimestamp();
        rainbowHue = (rainbowHue + (float)(frameDuration.TotalSeconds * 140.0)) % 360f;
        var sw = Stopwatch.StartNew();

        for (int p = 0; p < panelCount; p++)
        {
            int sel, gi, ti, ri, val;
            lock (stateLock) { sel = selectedPanel; gi = gaugeTypeIndex[p]; ti = themeIndex[p]; ri = rangeIndex[p]; val = values[p]; }

            var (min, max) = GetRange(ri);
            var (primary, secondary, background, text) = GetTheme(ti, rainbowHue);
            if (p == sel) secondary = primary.WithAlpha(150);

            var theme = new Theme { Typeface = typeface, PrimaryColor = primary, SecondaryColor = secondary, BackgroundColor = background, TextColor = text, Accents = [] };
            var rv = new RangedValue(min, max, val);

            using var surface = SKSurface.Create(new SKImageInfo(device.ImageWidth, device.ImageHeight));
            styles[gi](surface.Canvas, theme, typeface ?? SKTypeface.Default, new SKRect(0, 0, device.ImageWidth, device.ImageHeight), $"P{p + 1}", rv);
            using var img = surface.Snapshot();
            using var enc = img.Encode(SKEncodedImageFormat.Jpeg, 90);

            await device.SetButtonImageNoFlushAsync(p, enc.ToArray());
            lock (metricsLock) uploadCount++;
        }

        await device.FlushAsync();
        sw.Stop();
        lock (metricsLock) totalUploadMs += sw.Elapsed.TotalMilliseconds;

        var elapsed = Stopwatch.GetElapsedTime(frameStart);
        var remaining = frameDuration - elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, disconnectCts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    await device.StopListeningAsync();
    device.Dispose();
}

// ── Shared helpers ────────────────────────────────────────────────────────

static (int min, int max) GetRange(int idx) => idx switch
{
    0 => (-100, 100), 1 => (0, 100), _ => (-1000, 1000)
};

static (SkiaSharp.SKColor primary, SkiaSharp.SKColor secondary, SkiaSharp.SKColor background, SkiaSharp.SKColor text) GetTheme(int idx, float hue) => idx switch
{
    0 => (SkiaSharp.SKColor.FromHsv(hue % 360f, 90f, 100f), new(70, 78, 94), new(18, 22, 30), new(243, 246, 255)),
    1 => (new(120, 220, 255), new(42, 62, 74), new(8, 18, 24), new(224, 246, 255)),
    2 => (new(255, 186, 72), new(84, 62, 40), new(24, 16, 8), new(255, 243, 224)),
    _ => (new(151, 242, 156), new(48, 84, 52), new(10, 24, 12), new(235, 255, 236))
};

static int ParseIntArg(string[] args, string name, int defaultValue)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name && int.TryParse(args[i + 1], out int v)) return v;
    return defaultValue;
}

static string ParseStringArg(string[] args, string name, string? defaultValue)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return defaultValue!;
}
