using System.ComponentModel;

namespace MirageBox.TinyGauges;

public enum GaugeValueType { Numeric, Text }

/// <summary>
/// The kind of value a renderer parameter expects.
/// Used by the registry to convert config strings into the CLR type the factory method needs.
/// </summary>
public enum RendererParamKind
{
    
    /// <summary>A <see cref="float"/> value.</summary>
    Numeric,

    /// <summary>A plain <see cref="string"/>.</summary>
    Text,

    /// <summary>A file path that is loaded as an <see cref="SkiaSharp.SKBitmap"/>.</summary>
    Bitmap,
    
    /// <summary>
    /// A boolean value (true/false)
    /// </summary>
    Boolean,
}

/// <summary>
/// Marks a static method that returns a <see cref="RenderFunc"/> as a gauge renderer.
/// The method may be parameterless or may have parameters annotated with
/// <see cref="RendererParamAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class GaugeRendererAttribute : Attribute
{
    public string Name { get; }
    public GaugeValueType ValueType { get; }

    public GaugeRendererAttribute(string name, GaugeValueType valueType = GaugeValueType.Numeric)
    {
        Name = name;
        ValueType = valueType;
    }
}

/// <summary>
/// Annotates a parameter on a <see cref="GaugeRendererAttribute"/>-tagged method with
/// metadata the registry uses to map config values to CLR arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class RendererParamAttribute : Attribute
{
    /// <summary>Config key name (matched against <c>RendererConfig.Parameters</c>).</summary>
    public string Key { get; }

    /// <summary>Human-readable description shown in the UI.</summary>
    public string Description { get; }

    /// <summary>What kind of value this parameter expects.</summary>
    public RendererParamKind Kind { get; }

    /// <summary>Optional default value as a string (parsed via the same converter).</summary>
    public string? Default { get; set; }

    public RendererParamAttribute(string key, string description, RendererParamKind kind)
    {
        Key = key;
        Description = description;
        Kind = kind;
    }
}
