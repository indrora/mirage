using SkiaSharp;

namespace MirageBox.TinyGauges;

public sealed class BarMeter : TinyGuageBase
{
    public bool Vertical { get; set; }

    protected override void Render(SKCanvas canvas, SKRect bounds)
    {
        float pad = MathF.Max(4f, MathF.Min(bounds.Width, bounds.Height) * 0.1f);
        var bar = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad * 1.6f);

        using var track = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = SecondaryColor
        };
        canvas.DrawRoundRect(bar, 4f, 4f, track);

        var fill = bar;
        if (Vertical)
        {
            fill.Top = bar.Bottom - bar.Height * ClampedRatio;
        }
        else
        {
            fill.Right = bar.Left + bar.Width * ClampedRatio;
        }

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = PrimaryColor
        };
        canvas.DrawRoundRect(fill, 4f, 4f, fillPaint);

        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = TextColor,
            Typeface = Typeface,
            TextSize = MathF.Max(8f, bounds.Height * 0.14f)
        };

        string text = ValueText;
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
