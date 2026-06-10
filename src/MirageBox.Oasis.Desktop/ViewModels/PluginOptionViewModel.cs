using System.Reflection;
using MirageBox.Oasis.Core.DataSources;

namespace MirageBox.Oasis.Desktop.ViewModels;

/// <summary>A selectable data source plugin (builtin or discovered) for pickers.</summary>
public record PluginOption(string Id, string Description, bool RequiresElevation)
{
    public string Display
    {
        get
        {
            var text = string.IsNullOrEmpty(Description) ? Id : $"{Id} — {Description}";
            return RequiresElevation ? $"{text}  🛡 admin" : text;
        }
    }

    public override string ToString() => Id;
}

/// <summary>All known data source plugins: builtins plus reflection-discovered plugin DLLs.</summary>
public static class PluginCatalog
{
    private static IReadOnlyList<PluginOption>? _all;

    public static IReadOnlyList<PluginOption> All => _all ??= Build();

    public static bool IsElevated => PluginLoader.IsElevated;

    public static PluginOption? Find(string id)
        => All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<PluginOption> Build()
    {
        var options = new List<PluginOption>();

        foreach (var name in PluginLoader.BuiltinNames.OrderBy(n => n))
        {
            var attr = PluginLoader.ResolveType(name)?.GetCustomAttribute<DataSourceAttribute>();
            options.Add(new PluginOption(name, attr?.Description ?? "", attr?.RequiresElevation ?? false));
        }

        foreach (var info in PluginLoader.DiscoverPlugins())
            options.Add(new PluginOption(info.PluginId, info.Description, info.RequiresElevation));

        return options;
    }
}
