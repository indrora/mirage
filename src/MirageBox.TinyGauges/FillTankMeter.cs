using SkiaSharp;

namespace MirageBox.TinyGauges;

public sealed class TankFillMeter : TinyGuageBase
{
    protected override void Render(SKCanvas canvas, SKRect bounds)
    {
        float pad = MathF.Max(3f, MathF.Min(bounds.Width, bounds.Height) * 0.1f);
        var tank = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad * 1.4f);

        using var tankPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2f, bounds.Width * 0.05f),
            Color = SecondaryColor
        };
        canvas.DrawRoundRect(tank, 5f, 5f, tankPaint);

        float fillHeight = tank.Height * ClampedRatio;
        var fillRect = new SKRect(tank.Left + 2f, tank.Bottom - fillHeight + 2f, tank.Right - 2f, tank.Bottom - 2f);
        if (fillRect.Bottom > fillRect.Top)
        {
            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = PrimaryColor
            };
            canvas.DrawRoundRect(fillRect, 3f, 3f, fillPaint);
        }

        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = TextColor,
            Typeface = Typeface,
            TextSize = MathF.Max(9f, bounds.Height * 0.18f)
        };

        string text = $"{MathF.Round(ClampedRatio * 100f)}%";
        float textWidth = valuePaint.MeasureText(text);
        canvas.DrawText(text, bounds.MidX - textWidth / 2f, bounds.Bottom - 2f, valuePaint);

        if (!string.IsNullOrWhiteSpace(Label))
        {
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                Color = TextColor.WithAlpha(190),
                Typeface = Typeface,
                TextSize = MathF.Max(7f, bounds.Height * 0.12f)
            };
            float labelWidth = labelPaint.MeasureText(Label);
            canvas.DrawText(Label, bounds.MidX - labelWidth / 2f, bounds.Top + 10f, labelPaint);
        }
    }
}
