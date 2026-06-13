using System.Reflection;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Builds a metadata-only inspection context over the plugin dir. We list the
    /// runtime's BCL assemblies plus the plugin dir plus this Core assembly (so the
    /// PluginPlatform enum type referenced by a plugin resolves), deduped by simple
    /// name — PathAssemblyResolver throws on duplicate names, and the plugin dir can
    /// ship its own copies of framework assemblies (System.Management, System.IO.Ports…)
    /// that also live in the runtime dir. Runtime copy wins.
    /// </summary>
    private static MetadataLoadContext CreateMetadataContext(string pluginDir)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
            byName[Path.GetFileNameWithoutExtension(path)] = path;
        if (Directory.Exists(pluginDir))
            foreach (var path in Directory.GetFiles(pluginDir, "*.dll"))
                byName.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        var coreAsm = typeof(PluginPlatformAttribute).Assembly.Location;
        byName.TryAdd(Path.GetFileNameWithoutExtension(coreAsm), coreAsm);

        return new MetadataLoadContext(new PathAssemblyResolver(byName.Values));
    }

    /// <summary>
    /// Reads a plugin's [PluginPlatform] attribute from metadata alone — no binding,
    /// no execution — so a RID/architecture-incompatible assembly (e.g. the win-x64
    /// LHM plugin on macOS) is rejected here, before any real LoadFromAssemblyPath
    /// that would throw FileLoadException. Unmarked assemblies are treated as Any.
    /// On any inability to read the metadata we return true and let the normal load
    /// path's error handling deal with it rather than silently hiding a plugin.
    /// </summary>
    private static bool IsLoadableOnThisPlatform(string dllPath, MetadataLoadContext metadata)
    {
        PluginPlatform current;
        if (OperatingSystem.IsWindows()) current = PluginPlatform.Windows;
        else if (OperatingSystem.IsMacOS()) current = PluginPlatform.MacOS;
        else if (OperatingSystem.IsLinux()) current = PluginPlatform.Linux;
        else return true; // unknown host: don't second-guess

        try
        {
            var assembly = metadata.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var attr = assembly.GetCustomAttributesData().FirstOrDefault(
                a => a.AttributeType.FullName == typeof(PluginPlatformAttribute).FullName);
            if (attr == null) return true; // unmarked => runs anywhere

            // The enum ctor arg comes through as its boxed underlying integer.
            var declared = (PluginPlatform)Convert.ToInt32(attr.ConstructorArguments[0].Value);
            return (declared & current) != 0;
        }
        catch (BadImageFormatException)
        {
            // Native or non-.NET dll shipped beside a plugin — ScanAssembly skips it.
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[plugins] Could not read platform metadata for {Path.GetFileName(dllPath)}: {ex.Message}");
            return true;
        }
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
                using var metadata = CreateMetadataContext(dir);
                foreach (var dllPath in Directory.EnumerateFiles(dir, "*.dll"))
                {
                    try
                    {
                        if (!IsLoadableOnThisPlatform(dllPath, metadata))
                        {
                            Console.Error.WriteLine(
                                $"[plugins] Skipping {Path.GetFileName(dllPath)}: not supported on this platform");
                            continue;
                        }
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

            using (var metadata = CreateMetadataContext(dir))
            {
                if (!IsLoadableOnThisPlatform(dllPath, metadata)) continue;
            }

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var sourceType = assembly.GetExportedTypes()
                .FirstOrDefault(t => typeof(IDataSource).IsAssignableFrom(t) && !t.IsAbstract);

            if (sourceType != null)
                return (IDataSource?)Activator.CreateInstance(sourceType);
        }

        return null;
    }
}
