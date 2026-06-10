using System.Reflection;
using System.Text.Json;
using MirageBox.TinyGauges;
using SkiaSharp;

namespace MirageBox.Oasis.Core.Engine;

/// <summary>
/// Describes a single parameter that a renderer factory method accepts.
/// </summary>
public record RendererParamInfo(string Key, string Description, RendererParamKind Kind, string? Default);

/// <summary>
/// A registered renderer — either parameterless or parameterized.
/// </summary>
public record RendererEntry(
    string Name,
    GaugeValueType ValueType,
    IReadOnlyList<RendererParamInfo> Parameters,
    Func<Dictionary<string, JsonElement>?, RenderFunc> Factory)
{
    public bool HasParameters => Parameters.Count > 0;
}

public class RendererRegistry
{
    private readonly Dictionary<string, RendererEntry> _renderers = new(StringComparer.OrdinalIgnoreCase);

    public RendererRegistry()
    {
        DiscoverFromAssembly(typeof(Styles).Assembly);
    }

    public void DiscoverFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<GaugeRendererAttribute>();
                if (attr == null) continue;
                if (method.ReturnType != typeof(RenderFunc)) continue;

                var methodParams = method.GetParameters();

                if (methodParams.Length == 0)
                {
                    var entry = new RendererEntry(
                        attr.Name, attr.ValueType,
                        Array.Empty<RendererParamInfo>(),
                        _ => (RenderFunc)method.Invoke(null, null)!);
                    _renderers[attr.Name] = entry;
                }
                else
                {
                    var paramInfos = BuildParamInfos(methodParams);
                    if (paramInfos == null) continue; // skip if any param is missing its attribute

                    var capturedMethod = method;
                    var capturedParams = methodParams;
                    var entry = new RendererEntry(
                        attr.Name, attr.ValueType, paramInfos,
                        configParams => InvokeParameterized(capturedMethod, capturedParams, paramInfos, configParams));
                    _renderers[attr.Name] = entry;
                }
            }
        }
    }

    public void Register(string name, GaugeValueType valueType, Func<RenderFunc> factory)
    {
        _renderers[name] = new RendererEntry(
            name, valueType,
            Array.Empty<RendererParamInfo>(),
            _ => factory());
    }

    public RendererEntry? Get(string name) =>
        _renderers.GetValueOrDefault(name);

    public RenderFunc? Resolve(string name, DataSources.SensorValueType sensorType,
        Dictionary<string, JsonElement>? parameters = null)
    {
        var entry = Get(name);
        if (entry == null) return null;

        if (sensorType == DataSources.SensorValueType.Text && entry.ValueType == GaugeValueType.Numeric)
        {
            var textEntry = Get("Text");
            if (textEntry != null)
            {
                Console.Error.WriteLine(
                    $"Warning: Renderer '{name}' is numeric but sensor is text — falling back to Text renderer");
                return textEntry.Factory(null);
            }
        }

        return entry.Factory(parameters);
    }

    public IEnumerable<RendererEntry> GetAll() => _renderers.Values;

    // ── private helpers ──────────────────────────────────────────

    private static List<RendererParamInfo>? BuildParamInfos(ParameterInfo[] methodParams)
    {
        var infos = new List<RendererParamInfo>(methodParams.Length);
        foreach (var p in methodParams)
        {
            var attr = p.GetCustomAttribute<RendererParamAttribute>();
            if (attr == null)
            {
                Console.Error.WriteLine(
                    $"Warning: Renderer parameter '{p.Name}' is missing [RendererParam] — skipping method");
                return null;
            }
            infos.Add(new RendererParamInfo(attr.Key, attr.Description, attr.Kind, attr.Default));
        }
        return infos;
    }

    private static RenderFunc InvokeParameterized(
        MethodInfo method,
        ParameterInfo[] methodParams,
        IReadOnlyList<RendererParamInfo> paramInfos,
        Dictionary<string, JsonElement>? configParams)
    {
        var args = new object?[methodParams.Length];
        for (int i = 0; i < methodParams.Length; i++)
        {
            var info = paramInfos[i];
            string? raw = null;

            if (configParams != null &&
                configParams.TryGetValue(info.Key, out var elem))
            {
                raw = elem.ValueKind == JsonValueKind.String
                    ? elem.GetString()
                    : elem.GetRawText();
            }

            raw ??= info.Default;

            args[i] = ConvertParam(info.Kind, raw, info.Key);
        }

        return (RenderFunc)method.Invoke(null, args)!;
    }

    private static object? ConvertParam(RendererParamKind kind, string? raw, string key)
    {
        switch (kind)
        {
            case RendererParamKind.Numeric:
                if (raw != null && float.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    return f;
                Console.Error.WriteLine($"Warning: Renderer param '{key}' — expected numeric, got '{raw}', using 0");
                return 0f;

            case RendererParamKind.Text:
                return raw ?? "";

            case RendererParamKind.Boolean:
                return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);

            case RendererParamKind.Bitmap:
                if (string.IsNullOrEmpty(raw))
                {
                    Console.Error.WriteLine($"Warning: Renderer param '{key}' — no image path specified, using empty bitmap");
                    return new SKBitmap(1, 1);
                }
                try
                {
                    var bitmap = SKBitmap.Decode(raw);
                    if (bitmap != null) return bitmap;
                    Console.Error.WriteLine($"Warning: Renderer param '{key}' — failed to decode '{raw}', using empty bitmap");
                    return new SKBitmap(1, 1);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Renderer param '{key}' — error loading '{raw}': {ex.Message}");
                    return new SKBitmap(1, 1);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, $"Unknown param kind for '{key}'");
        }
    }
}
