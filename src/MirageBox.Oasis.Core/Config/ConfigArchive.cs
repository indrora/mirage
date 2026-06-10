using System.IO.Compression;
using System.Text.Json;
using MirageBox.Oasis.Core.Engine;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.Config;

/// <summary>
/// Exports/imports a configuration as a zip archive: config.json at the root
/// plus an assets/ folder containing every image referenced by gauge renderer
/// parameters. Paths inside the archive are rewritten to be relative
/// ("assets/&lt;file&gt;") and restored to absolute paths on import.
/// </summary>
public static class ConfigArchive
{
    /// <summary>Where imported assets are unpacked: ~/.mirage/assets</summary>
    public static string AssetsDir =>
        Path.Combine(Path.GetDirectoryName(ConfigLoader.DefaultConfigPath)!, "assets");

    public static void Export(OasisConfig config, RendererRegistry rendererRegistry, string zipPath)
    {
        // Deep-clone so path rewriting never touches the live config.
        var clone = JsonSerializer.Deserialize<OasisConfig>(JsonSerializer.Serialize(config))!;

        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var packed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // abs path -> archive path
        foreach (var imagePath in EnumerateImageParams(clone, rendererRegistry))
        {
            var abs = imagePath.Value;
            if (string.IsNullOrWhiteSpace(abs) || !File.Exists(abs)) continue;

            if (!packed.TryGetValue(abs, out var archivePath))
            {
                archivePath = $"assets/{Path.GetFileName(abs)}";
                var n = 2;
                while (packed.ContainsValue(archivePath))
                    archivePath = $"assets/{Path.GetFileNameWithoutExtension(abs)}-{n++}{Path.GetExtension(abs)}";
                zip.CreateEntryFromFile(abs, archivePath);
                packed[abs] = archivePath;
            }
            imagePath.Rewrite(archivePath);
        }

        var entry = zip.CreateEntry("config.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(clone, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static OasisConfig Import(string zipPath, RendererRegistry rendererRegistry)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var configEntry = zip.GetEntry("config.json")
            ?? throw new InvalidDataException("Archive has no config.json at its root.");

        OasisConfig config;
        using (var reader = new StreamReader(configEntry.Open()))
        {
            config = JsonSerializer.Deserialize<OasisConfig>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Archive config.json is empty or invalid.");
        }

        Directory.CreateDirectory(AssetsDir);
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // archive path -> abs path
        foreach (var entry in zip.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (!normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            var dest = Path.Combine(AssetsDir, entry.Name);
            entry.ExtractToFile(dest, overwrite: true);
            extracted[normalized] = dest;
        }

        foreach (var imagePath in EnumerateImageParams(config, rendererRegistry))
        {
            var normalized = imagePath.Value.Replace('\\', '/');
            if (extracted.TryGetValue(normalized, out var abs))
                imagePath.Rewrite(abs);
        }

        return config;
    }

    private record ImageParamRef(string Value, Action<string> Rewrite);

    /// <summary>Every Bitmap-kind renderer parameter value across all gauges.</summary>
    private static IEnumerable<ImageParamRef> EnumerateImageParams(
        OasisConfig config, RendererRegistry rendererRegistry)
    {
        foreach (var gauge in config.Gauges.Values)
        {
            var parameters = gauge.Renderer.Parameters;
            if (parameters == null) continue;

            var rendererEntry = rendererRegistry.Get(gauge.Renderer.Type);
            if (rendererEntry == null) continue;

            foreach (var paramInfo in rendererEntry.Parameters)
            {
                if (paramInfo.Kind != RendererParamKind.Bitmap) continue;
                if (!parameters.TryGetValue(paramInfo.Key, out var elem)) continue;
                if (elem.ValueKind != JsonValueKind.String) continue;

                var value = elem.GetString();
                if (string.IsNullOrEmpty(value)) continue;

                var key = paramInfo.Key;
                var dict = parameters;
                yield return new ImageParamRef(value,
                    newValue => dict[key] = JsonSerializer.SerializeToElement(newValue));
            }
        }
    }
}
