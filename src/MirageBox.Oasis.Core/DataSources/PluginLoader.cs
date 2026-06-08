using System.Reflection;
using System.Runtime.Loader;
using MirageBox.Oasis.Core.DataSources.Builtin;

namespace MirageBox.Oasis.Core.DataSources;

public static class PluginLoader
{
    private static readonly Dictionary<string, Func<IDataSource>> BuiltinSources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["__builtin:static"] = () => new StaticDataSource(),
        ["__builtin:clock"] = () => new ClockDataSource(),
        ["__builtin:counter"] = () => new CounterDataSource(),
        ["__builtin:timer"] = () => new TimerDataSource(),
    };

    public static IDataSource? Create(string pluginName, string? pluginsDir = null)
    {
        if (BuiltinSources.TryGetValue(pluginName, out var factory))
            return factory();

        var searchDirs = new List<string>();
        if (pluginsDir != null) searchDirs.Add(pluginsDir);
        searchDirs.Add(Path.Combine(AppContext.BaseDirectory, "plugins"));

        foreach (var dir in searchDirs)
        {
            var dllPath = Path.Combine(dir, pluginName + ".dll");
            if (!File.Exists(dllPath)) continue;

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var sourceType = assembly.GetExportedTypes()
                .FirstOrDefault(t => typeof(IDataSource).IsAssignableFrom(t) && !t.IsAbstract);

            if (sourceType != null)
                return (IDataSource?)Activator.CreateInstance(sourceType);
        }

        return null;
    }
}
