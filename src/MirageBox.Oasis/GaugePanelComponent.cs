using SkiaSharp;
using System.Globalization;

namespace MirageBox.Oasis;

public sealed class GaugePanelComponent
{
    public int ImageSize { get; set; } = 64;
    public int PanelNumber { get; set; }
    public int Value { get; set; }
    public bool IsSelected { get; set; }
    public float HueOffset { get; set; }

    public float MinGaugeValue { get; set; } = -100f;
    public float MaxGaugeValue { get; set; } = 100f;
    public float StartAngle { get; set; } = 135f;
    public float SweepAngle { get; set; } = 270f;
    public bool RotateClockwise90 { get; set; } = true;

    public SKTypeface? Typeface { get; set; }

    public byte[] RenderJpeg(int quality = 100)
    {
        using var bitmap = RenderBitmap();
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded.ToArray();
    }

    public SKBitmap RenderBitmap()
    {
        var bitmap = new SKBitmap(ImageSize, ImageSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(new SKColor(12, 14, 18));

        using (var bgPaint = new SKPaint
        {
            Color = new SKColor(24, 28, 36),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        })
        {
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(1, 1, ImageSize - 1, ImageSize - 1), 8, 8), bgPaint);
        }

        var accent = SKColor.FromHsv(HueOffset % 360f, 85f, 100f);
        float normalized = Math.Clamp((Value - MinGaugeValue) / (MaxGaugeValue - MinGaugeValue), 0f, 1f);
        float valueAngle = StartAngle + SweepAngle * normalized;
        var gaugeRect = new SKRect(9, 10, ImageSize - 9, ImageSize - 8);

        using (var trackPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5.5f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(70, 78, 94)
        })
        {
            canvas.DrawArc(gaugeRect, StartAngle, SweepAngle, false, trackPaint);
        }

        using (var valuePaintArc = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5.5f,
            StrokeCap = SKStrokeCap.Round,
            Color = accent
        })
        {
            canvas.DrawArc(gaugeRect, StartAngle, SweepAngle * normalized, false, valuePaintArc);
        }

        using (var tickPaint = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 1.6f,
            Color = new SKColor(210, 215, 230, 175)
        })
        {
            var center = new SKPoint(ImageSize / 2f, ImageSize / 2f + 3);
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                float a = (StartAngle + SweepAngle * t) * (float)Math.PI / 180f;
                float inner = 20f;
                float outer = 23.5f;
                var p1 = new SKPoint(center.X + inner * MathF.Cos(a), center.Y + inner * MathF.Sin(a));
                var p2 = new SKPoint(center.X + outer * MathF.Cos(a), center.Y + outer * MathF.Sin(a));
                canvas.DrawLine(p1, p2, tickPaint);
            }
        }

        using (var needlePaint = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 2.4f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(248, 248, 255)
        })
        {
            var center = new SKPoint(ImageSize / 2f, ImageSize / 2f + 3);
            float a = valueAngle * (float)Math.PI / 180f;
            var tip = new SKPoint(center.X + 18f * MathF.Cos(a), center.Y + 18f * MathF.Sin(a));
            canvas.DrawLine(center, tip, needlePaint);

            using var hubPaint = new SKPaint { IsAntialias = true, Color = accent };
            canvas.DrawCircle(center, 2.7f, hubPaint);
        }

        if (IsSelected)
        {
            using var borderPaint = new SKPaint
            {
                Color = new SKColor(accent.Red, accent.Green, accent.Blue, 235),
                IsStroke = true,
                StrokeWidth = 3f,
                IsAntialias = true
            };
            canvas.DrawRect(new SKRect(1.5f, 1.5f, ImageSize - 1.5f, ImageSize - 1.5f), borderPaint);
        }

        string panelText = PanelNumber.ToString(CultureInfo.InvariantCulture);
        string valueText = Value.ToString(CultureInfo.InvariantCulture);

        using var panelPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 220),
            IsAntialias = true,
            Typeface = Typeface,
            TextSize = 11f
        };

        using var valuePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 245),
            IsAntialias = true,
            Typeface = Typeface,
            TextSize = 13f
        };

        canvas.DrawText($"P{panelText}", 4f, 11f, panelPaint);
        float valueWidth = valuePaint.MeasureText(valueText);
        float valueX = (ImageSize - valueWidth) / 2f;
        float valueY = 61f;
        canvas.DrawText(valueText, valueX, valueY, valuePaint);

        if (!RotateClockwise90)
        {
            return bitmap;
        }

        var rotated = RotateBitmap90Clockwise(bitmap);
        bitmap.Dispose();
        return rotated;
    }

    public void AdjustValue(int delta, int min = -999, int max = 9999)
    {
        Value = Math.Clamp(Value + delta, min, max);
    }

    private static SKBitmap RotateBitmap90Clockwise(SKBitmap source)
    {
        var rotated = new SKBitmap(source.Height, source.Width, source.ColorType, source.AlphaType);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int dstX = source.Height - 1 - y;
                int dstY = x;
                rotated.SetPixel(dstX, dstY, source.GetPixel(x, y));
            }
        }

        return rotated;
    }
}
