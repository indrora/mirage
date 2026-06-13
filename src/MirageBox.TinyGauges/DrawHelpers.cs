using SkiaSharp;

namespace MirageBox.TinyGauges;

public static class DrawHelpers
{
    public static void DrawText(SKCanvas canvas, SKRect bounds, Theme theme, SKTypeface typeface, string? label, RangedValue value)
    {
        // SkiaSharp 3.x split text state off SKPaint: font (typeface + size) now
        // lives on SKFont, while SKPaint keeps only color/antialias. DrawText takes
        // an explicit SKTextAlign, so we anchor at bounds.MidX with Center alignment
        // instead of measuring the string and offsetting by half its width.
        string valueText = value.ValueClamped.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        using var valuePaint = new SKPaint { IsAntialias = true, Color = theme.TextColor };
        using var valueFont = new SKFont { Typeface = typeface ?? SKTypeface.Default, Size = MathF.Max(10f, bounds.Height * 0.2f) };

        canvas.DrawText(valueText, bounds.MidX, bounds.Bottom - bounds.Height * 0.16f, SKTextAlign.Center, valueFont, valuePaint);

        if (!string.IsNullOrWhiteSpace(label))
        {
            using var labelPaint = new SKPaint { IsAntialias = true, Color = theme.TextColor.WithAlpha(190) };
            using var labelFont = new SKFont { Typeface = typeface ?? SKTypeface.Default, Size = MathF.Max(8f, bounds.Height * 0.12f) };

            canvas.DrawText(label, bounds.MidX, bounds.Top + bounds.Height * 0.22f, SKTextAlign.Center, labelFont, labelPaint);
        }
    }

    public static SKColor FunTrack(Theme t) => t.SecondaryColor.WithAlpha(80);
    public static SKColor FunInfo(Theme t) => t.PrimaryColor;
    public static SKColor FunOk(Theme t) => new SKColor(0x4C, 0xAF, 0x50);
    public static SKColor FunWarn(Theme t) => new SKColor(0xFF, 0xC1, 0x07);
    public static SKColor FunCrit(Theme t) => new SKColor(0xF4, 0x43, 0x36);

    public static SKColor FunLevel(Theme t, float fraction) =>
        fraction < 0.25f ? FunCrit(t) : fraction < 0.5f ? FunWarn(t) : FunOk(t);

    public static (float x, float y) PolartoXY(float cx, float cy, float r, float deg)
    {
        float rad = deg * MathF.PI / 180f;
        return (cx + MathF.Cos(rad) * r, cy + MathF.Sin(rad) * r);
    }

    public static string FunNum(float v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static string FunShort(double v) =>
        ((float)v).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    public static SKPaint FunFill(SKColor color) =>
        new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };

    public static SKPaint FunStroke(SKColor color, float width) =>
        new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = color };

    public static void FunArc(SKCanvas c, SKRect rect, float startAngle, float sweepDeg, float fraction, float sw, SKColor track, SKColor val)
    {
        using var tp = FunStroke(track, sw);
        c.DrawArc(rect, startAngle, sweepDeg, false, tp);
        using var vp = FunStroke(val, sw);
        c.DrawArc(rect, startAngle, sweepDeg * fraction, false, vp);
    }

    public static void FunTextCentered(SKCanvas c, string text, float cx, float cy, float size, SKColor color, SKTypeface? tf)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color };
        using var font = new SKFont { Typeface = tf ?? SKTypeface.Default, Size = size };
        c.DrawText(text, cx, cy, SKTextAlign.Center, font, p);
    }

    public static void FunText(SKCanvas c, string text, float cx, float cy, float size, SKColor color, SKTypeface? tf, SKTextAlign align = SKTextAlign.Center)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color };
        using var font = new SKFont { Typeface = tf ?? SKTypeface.Default, Size = size };
        c.DrawText(text, cx, cy, align, font, p);
    }

    public static void FunRect(SKCanvas c, float x, float y, float w, float h, float radius, SKColor color)
    {
        using var p = FunFill(color);
        c.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), radius, radius), p);
    }

    public static void FunStrokeRect(SKCanvas c, float x, float y, float w, float h, float radius, SKColor color, float sw)
    {
        using var p = FunStroke(color, sw);
        c.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), radius, radius), p);
    }

    public static void FunLine(SKCanvas c, float x1, float y1, float x2, float y2, SKColor color, float width)
    {
        using var p = FunStroke(color, width);
        c.DrawLine(x1, y1, x2, y2, p);
    }

    public static void In64(SKCanvas canvas, SKRect bounds, Theme theme, Action<SKCanvas> draw)
    {
        canvas.Clear(theme.BackgroundColor);
        canvas.Save();
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.Scale(bounds.Width / 64f, bounds.Height / 64f);
        canvas.ClipRect(new SKRect(0, 0, 64, 64));
        draw(canvas);
        canvas.Restore();
    }
}