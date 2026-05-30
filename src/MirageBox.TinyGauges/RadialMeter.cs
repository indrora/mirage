using SkiaSharp;

namespace MirageBox.TinyGauges;

public sealed class RadialMeter : TinyGuageBase
{
    public float StartAngle { get; set; } = 135f;
    public float SweepAngle { get; set; } = 270f;

    protected override void Render(SKCanvas canvas, SKRect bounds)
    {
        float pad = MathF.Max(3f, MathF.Min(bounds.Width, bounds.Height) * 0.12f);
        var arcRect = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad);
        float stroke = MathF.Max(2f, MathF.Min(bounds.Width, bounds.Height) * 0.1f);

        using var trackPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
            Color = SecondaryColor
        };
        canvas.DrawArc(arcRect, StartAngle, SweepAngle, false, trackPaint);

        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
            Color = PrimaryColor
        };
        canvas.DrawArc(arcRect, StartAngle, SweepAngle * ClampedRatio, false, valuePaint);

        var center = new SKPoint(bounds.MidX, bounds.MidY);
        float needleAngle = (StartAngle + SweepAngle * ClampedRatio) * (MathF.PI / 180f);
        float needleLen = MathF.Min(bounds.Width, bounds.Height) * 0.28f;
        var tip = new SKPoint(center.X + MathF.Cos(needleAngle) * needleLen, center.Y + MathF.Sin(needleAngle) * needleLen);

        using var needlePaint = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = MathF.Max(1.5f, stroke * 0.38f),
            StrokeCap = SKStrokeCap.Round,
            Color = TextColor
        };
        canvas.DrawLine(center, tip, needlePaint);

        using var hubPaint = new SKPaint { IsAntialias = true, Color = PrimaryColor };
        canvas.DrawCircle(center, MathF.Max(2f, stroke * 0.38f), hubPaint);

        DrawText(canvas, bounds);
    }

    private void DrawText(SKCanvas canvas, SKRect bounds)
    {
        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = TextColor,
            Typeface = Typeface,
            TextSize = MathF.Max(10f, bounds.Height * 0.2f)
        };

        string text = ValueText;
        float textWidth = valuePaint.MeasureText(text);
        canvas.DrawText(text, bounds.MidX - textWidth / 2f, bounds.Bottom - bounds.Height * 0.16f, valuePaint);

        if (!string.IsNullOrWhiteSpace(Label))
        {
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                Color = TextColor.WithAlpha(190),
                Typeface = Typeface,
                TextSize = MathF.Max(8f, bounds.Height * 0.12f)
            };

            float labelWidth = labelPaint.MeasureText(Label);
            canvas.DrawText(Label, bounds.MidX - labelWidth / 2f, bounds.Top + bounds.Height * 0.22f, labelPaint);
        }
    }
}
