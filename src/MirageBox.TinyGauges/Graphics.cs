using SkiaSharp;

namespace MirageBox.TinyGauges;

public static partial class Styles
{
    [GaugeRenderer("Image")]
    public static RenderFunc Image(
        [RendererParam("image", "Background image", RendererParamKind.Bitmap)]
        SKBitmap bitmap,
        [RendererParam("drawLabel", "Draw label", RendererParamKind.Boolean, Default="false")]
        bool drawLabel
        ) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            canvas.Clear(theme.BackgroundColor);
            var srcRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(bitmap, srcRect, bounds, paint);
            if(drawLabel) DrawHelpers.DrawText(canvas, bounds, theme, typeface, label, value);
        };

    [GaugeRenderer("ImageFade")]
    public static RenderFunc ImageFade(
        [RendererParam("imageA", "Image shown at 0%", RendererParamKind.Bitmap)]
        SKBitmap A,
        [RendererParam("imageB", "Image shown at 100%", RendererParamKind.Bitmap)]
        SKBitmap B,
        [RendererParam("drawLabel", "Draw label", RendererParamKind.Boolean)]
        bool drawLabel) =>
        (canvas, theme, typeface, bounds, label, value) =>
        {
            canvas.Clear(theme.BackgroundColor);
            var srcRect = new SKRect(0, 0, A.Width, A.Height);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(A, srcRect, bounds, paint);
            paint.Color = paint.Color.WithAlpha((byte)(value.Ratio * 255));
            canvas.DrawBitmap(B, srcRect, bounds, paint);
            if(drawLabel) DrawHelpers.DrawText(canvas, bounds, theme, typeface, label, value);
        };
}