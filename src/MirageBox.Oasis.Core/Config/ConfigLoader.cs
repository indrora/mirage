using System.Text.Json;

namespace MirageBox.Oasis.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mirage", "config.json");

    public static OasisConfig Load(string? path = null)
    {
        path ??= DefaultConfigPath;
        if (!File.Exists(path))
            return new OasisConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<OasisConfig>(json, JsonOptions) ?? new OasisConfig();
    }

    public static void Save(OasisConfig config, string? path = null)
    {
        path ??= DefaultConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }
}
