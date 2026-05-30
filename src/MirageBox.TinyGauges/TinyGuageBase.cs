using SkiaSharp;
using System.Globalization;

namespace MirageBox.TinyGauges;

public abstract class TinyGuageBase : ITinyGauge
{
    public int Width { get; private set; } = 95;
    public int Height { get; private set; } = 95;

    public float MinValue { get; private set; } = 0;
    public float MaxValue { get; private set; } = 100;
    public float Value { get; private set; } = 0;
    public string? Label { get; private set; }

    protected SKTypeface? Typeface { get; private set; }
    protected SKColor PrimaryColor { get; private set; } = new(79, 146, 255);
    protected SKColor SecondaryColor { get; private set; } = new(66, 72, 88);
    protected SKColor BackgroundColor { get; private set; } = new(18, 22, 30);
    protected SKColor TextColor { get; private set; } = new(243, 246, 255);

    public ITinyGauge SetSize(int width, int height)
    {
        Width = Math.Max(8, width);
        Height = Math.Max(8, height);
        return this;
    }

    public ITinyGauge SetRange(float minValue, float maxValue)
    {
        if (maxValue <= minValue)
            throw new ArgumentException("maxValue must be greater than minValue.");

        MinValue = minValue;
        MaxValue = maxValue;
        Value = Math.Clamp(Value, MinValue, MaxValue);
        return this;
    }

    public ITinyGauge SetValue(float value)
    {
        Value = Math.Clamp(value, MinValue, MaxValue);
        return this;
    }

    public ITinyGauge SetLabel(string? label)
    {
        Label = label;
        return this;
    }

    public ITinyGauge SetTypeface(SKTypeface? typeface)
    {
        Typeface = typeface;
        return this;
    }

    public ITinyGauge SetPrimaryColor(SKColor color)
    {
        PrimaryColor = color;
        return this;
    }

    public ITinyGauge SetSecondaryColor(SKColor color)
    {
        SecondaryColor = color;
        return this;
    }

    public ITinyGauge SetBackgroundColor(SKColor color)
    {
        BackgroundColor = color;
        return this;
    }

    public ITinyGauge SetTextColor(SKColor color)
    {
        TextColor = color;
        return this;
    }

    public SKBitmap RenderBitmap()
    {
        var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(BackgroundColor);

        var bounds = new SKRect(0, 0, Width, Height);
        Render(canvas, bounds);

        return bitmap;
    }

    public byte[] RenderJpeg(int quality = 90)
    {
        using var bitmap = RenderBitmap();
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 0, 100));
        return encoded.ToArray();
    }

    protected float ValueRatio => (Value - MinValue) / (MaxValue - MinValue);
    protected float ClampedRatio => Math.Clamp(ValueRatio, 0f, 1f);
    protected string ValueText => Value.ToString("0.##", CultureInfo.InvariantCulture);

    protected abstract void Render(SKCanvas canvas, SKRect bounds);
}
