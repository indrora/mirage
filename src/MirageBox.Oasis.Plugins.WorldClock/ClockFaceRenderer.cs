using MirageBox.TinyGauges;
using SkiaSharp;

namespace MirageBox.Oasis.Plugins.WorldClock;

/// <summary>
/// Sensor-provided renderer for the "face" sensor: an analog clock face for the
/// source's configured timezone. The closure reads the zone time at draw time.
/// </summary>
internal static class ClockFaceRenderer
{
    public static RenderFunc AnalogFace(WorldClockDataSource source) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            var tf = typeface ?? theme.Typeface;
            var now = source.ZoneNow;
            DrawHelpers.In64(canvas, bounds, theme, c =>
            {
                const float cx = 32, cy = 28, r = 22;

                // Day/night tint on the dial: subtle accent during 6:00-18:00.
                var isDay = now.Hour is >= 6 and < 18;
                var dial = isDay ? theme.SecondaryColor.WithAlpha(50) : SKColors.Black.WithAlpha(60);
                using (var fill = DrawHelpers.FunFill(dial))
                    c.DrawCircle(cx, cy, r, fill);
                using (var ring = DrawHelpers.FunStroke(theme.SecondaryColor, 1.5f))
                    c.DrawCircle(cx, cy, r, ring);

                for (var h = 0; h < 12; h++)
                {
                    var (x1, y1) = DrawHelpers.PolartoXY(cx, cy, r - (h % 3 == 0 ? 4f : 2f), h * 30f);
                    var (x2, y2) = DrawHelpers.PolartoXY(cx, cy, r - 1f, h * 30f);
                    DrawHelpers.FunLine(c, x1, y1, x2, y2, theme.SecondaryColor, h % 3 == 0 ? 1.5f : 1f);
                }

                // Angles: 0 deg points right in PolartoXY, clock 12 is -90.
                float hourDeg = (now.Hour % 12 + now.Minute / 60f) * 30f - 90f;
                float minuteDeg = (now.Minute + now.Second / 60f) * 6f - 90f;
                float secondDeg = now.Second * 6f - 90f;

                var (hx, hy) = DrawHelpers.PolartoXY(cx, cy, r * 0.5f, hourDeg);
                DrawHelpers.FunLine(c, cx, cy, hx, hy, theme.PrimaryColor, 3f);
                var (mx, my) = DrawHelpers.PolartoXY(cx, cy, r * 0.8f, minuteDeg);
                DrawHelpers.FunLine(c, cx, cy, mx, my, theme.PrimaryColor, 2f);
                var (sx, sy) = DrawHelpers.PolartoXY(cx, cy, r * 0.85f, secondDeg);
                DrawHelpers.FunLine(c, cx, cy, sx, sy, theme.GetAccent(0), 1f);
                using (var hub = DrawHelpers.FunFill(theme.PrimaryColor))
                    c.DrawCircle(cx, cy, 1.8f, hub);

                DrawHelpers.FunTextCentered(c, label ?? source.ZoneLabel, 32, 60, 9,
                    theme.TextColor.WithAlpha(190), tf);
            });
        };
}
