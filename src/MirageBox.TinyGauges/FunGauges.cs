using System.Numerics;
using SkiaSharp;

namespace MirageBox.TinyGauges;

// All gauges author geometry in a 64x64 space scaled to bounds at render time.
// value.Ratio drives geometry; value.ValueClamped drives the numeral; label is the caption.

public static partial class Styles
{

    /// <summary>Edge-to-edge circular progress ring with a large centered readout.</summary>
    [GaugeRenderer("FullRing")]
    public static RenderFunc FullRing() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            DrawHelpers.FunArc(c, new SKRect(6, 6, 58, 58), -90, 360, value.Ratio, 7, DrawHelpers.FunTrack(theme), DrawHelpers.FunInfo(theme));
            DrawHelpers.FunTextCentered(c, DrawHelpers.FunNum(value.ValueClamped), 32, label is null ? 32 : 28, 22, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label)) DrawHelpers.FunText(c, label, 32, 50, 11, theme.SecondaryColor, tf);
        });
    };

    /// <summary>Progress traced around the rounded keycap perimeter; center stays free for a number.</summary>
    [GaugeRenderer("Perimeter")]
    public static RenderFunc Perimeter() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            using var path = new SKPath();
            path.AddRoundRect(new SKRoundRect(new SKRect(5, 5, 59, 59), 10, 10));

            using (var tp = DrawHelpers.FunStroke(DrawHelpers.FunTrack(theme), 5))
                c.DrawPath(path, tp);

            using var measure = new SKPathMeasure(path, false);
            float len = measure.Length;
            using var seg = new SKPath();
            if (measure.GetSegment(-90, len * value.Ratio, seg, true))
            {
                using var vp = DrawHelpers.FunStroke(DrawHelpers.FunOk(theme), 5);
                c.DrawPath(seg, vp);
            }

            DrawHelpers.FunTextCentered(c, DrawHelpers.FunNum(value.ValueClamped), 32, label is null ? 32 : 28, 22, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label)) DrawHelpers.FunText(c, label, 32, 50, 11, theme.SecondaryColor, tf);
        });
    };

    /// <summary>Full-tile liquid fill rising from the bottom, with a bright surface line.</summary>
    [GaugeRenderer("LiquidTank")]
    public static RenderFunc LiquidTank() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            float h = value.Ratio * 64f;
            using (new SKAutoCanvasRestore(c))
            {
                c.ClipRoundRect(new SKRoundRect(new SKRect(0.5f, 0.5f, 63.5f, 63.5f), 10, 10), antialias: true);
                using var body = DrawHelpers.FunFill(DrawHelpers.FunInfo(theme).WithAlpha(140));
                c.DrawRect(0, 64 - h, 64, h, body);
                using var surface = DrawHelpers.FunFill(DrawHelpers.FunInfo(theme));
                c.DrawRect(0, 64 - h, 64, 2, surface);
            }

            DrawHelpers.FunTextCentered(c, DrawHelpers.FunNum(value.ValueClamped), 32, 28, 20, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label)) DrawHelpers.FunText(c, label, 32, 46, 11, theme.TextColor, tf);
        });
    };

    /// <summary>270-degree needle dial with a graduated tick ring.</summary>
    [GaugeRenderer("BigDial")]
    public static RenderFunc BigDial() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            foreach (float f in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                float ang = 135 + f * 270;
                var (ox, oy) = DrawHelpers.FunPolar(32, 32, 27, ang);
                var (ix, iy) = DrawHelpers.FunPolar(32, 32, 23, ang);
                DrawHelpers.FunLine(c, ox, oy, ix, iy, theme.SecondaryColor, 1.5f);
            }

            DrawHelpers.FunArc(c, new SKRect(8, 8, 56, 56), 135, 270, value.Ratio, 3, DrawHelpers.FunTrack(theme), DrawHelpers.FunInfo(theme));

            float na = 135 + value.Ratio * 270;
            var (nx, ny) = DrawHelpers.FunPolar(32, 32, 22, na);
            DrawHelpers.FunLine(c, 32, 32, nx, ny, theme.TextColor, 2.5f);
            using (var hub = DrawHelpers.FunFill(theme.TextColor))
                c.DrawCircle(32, 32, 3, hub);

            string text = string.IsNullOrEmpty(label) ? DrawHelpers.FunNum(value.ValueClamped) : label;
            DrawHelpers.FunText(c, text, 32, 47, 11, string.IsNullOrEmpty(label) ? theme.TextColor : theme.SecondaryColor, tf);
        });
    };

    /// <summary>Big numeral over a full-width progress bar pinned to the bottom edge.</summary>
    [GaugeRenderer("NumberBar")]
    public static RenderFunc NumberBar() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            if (!string.IsNullOrEmpty(label)) DrawHelpers.FunText(c, label, 32, 13, 11, theme.SecondaryColor, tf);
            DrawHelpers.FunTextCentered(c, DrawHelpers.FunNum(value.ValueClamped), 32, 34, 24, theme.TextColor, tf);
            DrawHelpers.FunRect(c, 4, 52, 56, 7, 3, DrawHelpers.FunTrack(theme));
            DrawHelpers.FunRect(c, 4, 52, value.Ratio * 56, 7, 3, DrawHelpers.FunInfo(theme));
        });
    };

    /// <summary>Segmented LED ring (12 radial segments) lit proportionally, color-zoned.</summary>
    [GaugeRenderer("LedRing")]
    public static RenderFunc LedRing() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            int lit = (int)MathF.Round(value.Ratio * 12);
            for (int i = 0; i < 12; i++)
            {
                float ang = -90 + i * 30;
                var (ox, oy) = DrawHelpers.FunPolar(32, 32, 29, ang);
                var (ix, iy) = DrawHelpers.FunPolar(32, 32, 22, ang);
                SKColor col = i < lit
                    ? (i < 7 ? DrawHelpers.FunOk(theme) : i < 10 ? DrawHelpers.FunWarn(theme) : DrawHelpers.FunCrit(theme))
                    : DrawHelpers.FunTrack(theme);
                DrawHelpers.FunLine(c, ox, oy, ix, iy, col, 3);
            }

            DrawHelpers.FunTextCentered(c, DrawHelpers.FunNum(value.ValueClamped), 32, label is null ? 32 : 30, 15, theme.TextColor, tf);
            if (!string.IsNullOrEmpty(label)) DrawHelpers.FunText(c, label, 32, 47, 11, theme.SecondaryColor, tf);
        });
    };

    /// <summary>Numeral plus a labeled horizontal scale (min / max from the range).</summary>
    [GaugeRenderer("ValueScale")]
    public static RenderFunc ValueScale() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            if (!string.IsNullOrEmpty(label)) DrawHelpers.FunText(c, label, 32, 13, 11, theme.SecondaryColor, tf);
            DrawHelpers.FunText(c, DrawHelpers.FunNum(value.ValueClamped), 32, 31, 21, theme.TextColor, tf);

            DrawHelpers.FunRect(c, 4, 39, 56, 6, 3, DrawHelpers.FunTrack(theme));
            DrawHelpers.FunRect(c, 4, 39, value.Ratio * 56, 6, 3, DrawHelpers.FunInfo(theme));

            foreach (float x in new[] { 4f, 32f, 60f }) DrawHelpers.FunLine(c, x, 47, x, 50, theme.SecondaryColor, 1);
            DrawHelpers.FunText(c, DrawHelpers.FunShort(value.Min), 4, 60, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            DrawHelpers.FunText(c, DrawHelpers.FunShort(value.Max), 60, 60, 11, theme.SecondaryColor, tf, SKTextAlign.Right);
        });
    };

    /// <summary>Labeled 180-degree analog meter: needle, graduations, min/max, value.</summary>
    [GaugeRenderer("ArcScale")]
    public static RenderFunc ArcScale() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            var oval = new SKRect(6, 14, 58, 66); // centered on (32,40), r=26

            foreach (float f in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                float ang = 180 + f * 180;
                var (ox, oy) = DrawHelpers.FunPolar(32, 40, 27, ang);
                var (ix, iy) = DrawHelpers.FunPolar(32, 40, 22, ang);
                DrawHelpers.FunLine(c, ox, oy, ix, iy, theme.SecondaryColor, 1.5f);
            }

            DrawHelpers.FunArc(c, oval, 180, 180, value.Ratio, 4, DrawHelpers.FunTrack(theme), DrawHelpers.FunInfo(theme));

            float na = 180 + value.Ratio * 180;
            var (nx, ny) = DrawHelpers.FunPolar(32, 40, 24, na);
            DrawHelpers.FunLine(c, 32, 40, nx, ny, theme.TextColor, 2);
            using (var hub = DrawHelpers.FunFill(theme.TextColor))
                c.DrawCircle(32, 40, 3, hub);

            DrawHelpers.FunText(c, DrawHelpers.FunShort(value.Min), 4, 52, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            DrawHelpers.FunText(c, DrawHelpers.FunShort(value.Max), 60, 52, 11, theme.SecondaryColor, tf, SKTextAlign.Right);
            DrawHelpers.FunText(c, DrawHelpers.FunNum(value.ValueClamped), 32, 58, 12, theme.TextColor, tf);
        });
    };

    /// <summary>Vertical segmented bargraph (10 segments) with a side scale.</summary>
    [GaugeRenderer("SegmentBar")]
    public static RenderFunc SegmentBar() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            int lit = (int)MathF.Round(value.Ratio * 10);
            for (int i = 0; i < 10; i++)
            {
                float y = 53 - i * 5.2f;
                SKColor col = i < lit
                    ? (i < 6 ? DrawHelpers.FunOk(theme) : i < 8 ? DrawHelpers.FunWarn(theme) : DrawHelpers.FunCrit(theme))
                    : DrawHelpers.FunTrack(theme);
                DrawHelpers.FunRect(c, 13, y, 22, 4, 1, col);
            }
            foreach (float y in new[] { 55f, 31.6f, 8.2f }) DrawHelpers.FunLine(c, 37, y, 40, y, theme.SecondaryColor, 1);
            double mid = (value.Min + value.Max) / 2.0;
            DrawHelpers.FunText(c, DrawHelpers.FunShort(value.Min), 42, 58, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            DrawHelpers.FunText(c, DrawHelpers.FunShort(mid), 42, 35, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
            DrawHelpers.FunText(c, DrawHelpers.FunShort(value.Max), 42, 12, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
        });
    };

    /// <summary>
    /// Two concentric rings. The outer ring shows the value; the inner ring is a
    /// "vernier" of the same value (×10 fractional part), giving fine resolution from
    /// a single metric.
    /// </summary>
    [GaugeRenderer("DualRing")]
    public static RenderFunc DualRing() => (canvas, theme, typeface, bounds, label, value) =>
    {
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            float outer = value.Ratio;
            float inner = (outer * 10f) % 1f;
            DrawHelpers.FunArc(c, new SKRect(5, 5, 59, 59), -90, 360, outer, 5, DrawHelpers.FunTrack(theme), DrawHelpers.FunInfo(theme));
            DrawHelpers.FunArc(c, new SKRect(15, 15, 49, 49), -90, 360, inner, 5, DrawHelpers.FunTrack(theme), theme.GetAccent(6));
        });
    };

    /// <summary>Horizontal battery with a level-colored fill and percentage.</summary>
    [GaugeRenderer("Battery")]
    public static RenderFunc Battery() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            DrawHelpers.FunStrokeRect(c, 6, 17, 46, 30, 4, theme.SecondaryColor, 2);
            DrawHelpers.FunRect(c, 52, 25, 4, 14, 2, theme.SecondaryColor);
            DrawHelpers.FunRect(c, 9, 20, value.Ratio * 40, 24, 2, DrawHelpers.FunLevel(theme, value.Ratio));
            DrawHelpers.FunTextCentered(c, DrawHelpers.FunNum(value.ValueClamped), 29, 32, 16, theme.TextColor, tf);
        });
    };

    /// <summary>Vertical thermometer column with a numbered side scale.</summary>
    [GaugeRenderer("Thermometer")]
    public static RenderFunc Thermometer() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            DrawHelpers.FunRect(c, 14, 7, 12, 50, 6, DrawHelpers.FunTrack(theme));
            float fillH = value.Ratio * 48f;
            DrawHelpers.FunRect(c, 15, 56 - fillH, 10, fillH, 5, DrawHelpers.FunCrit(theme));

            var rows = new (float y, double frac)[] { (55, 0), (43, .25), (31, .5), (19, .75), (7, 1) };
            foreach (var (y, frac) in rows)
            {
                DrawHelpers.FunLine(c, 28, y, 32, y, theme.SecondaryColor, 1);
                if (frac is 0 or .5 or 1)
                {
                    double val = value.Min + (value.Max - value.Min) * frac;
                    DrawHelpers.FunText(c, DrawHelpers.FunShort(val), 34, y + 3, 11, theme.SecondaryColor, tf, SKTextAlign.Left);
                }
            }
        });
    };

    [GaugeRenderer("Text", GaugeValueType.Text)]
    public static RenderFunc Text() => (canvas, theme, typeface, bounds, label, value) =>
    {
        var tf = typeface ?? theme.Typeface;
        DrawHelpers.In64(canvas, bounds, theme, c =>
        {
            var text = label ?? "";
            float fontSize = text.Length <= 5 ? 20 : text.Length <= 8 ? 16 : 12;
            DrawHelpers.FunTextCentered(c, text, 32, 36, fontSize, theme.TextColor, tf);
        });
    };
}