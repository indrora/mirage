using SkiaSharp;

namespace MirageBox.TinyGauges;

public static partial class Styles
{
    public static RenderFunc Image(SKBitmap bitmap) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            canvas.Clear(theme.BackgroundColor);
            var srcRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(bitmap, srcRect, bounds, paint);
            DrawText(canvas, bounds, theme, typeface, label, value);
        };

    public static RenderFunc ImageFade(SKBitmap A, SKBitmap B) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            canvas.Clear(theme.BackgroundColor);
            var srcRect = new SKRect(0, 0, A.Width, A.Height);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(A, srcRect, bounds, paint);
            paint.Color = paint.Color.WithAlpha((byte)(value.Ratio * 255));
            canvas.DrawBitmap(B, srcRect, bounds, paint);
            DrawText(canvas, bounds, theme, typeface, label, value);
        };
}