using System.Text.Json;
using System.Text.Json.Serialization;

namespace MirageBox.Oasis.Core.Config;

public class OasisConfig
{
    [JsonPropertyName("devices")]
    public Dictionary<string, DeviceConfig> Devices { get; set; } = new();

    [JsonPropertyName("dataSources")]
    public Dictionary<string, DataSourceConfig> DataSources { get; set; } = new();

    [JsonPropertyName("gauges")]
    public Dictionary<string, GaugeConfig> Gauges { get; set; } = new();

    [JsonPropertyName("scenes")]
    public Dictionary<string, DeviceSceneConfig> Scenes { get; set; } = new();

    [JsonPropertyName("defaults")]
    public DefaultsConfig Defaults { get; set; } = new();

    [JsonPropertyName("themes")]
    public Dictionary<string, ThemeConfig> Themes { get; set; } = new();

    [JsonPropertyName("contentDirs")]
    public List<string> ContentDirs { get; set; } = ["~/.mirage/content", "./content"];
}

public class DeviceConfig
{
    [JsonPropertyName("serial")]
    public string? Serial { get; set; }

    [JsonPropertyName("simulator")]
    public bool Simulator { get; set; }

    [JsonPropertyName("buttons")]
    public int Buttons { get; set; } = 4;

    [JsonPropertyName("tactile")]
    public int Tactile { get; set; } = 3;

    [JsonPropertyName("imageSize")]
    public int ImageSize { get; set; } = 128;
}

public class DataSourceConfig
{
    [JsonPropertyName("plugin")]
    public string Plugin { get; set; } = "";

    [JsonPropertyName("config")]
    public Dictionary<string, JsonElement>? Config { get; set; }
}

public class GaugeConfig
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("sensor")]
    public string Sensor { get; set; } = "";

    [JsonPropertyName("renderer")]
    public RendererConfig Renderer { get; set; } = new();

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("font")]
    public string? Font { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Gauge minimum; null = use the source-provided range when available.</summary>
    [JsonPropertyName("min")]
    public float? Min { get; set; }

    /// <summary>Gauge maximum; null = use the source-provided range when available.</summary>
    [JsonPropertyName("max")]
    public float? Max { get; set; }
}

public class RendererConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FullRing";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Parameters { get; set; }
}

public class ButtonAssignmentConfig
{
    [JsonPropertyName("gauge")]
    public string? Gauge { get; set; }

    /// <summary>Single-press action (legacy key "action").</summary>
    [JsonPropertyName("action")]
    public ActionConfig? Action { get; set; }

    [JsonPropertyName("doublePressAction")]
    public ActionConfig? DoublePressAction { get; set; }

    [JsonPropertyName("holdAction")]
    public ActionConfig? HoldAction { get; set; }
}

public class ActionConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Parameters { get; set; }
}

public class DeviceSceneConfig
{
    [JsonPropertyName("activeScene")]
    public string ActiveScene { get; set; } = "";

    [JsonPropertyName("pinned")]
    public Dictionary<string, ButtonAssignmentConfig> Pinned { get; set; } = new();

    [JsonPropertyName("list")]
    public Dictionary<string, SceneConfig> List { get; set; } = new();
}

public class SceneConfig
{
    [JsonPropertyName("buttons")]
    public Dictionary<string, ButtonAssignmentConfig> Buttons { get; set; } = new();

    [JsonPropertyName("encoders")]
    public Dictionary<string, ButtonAssignmentConfig>? Encoders { get; set; }

    [JsonPropertyName("tactileButtons")]
    public Dictionary<string, ButtonAssignmentConfig>? TactileButtons { get; set; }
}

public class DefaultsConfig
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "default";

    [JsonPropertyName("font")]
    public string Font { get; set; } = "prophet.ttf";
}

public class ThemeConfig
{
    [JsonPropertyName("primary")]
    public string? Primary { get; set; }

    [JsonPropertyName("secondary")]
    public string? Secondary { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("accents")]
    public List<string>? Accents { get; set; }
}
