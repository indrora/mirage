using SkiaSharp;

namespace MirageBox.TinyGauges;

public sealed class NumericMeter : TinyGuageBase
{
    protected override void Render(SKCanvas canvas, SKRect bounds)
    {
        using var boxPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2f, bounds.Width * 0.04f),
            Color = SecondaryColor
        };
        canvas.DrawRoundRect(bounds.Left + 2, bounds.Top + 2, bounds.Width - 4, bounds.Height - 4, 8, 8, boxPaint);

        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = PrimaryColor,
            Typeface = Typeface,
            TextSize = MathF.Max(12f, bounds.Height * 0.42f)
        };

        string text = ValueText;
        float textWidth = valuePaint.MeasureText(text);
        canvas.DrawText(text, bounds.MidX - textWidth / 2f, bounds.MidY + bounds.Height * 0.15f, valuePaint);

        if (!string.IsNullOrWhiteSpace(Label))
        {
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                Color = TextColor.WithAlpha(200),
                Typeface = Typeface,
                TextSize = MathF.Max(7f, bounds.Height * 0.14f)
            };
            float labelWidth = labelPaint.MeasureText(Label);
            canvas.DrawText(Label, bounds.MidX - labelWidth / 2f, bounds.Top + bounds.Height * 0.2f, labelPaint);
        }
    }
}
