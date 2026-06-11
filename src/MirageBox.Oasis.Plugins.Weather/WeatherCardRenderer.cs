using MirageBox.TinyGauges;
using SkiaSharp;

namespace MirageBox.Oasis.Plugins.Weather;

/// <summary>
/// Sensor-provided renderer for the "status" sensor: condition glyph, big
/// temperature, condition + location text. The closure reads the source's live
/// snapshot at draw time — the RangedValue/label arguments can't carry a
/// composite reading.
/// </summary>
internal static class WeatherCardRenderer
{
    public static RenderFunc StatusCard(WeatherDataSource source) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            var tf = typeface ?? theme.Typeface;
            var snap = source.Snapshot;
            DrawHelpers.In64(canvas, bounds, theme, c =>
            {
                if (snap == null)
                {
                    DrawHelpers.FunTextCentered(c, label ?? "Weather", 32, 30, 12, theme.SecondaryColor, tf);
                    DrawHelpers.FunTextCentered(c, "...", 32, 44, 12, theme.TextColor, tf);
                    return;
                }

                var (condition, icon) = WmoCodes.Describe(snap.WeatherCode);
                DrawIcon(c, theme, icon, cx: 18, cy: 16, r: 11);

                var temp = $"{MathF.Round(snap.Temperature)}°{(source.Imperial ? "F" : "C")}";
                var tempSize = temp.Length <= 4 ? 17f : 14f;
                DrawHelpers.FunText(c, temp, 31, 23, tempSize, theme.PrimaryColor, tf, SKTextAlign.Left);

                DrawHelpers.FunTextCentered(c, condition, 32, 44, 11, theme.TextColor, tf);
                // Not the label argument: for text sensors the engine passes the
                // sensor value there, which is already drawn as the condition.
                DrawHelpers.FunTextCentered(c, snap.Location, 32, 57, 9, theme.TextColor.WithAlpha(190), tf);
            });
        };

    // All glyphs draw in the 64-unit space of DrawHelpers.In64, centered at
    // (cx, cy) with nominal radius r.
    private static void DrawIcon(SKCanvas c, Theme theme, WeatherIcon icon, float cx, float cy, float r)
    {
        switch (icon)
        {
            case WeatherIcon.Sun:
                DrawSun(c, theme, cx, cy, r);
                break;
            case WeatherIcon.PartlyCloudy:
                DrawSun(c, theme, cx - r * 0.3f, cy - r * 0.3f, r * 0.65f);
                DrawCloud(c, theme.SecondaryColor, cx + r * 0.15f, cy + r * 0.3f, r * 0.8f);
                break;
            case WeatherIcon.Cloud:
                DrawCloud(c, theme.SecondaryColor, cx, cy, r);
                break;
            case WeatherIcon.Fog:
                for (var i = 0; i < 3; i++)
                    DrawHelpers.FunLine(c, cx - r, cy - r * 0.5f + i * r * 0.5f, cx + r, cy - r * 0.5f + i * r * 0.5f,
                        theme.SecondaryColor, 2.5f);
                break;
            case WeatherIcon.Rain:
                DrawCloud(c, theme.SecondaryColor, cx, cy - r * 0.25f, r * 0.9f);
                for (var i = 0; i < 3; i++)
                    DrawHelpers.FunLine(c, cx - r * 0.5f + i * r * 0.5f, cy + r * 0.45f,
                        cx - r * 0.65f + i * r * 0.5f, cy + r * 0.95f, DrawHelpers.FunInfo(theme), 2f);
                break;
            case WeatherIcon.Snow:
                DrawCloud(c, theme.SecondaryColor, cx, cy - r * 0.25f, r * 0.9f);
                for (var i = 0; i < 3; i++)
                    DrawSnowflake(c, theme.TextColor, cx - r * 0.5f + i * r * 0.5f, cy + r * 0.7f, r * 0.18f);
                break;
            case WeatherIcon.Thunder:
                DrawCloud(c, theme.SecondaryColor, cx, cy - r * 0.25f, r * 0.9f);
                DrawBolt(c, DrawHelpers.FunWarn(theme), cx, cy + r * 0.65f, r * 0.55f);
                break;
        }
    }

    private static void DrawSun(SKCanvas c, Theme theme, float cx, float cy, float r)
    {
        var color = DrawHelpers.FunWarn(theme);
        using var fill = DrawHelpers.FunFill(color);
        c.DrawCircle(cx, cy, r * 0.55f, fill);
        for (var i = 0; i < 8; i++)
        {
            var (x1, y1) = DrawHelpers.PolartoXY(cx, cy, r * 0.72f, i * 45f);
            var (x2, y2) = DrawHelpers.PolartoXY(cx, cy, r, i * 45f);
            DrawHelpers.FunLine(c, x1, y1, x2, y2, color, 2f);
        }
    }

    private static void DrawCloud(SKCanvas c, SKColor color, float cx, float cy, float r)
    {
        using var fill = DrawHelpers.FunFill(color);
        c.DrawCircle(cx - r * 0.45f, cy + r * 0.1f, r * 0.45f, fill);
        c.DrawCircle(cx + r * 0.1f, cy - r * 0.15f, r * 0.55f, fill);
        c.DrawCircle(cx + r * 0.55f, cy + r * 0.15f, r * 0.4f, fill);
        c.DrawRect(cx - r * 0.45f, cy + r * 0.1f, r, r * 0.45f, fill);
    }

    private static void DrawSnowflake(SKCanvas c, SKColor color, float cx, float cy, float r)
    {
        for (var i = 0; i < 3; i++)
        {
            var (x1, y1) = DrawHelpers.PolartoXY(cx, cy, r, i * 60f);
            var (x2, y2) = DrawHelpers.PolartoXY(cx, cy, r, i * 60f + 180f);
            DrawHelpers.FunLine(c, x1, y1, x2, y2, color, 1.5f);
        }
    }

    private static void DrawBolt(SKCanvas c, SKColor color, float cx, float cy, float r)
    {
        using var path = new SKPath();
        path.MoveTo(cx + r * 0.3f, cy - r);
        path.LineTo(cx - r * 0.4f, cy + r * 0.2f);
        path.LineTo(cx, cy + r * 0.2f);
        path.LineTo(cx - r * 0.3f, cy + r);
        path.LineTo(cx + r * 0.4f, cy - r * 0.2f);
        path.LineTo(cx, cy - r * 0.2f);
        path.Close();
        using var fill = DrawHelpers.FunFill(color);
        c.DrawPath(path, fill);
    }
}
