using SkiaSharp;

namespace MirageBox.TinyGauges;

public interface ITinyGauge
{
    int Width { get; }
    int Height { get; }
    float MinValue { get; }
    float MaxValue { get; }
    float Value { get; }
    string? Label { get; }

    ITinyGauge SetSize(int width, int height);
    ITinyGauge SetRange(float minValue, float maxValue);
    ITinyGauge SetValue(float value);
    ITinyGauge SetLabel(string? label);

    ITinyGauge SetTypeface(SKTypeface? typeface);
    ITinyGauge SetPrimaryColor(SKColor color);
    ITinyGauge SetSecondaryColor(SKColor color);
    ITinyGauge SetBackgroundColor(SKColor color);
    ITinyGauge SetTextColor(SKColor color);

    SKBitmap RenderBitmap();
    byte[] RenderJpeg(int quality = 90);
}
