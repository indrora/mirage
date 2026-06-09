using System.Text.Json;

namespace MirageBox.Oasis.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
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
        return JsonSerializer.Deserialize<OasisConfig>(json, ReadOptions) ?? new OasisConfig();
    }

    public static void Save(OasisConfig config, string? path = null)
    {
        path ??= DefaultConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(path, json);
    }
}
