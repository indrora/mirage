using System.Numerics;
using SkiaSharp;

namespace MirageBox.TinyGauges;

// All gauges author geometry in a 64x64 space scaled to bounds at render time.
// value.Ratio drives geometry; value.ValueClamped drives the numeral; label is the caption.

public static partial class Styles
{
    // ── colour helpers (map old theme methods to current Theme properties) ─────────

    private static SKColor FunTrack(Theme t) => t.SecondaryColor.WithAlpha(80);
    private static SKColor FunInfo(Theme t) => t.PrimaryColor;
    private static SKColor FunOk(Theme t) => new SKColor(0x4C, 0xAF, 0x50);
    private static SKColor FunWarn(Theme t) => new SKColor(0xFF, 0xC1, 0x07);
    private static SKColor FunCrit(Theme t) => new SKColor(0xF4, 0x43, 0x36);
    private static SKColor FunLevel(Theme t, float fraction) =>
        fraction < 0.25f ? FunCrit(t) : fraction < 0.5f ? FunWarn(t) : FunOk(t);

    // ── geometry helpers (operate in 64×64 space) ─────────────────────────────────

    private static (float x, float y) FunPolar(float cx, float cy, float r, float deg)
    {
        float rad = deg * MathF.PI / 180f;
        return (cx + MathF.Cos(rad) * r, cy + MathF.Sin(rad) * r);
    }

    private static string FunNum(float v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string FunShort(double v) =>
        ((float)v).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static SKPaint FunFill(SKColor color) =>
        new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };

    private static SKPaint FunStroke(SKColor color, float width) =>
        new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = color };

    private static void FunArc(SKCanvas c, SKRect rect, float startAngle, float sweepDeg, float fraction, float sw, SKColor track, SKColor val)
    {
        using var tp = FunStroke(track, sw);
        c.DrawArc(rect, startAngle, sweepDeg, false, tp);
        using var vp = FunStroke(val, sw);
        c.DrawArc(rect, startAngle, sweepDeg * fraction, false, vp);
    }

    private static void FunTextCentered(SKCanvas c, string text, float cx, float cy, float size, SKColor color, SKTypeface? tf)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color, Typeface = tf, TextSize = size, TextAlign = SKTextAlign.Center };
        c.DrawText(text, cx, cy, p);
    }

    private static void FunText(SKCanvas c, string text, float cx, float cy, float size, SKColor color, SKTypeface? tf, SKTextAlign align = SKTextAlign.Center)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color, Typeface = tf, TextSize = size, TextAlign = align };
        c.DrawText(text, cx, cy, p);
    }

    private static void FunRect(SKCanvas c, float x, float y, float w, float h, float radius, SKColor color)
    {
        using var p = FunFill(color);
        c.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), radius, radius), p);
    }

    private static void FunStrokeRect(SKCanvas c, float x, float y, float w, float h, float radius, SKColor color, float sw)
    {
        using var p = FunStroke(color, sw);
        c.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), radius, radius), p);
    }

    private static void FunLine(SKCanvas c, float x1, float y1, float x2, float y2, SKColor color, float width)
    {
        using var p = FunStroke(color, width);
        c.DrawLine(x1, y1, x2, y2, p);
    }

    // Sets up background + a 64×64 → bounds scaling transform, then restores.
    private static void In64(SKCanvas canvas, SKRect bounds, Theme theme, Action<SKCanvas> draw)
    {
        canvas.Clear(theme.BackgroundColor);
        canvas.Save();
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.Scale(bounds.Width / 64f, bounds.Height / 64f);
        canvas.ClipRect(new SKRect(0, 0, 64, 64));
        draw(canvas);
        canvas.Restore();
    }

    // ── RenderFunc implementations ────────────────────────────────────────────────

    /// <summary>Edge-to-edge circular progress ring with a large centered readout.</summary>
    public static RenderFunc FullRing() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            FunArc(c, new SKRect(6, 6, 58, 58), -90, 360, value.Ratio, 7, FunTrack(theme), FunInfo(theme));
            FunTextCentered(c, FunNum(value.ValueClamped), 32, label is null ? 32 : 28, 22, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label))
                FunText(c, label, 32, 50, 11, theme.SecondaryColor, tf);
        });
    };

    /// <summary>Progress traced around the rounded keycap perimeter; center stays free for a number.</summary>
    public static RenderFunc Perimeter() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            using var path = new SKPath();
            path.AddRoundRect(new SKRoundRect(new SKRect(5, 5, 59, 59), 10, 10));

            using (var tp = FunStroke(FunTrack(theme), 5))
                c.DrawPath(path, tp);

            using var measure = new SKPathMeasure(path, false);
            float len = measure.Length;
            using var seg = new SKPath();
            if (measure.GetSegment(-90, len * value.Ratio, seg, true))
            {
                using var vp = FunStroke(FunOk(theme), 5);
                c.DrawPath(seg, vp);
            }

            FunTextCentered(c, FunNum(value.ValueClamped), 32, label is null ? 32 : 28, 22, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label))
                FunText(c, label, 32, 50, 11, theme.SecondaryColor, tf);
        });
    };

    /// <summary>Full-tile liquid fill rising from the bottom, with a bright surface line.</summary>
    public static RenderFunc LiquidTank() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            float h = value.Ratio * 64f;
            using (new SKAutoCanvasRestore(c))
            {
                c.ClipRoundRect(new SKRoundRect(new SKRect(0.5f, 0.5f, 63.5f, 63.5f), 10, 10), antialias: true);
                using var body = FunFill(FunInfo(theme).WithAlpha(140));
                c.DrawRect(0, 64 - h, 64, h, body);
                using var surface = FunFill(FunInfo(theme));
                c.DrawRect(0, 64 - h, 64, 2, surface);
            }
            FunTextCentered(c, FunNum(value.ValueClamped), 32, 28, 20, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label))
                FunText(c, label, 32, 46, 11, theme.TextColor, tf);
        });
    };

    /// <summary>270-degree needle dial with a graduated tick ring.</summary>
    public static RenderFunc BigDial() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            foreach (float f in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                float ang = 135 + f * 270;
                var (ox, oy) = FunPolar(32, 32, 27, ang);
                var (ix, iy) = FunPolar(32, 32, 23, ang);
                FunLine(c, ox, oy, ix, iy, theme.SecondaryColor, 1.5f);
            }
            FunArc(c, new SKRect(8, 8, 56, 56), 135, 270, value.Ratio, 3, FunTrack(theme), FunInfo(theme));

            float na = 135 + value.Ratio * 270;
            var (nx, ny) = FunPolar(32, 32, 22, na);
            FunLine(c, 32, 32, nx, ny, theme.TextColor, 2.5f);
            using (var hub = FunFill(theme.TextColor))
                c.DrawCircle(32, 32, 3, hub);

            string text = string.IsNullOrEmpty(label) ? FunNum(value.ValueClamped) : label;
            FunText(c, text, 32, 47, 11, string.IsNullOrEmpty(label) ? theme.TextColor : theme.SecondaryColor, tf);
        });
    };

    /// <summary>Big numeral over a full-width progress bar pinned to the bottom edge.</summary>
    public static RenderFunc NumberBar() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            if (!string.IsNullOrEmpty(label))
                FunText(c, label, 32, 13, 11, theme.SecondaryColor, tf);
            FunTextCentered(c, FunNum(value.ValueClamped), 32, 34, 24, theme.TextColor, tf);
            FunRect(c, 4, 52, 56, 7, 3, FunTrack(theme));
            FunRect(c, 4, 52, value.Ratio * 56, 7, 3, FunInfo(theme));
        });
    };

    /// <summary>Segmented LED ring (12 radial segments) lit proportionally, color-zoned.</summary>
    public static RenderFunc LedRing() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            int lit = (int)MathF.Round(value.Ratio * 12);
            for (int i = 0; i < 12; i++)
            {
                float ang = -90 + i * 30;
                var (ox, oy) = FunPolar(32, 32, 29, ang);
                var (ix, iy) = FunPolar(32, 32, 22, ang);
                SKColor col = i < lit
                    ? (i < 7 ? FunOk(theme) : i < 10 ? FunWarn(theme) : FunCrit(theme))
                    : FunTrack(theme);
                FunLine(c, ox, oy, ix, iy, col, 3);
            }
            FunTextCentered(c, FunNum(value.ValueClamped), 32, label is null ? 32 : 30, 15, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label))
                FunText(c, label, 32, 47, 11, theme.SecondaryColor, tf);
        });
    };

    /// <summary>Numeral plus a labeled horizontal scale (min / max from the range).</summary>
    public static RenderFunc ValueScale() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            if (!string.IsNullOrEmpty(label))
                FunText(c, label, 32, 13, 11, theme.SecondaryColor, tf);
            FunText(c, FunNum(value.ValueClamped), 32, 31, 21, theme.TextColor, tf);

            FunRect(c, 4, 39, 56, 6, 3, FunTrack(theme));
            FunRect(c, 4, 39, value.Ratio * 56, 6, 3, FunInfo(theme));

            foreach (float x in new[] { 4f, 32f, 60f })
                FunLine(c, x, 47, x, 50, theme.SecondaryColor, 1);
            FunText(c, FunShort(value.Min), 4, 60, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            FunText(c, FunShort(value.Max), 60, 60, 11, theme.SecondaryColor, tf, SKTextAlign.Right);
        });
    };

    /// <summary>Labeled 180-degree analog meter: needle, graduations, min/max, value.</summary>
    public static RenderFunc ArcScale() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            var oval = new SKRect(6, 14, 58, 66); // centered on (32,40), r=26

            foreach (float f in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                float ang = 180 + f * 180;
                var (ox, oy) = FunPolar(32, 40, 27, ang);
                var (ix, iy) = FunPolar(32, 40, 22, ang);
                FunLine(c, ox, oy, ix, iy, theme.SecondaryColor, 1.5f);
            }
            FunArc(c, oval, 180, 180, value.Ratio, 4, FunTrack(theme), FunInfo(theme));

            float na = 180 + value.Ratio * 180;
            var (nx, ny) = FunPolar(32, 40, 24, na);
            FunLine(c, 32, 40, nx, ny, theme.TextColor, 2);
            using (var hub = FunFill(theme.TextColor))
                c.DrawCircle(32, 40, 3, hub);

            FunText(c, FunShort(value.Min), 4, 52, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            FunText(c, FunShort(value.Max), 60, 52, 11, theme.SecondaryColor, tf, SKTextAlign.Right);
            FunText(c, FunNum(value.ValueClamped), 32, 58, 12, theme.TextColor, tf);
        });
    };

    /// <summary>Vertical segmented bargraph (10 segments) with a side scale.</summary>
    public static RenderFunc SegmentBar() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            int lit = (int)MathF.Round(value.Ratio * 10);
            for (int i = 0; i < 10; i++)
            {
                float y = 53 - i * 5.2f;
                SKColor col = i < lit
                    ? (i < 6 ? FunOk(theme) : i < 8 ? FunWarn(theme) : FunCrit(theme))
                    : FunTrack(theme);
                FunRect(c, 13, y, 22, 4, 1, col);
            }
            foreach (float y in new[] { 55f, 31.6f, 8.2f })
                FunLine(c, 37, y, 40, y, theme.SecondaryColor, 1);
            double mid = (value.Min + value.Max) / 2.0;
            FunText(c, FunShort(value.Min), 42, 58, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            FunText(c, FunShort(mid), 42, 35, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            FunText(c, FunShort(value.Max), 42, 12, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
        });
    };

    /// <summary>
    /// Two concentric rings. The outer ring shows the value; the inner ring is a
    /// "vernier" of the same value (×10 fractional part), giving fine resolution from
    /// a single metric.
    /// </summary>
    public static RenderFunc DualRing() => (canvas, theme, typeface, bounds, label, value) =>
    {
        In64(canvas, bounds, theme, c =>
        {
            float outer = value.Ratio;
            float inner = (outer * 10f) % 1f;
            FunArc(c, new SKRect(5, 5, 59, 59), -90, 360, outer, 5, FunTrack(theme), FunInfo(theme));
            FunArc(c, new SKRect(15, 15, 49, 49), -90, 360, inner, 5, FunTrack(theme), theme.GetAccent(6));
        });
    };

    /// <summary>Horizontal battery with a level-colored fill and percentage.</summary>
    public static RenderFunc Battery() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            FunStrokeRect(c, 6, 17, 46, 30, 4, theme.SecondaryColor, 2);
            FunRect(c, 52, 25, 4, 14, 2, theme.SecondaryColor);
            FunRect(c, 9, 20, value.Ratio * 40, 24, 2, FunLevel(theme, value.Ratio));
            FunTextCentered(c, FunNum(value.ValueClamped), 29, 32, 16, theme.TextColor, tf);
        });
    };

    /// <summary>Vertical thermometer column with a numbered side scale.</summary>
    public static RenderFunc Thermometer() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        In64(canvas, bounds, theme, c =>
        {
            FunRect(c, 14, 7, 12, 50, 6, FunTrack(theme));
            float fillH = value.Ratio * 48f;
            FunRect(c, 15, 56 - fillH, 10, fillH, 5, FunCrit(theme));

            var rows = new (float y, double frac)[] { (55, 0), (43, .25), (31, .5), (19, .75), (7, 1) };
            foreach (var (y, frac) in rows)
            {
                FunLine(c, 28, y, 32, y, theme.SecondaryColor, 1);
                if (frac is 0 or .5 or 1)
                {
                    double val = value.Min + (value.Max - value.Min) * frac;
                    FunText(c, FunShort(val), 34, y + 3, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
                }
            }
        });
    };
}