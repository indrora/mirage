using SkiaSharp;

namespace MirageBox.Oasis;

public static class ResourceLoader
{
    public static SKTypeface? TryLoadTypeface(string fileName)
    {
        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, fileName),
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", fileName),
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", fileName)
        ];

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
                continue;

            try
            {
                var typeface = SKTypeface.FromFile(fullPath);
                if (typeface is not null)
                    return typeface;
            }
            catch
            {
                // Try next candidate.
            }
        }

        return null;
    }
}
