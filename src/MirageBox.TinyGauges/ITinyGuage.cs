
using System.Numerics;
using SkiaSharp;

namespace MirageBox.TinyGauges;

public record struct RangedValue
{
    public float Min { get; init; }
    public float Max { get; init; }
    public float Value { get; init; }

    public RangedValue(float min, float max, float value)
    {
        if (max <= min)
            throw new ArgumentException("max must be greater than min.");

        Min = min;
        Max = max;
        Value = value < Min ? Min : value > Max ? Max : value;
    }
    public float ValueClamped => Value < Min ? Min : Value > Max ? Max : Value;
    public float Ratio => (ValueClamped - Min) / (Max - Min);

    public string Formatted(string format) => ValueClamped.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
}

public delegate void RenderFunc(SKCanvas canvas, Theme theme, SKTypeface typeface, SKRect bounds, string? label, RangedValue value);

public record struct GaugeConfig {

    public RangedValue Value;
    public string? Label;
    public SKTypeface Typeface;
    public Theme Theme;
    public RenderFunc Renderer;
}
