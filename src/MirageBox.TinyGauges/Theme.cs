using SkiaSharp;

namespace MirageBox.TinyGauges;


public record struct Theme
{
    public SKTypeface? Typeface { get; init; }
    public SKColor PrimaryColor { get; init; }
    public SKColor SecondaryColor { get; init; }
    public SKColor BackgroundColor { get; init; }
    public SKColor TextColor { get; init; }

    public SKColor[] Accents {get; init; }

    public static Theme Default => new Theme
    {
        Typeface = null,
        PrimaryColor = SKColors.White,
        SecondaryColor = SKColors.LightGray,
        BackgroundColor = SKColors.Black,
        TextColor = SKColors.White,
        Accents = Array.Empty<SKColor>()
    };

    public SKColor GetAccent(int index)
    {
        // If no accents are defined, return the primary color.
        if (Accents == null || Accents.Length == 0)
            return PrimaryColor;

        // Return the accent color at the specified index, clamping to the valid range.
        return Accents[Math.Max(0, Math.Min(index, Accents.Length - 1))];
    }
    
    

}