using SkiaSharp;

namespace MirageBox.TinyGauges;

public static partial class Styles
{
    [GaugeRenderer("Radial")]
    public static RenderFunc Radial(float startAngle, float sweepAngle) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            canvas.Clear(theme.BackgroundColor);

            float pad = MathF.Max(3f, MathF.Min(bounds.Width, bounds.Height) * 0.12f);
            var arcRect = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad);
            float stroke = MathF.Max(2f, MathF.Min(bounds.Width, bounds.Height) * 0.1f);

            using var trackPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = stroke,
                StrokeCap = SKStrokeCap.Round,
                Color = theme.SecondaryColor
            };
            canvas.DrawArc(arcRect, startAngle, sweepAngle, false, trackPaint);

            using var valuePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = stroke,
                StrokeCap = SKStrokeCap.Round,
                Color = theme.PrimaryColor
            };
            canvas.DrawArc(arcRect, startAngle, sweepAngle * value.Ratio, false, valuePaint);

            var center = new SKPoint(bounds.MidX, bounds.MidY);
            float needleAngle = (startAngle + sweepAngle * value.Ratio) * (MathF.PI / 180f);
            float needleLen = MathF.Min(bounds.Width, bounds.Height) * 0.28f;
            var tip = new SKPoint(center.X + MathF.Cos(needleAngle) * needleLen, center.Y + MathF.Sin(needleAngle) * needleLen);

            using var needlePaint = new SKPaint
            {
                IsAntialias = true,
                StrokeWidth = MathF.Max(1.5f, stroke * 0.38f),
                StrokeCap = SKStrokeCap.Round,
                Color = theme.TextColor
            };
            canvas.DrawLine(center, tip, needlePaint);

            using var hubPaint = new SKPaint { IsAntialias = true, Color = theme.PrimaryColor };
            canvas.DrawCircle(center, MathF.Max(2f, stroke * 0.38f), hubPaint);

            DrawText(canvas, bounds, theme, typeface, label, value);


        };

    private static void DrawText(SKCanvas canvas, SKRect bounds, Theme theme, SKTypeface typeface, string? label, RangedValue value)
    {
        string valueText = value.ValueClamped.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = theme.TextColor,
            Typeface = typeface,
            TextSize = MathF.Max(10f, bounds.Height * 0.2f)
        };

        float textWidth = valuePaint.MeasureText(valueText);
        canvas.DrawText(valueText, bounds.MidX - textWidth / 2f, bounds.Bottom - bounds.Height * 0.16f, valuePaint);

        if (!string.IsNullOrWhiteSpace(label))
        {
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                Color = theme.TextColor.WithAlpha(190),
                Typeface = typeface,
                TextSize = MathF.Max(8f, bounds.Height * 0.12f)
            };

            float labelWidth = labelPaint.MeasureText(label);
            canvas.DrawText(label, bounds.MidX - labelWidth / 2f, bounds.Top + bounds.Height * 0.22f, labelPaint);
        }
    }


    [GaugeRenderer("Bar")]
    public static RenderFunc Bar() => (canvas, theme, typeface, bounds, label, value) =>
    {
        canvas.Clear(theme.BackgroundColor);

        float pad = MathF.Max(4f, MathF.Min(bounds.Width, bounds.Height) * 0.1f);
        var bar = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad * 1.6f);

        using var track = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = theme.SecondaryColor
        };
        canvas.DrawRoundRect(bar, 4f, 4f, track);

        var fill = bar;
        fill.Right = bar.Left + bar.Width * value.Ratio;

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = theme.PrimaryColor
        };
        canvas.DrawRoundRect(fill, 4f, 4f, fillPaint);
    };

    [GaugeRenderer("TankFill")]
    public static RenderFunc TankFill() => (canvas, theme, typeface, bounds, label, value) =>
    {
        canvas.Clear(theme.BackgroundColor);

        float pad = MathF.Max(3f, MathF.Min(bounds.Width, bounds.Height) * 0.1f);
        var tank = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad * 1.4f);

        using var tankPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2f, bounds.Width * 0.05f),
            Color = theme.SecondaryColor
        };
        canvas.DrawRoundRect(tank, 5f, 5f, tankPaint);

        float fillHeight = tank.Height * value.Ratio;
        var fillRect = new SKRect(tank.Left + 2f, tank.Bottom - fillHeight + 2f, tank.Right - 2f, tank.Bottom - 2f);
        if (fillRect.Bottom > fillRect.Top)
        {
            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = theme.PrimaryColor
            };
            canvas.DrawRoundRect(fillRect, 3f, 3f, fillPaint);
        }
    };

    [GaugeRenderer("Numeric")]
    public static RenderFunc Numeric() => (canvas, theme, typeface, bounds, label, value) =>
    {
        canvas.Clear(theme.BackgroundColor);

        using var boxPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2f, bounds.Width * 0.04f),
            Color = theme.SecondaryColor
        };
        canvas.DrawRoundRect(bounds.Left + 2, bounds.Top + 2, bounds.Width - 4, bounds.Height - 4, 8, 8, boxPaint);

        string valueText = value.ValueClamped.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = theme.PrimaryColor,
            Typeface = typeface,
            TextSize = MathF.Max(12f, bounds.Height * 0.42f)
        };

        float textWidth = valuePaint.MeasureText(valueText);
        canvas.DrawText(valueText, bounds.MidX - textWidth / 2f, bounds.MidY + bounds.Height * 0.15f, valuePaint);

        if (!string.IsNullOrWhiteSpace(label))
        {
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                Color = theme.TextColor.WithAlpha(200),
                Typeface = typeface,
                TextSize = MathF.Max(7f, bounds.Height * 0.14f)
            };
            float labelWidth = labelPaint.MeasureText(label);
            canvas.DrawText(label, bounds.MidX - labelWidth / 2f, bounds.Top + bounds.Height * 0.2f, labelPaint);
        }
    };

}