using System.Reflection;
using System.Runtime.Loader;
using System.Security.Principal;
using MirageBox.Oasis.Core.DataSources.Builtin;

namespace MirageBox.Oasis.Core.DataSources;

/// <summary>
/// Describes a data source discovered in an external plugin assembly.
/// </summary>
public record PluginSourceInfo(
    string PluginId,
    string Description,
    bool RequiresElevation,
    string Category,
    Type Type,
    string AssemblyPath);

public static class PluginLoader
{
    private static readonly Dictionary<string, Type> BuiltinTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["__builtin:static"] = typeof(StaticDataSource),
        ["__builtin:clock"] = typeof(ClockDataSource),
        ["__builtin:counter"] = typeof(CounterDataSource),
        ["__builtin:timer"] = typeof(TimerDataSource),
    };

    private static readonly Dictionary<string, Func<IDataSource>> BuiltinSources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["__builtin:static"] = () => new StaticDataSource(),
        ["__builtin:clock"] = () => new ClockDataSource(),
        ["__builtin:counter"] = () => new CounterDataSource(),
        ["__builtin:timer"] = () => new TimerDataSource(),
    };

    public static IReadOnlyList<string> BuiltinNames => BuiltinSources.Keys.ToList();

    private static readonly object DiscoveryLock = new();
    private static Dictionary<string, PluginSourceInfo>? _discovered;
    private static string? _discoveredDir;
    private static string? _resolverDir;

    /// <summary>
    /// Reflection-loaded plugins don't get deps.json-based probing, so resolve
    /// their dependencies (e.g. LibreHardwareMonitorLib) from the plugins dir.
    /// </summary>
    private static void EnsureDependencyResolver(string dir)
    {
        if (_resolverDir != null) return;
        _resolverDir = dir;
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var candidate = Path.Combine(_resolverDir, name.Name + ".dll");
            return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };
    }

    /// <summary>Whether the current process has administrative elevation.</summary>
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Scans the plugins directory for assemblies marked with
    /// [assembly: DataSourcePlugin] and returns every [DataSource] type found.
    /// Results are cached after the first scan.
    /// </summary>
    public static IReadOnlyList<PluginSourceInfo> DiscoverPlugins(string? pluginsDir = null)
        => DiscoveryCache(pluginsDir).Values.OrderBy(p => p.PluginId).ToList();

    private static Dictionary<string, PluginSourceInfo> DiscoveryCache(string? pluginsDir)
    {
        var dir = pluginsDir ?? Path.Combine(AppContext.BaseDirectory, "plugins");
        lock (DiscoveryLock)
        {
            if (_discovered != null && _discoveredDir == dir)
                return _discovered;

            var found = new Dictionary<string, PluginSourceInfo>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
            {
                EnsureDependencyResolver(dir);
                foreach (var dllPath in Directory.EnumerateFiles(dir, "*.dll"))
                {
                    try
                    {
                        ScanAssembly(dllPath, found);
                    }
                    catch (BadImageFormatException)
                    {
                        // Native or non-.NET DLL shipped alongside a plugin — not an error.
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[plugins] Skipping {Path.GetFileName(dllPath)}: {ex.Message}");
                    }
                }
            }

            _discovered = found;
            _discoveredDir = dir;
            return found;
        }
    }

    private static void ScanAssembly(string dllPath, Dictionary<string, PluginSourceInfo> found)
    {
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
        var pluginAttr = assembly.GetCustomAttribute<DataSourcePluginAttribute>();
        if (pluginAttr == null) return;

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || !typeof(IDataSource).IsAssignableFrom(type)) continue;
            var attr = type.GetCustomAttribute<DataSourceAttribute>();
            if (attr == null) continue;

            var id = attr.Name.Contains(':')
                ? attr.Name
                : $"{pluginAttr.Prefix}:{attr.Name}";

            found[id] = new PluginSourceInfo(
                id, attr.Description, attr.RequiresElevation, attr.Category, type, dllPath);
        }
    }

    public static Type? ResolveType(string pluginName, string? pluginsDir = null)
    {
        if (BuiltinTypes.TryGetValue(pluginName, out var type))
            return type;
        if (DiscoveryCache(pluginsDir).TryGetValue(pluginName, out var info))
            return info.Type;
        return null;
    }

    public static IDataSource? Create(string pluginName, string? pluginsDir = null)
    {
        if (BuiltinSources.TryGetValue(pluginName, out var factory))
            return factory();

        if (DiscoveryCache(pluginsDir).TryGetValue(pluginName, out var info))
            return (IDataSource?)Activator.CreateInstance(info.Type);

        // Legacy fallback: plugin name is a bare assembly name containing one source.
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
