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
        Value = Math.Clamp(value, Min, Max);
    }
    public float ValueClamped => Math.Clamp(Value, Min, Max);
    public float Ratio => (ValueClamped - Min) / (Max - Min);
}

public delegate void RenderFunc(SKCanvas canvas, Theme theme, SKTypeface typeface, SKRect bounds, string? label, RangedValue value);

public record struct GaugeConfig(
    RangedValue Value,
    string? Label,
    SKTypeface Typeface,
    Theme Theme,
    RenderFunc Renderer
);
