namespace MirageBox.Oasis.Core.DataSources;

/// <summary>
/// Assembly-level marker for data source plugin assemblies. All [DataSource]
/// types in the assembly are registered as "&lt;Prefix&gt;:&lt;name&gt;".
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public class DataSourcePluginAttribute : Attribute
{
    public string Prefix { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public DataSourcePluginAttribute(string prefix, string displayName, string description = "")
    {
        Prefix = prefix;
        DisplayName = displayName;
        Description = description;
    }
}

/// <summary>
/// OS platforms a plugin assembly can run on. Flags so a plugin can declare
/// more than one (e.g. <c>Windows | Linux</c>). Bit values are fixed because
/// the loader reads them out of assembly metadata as raw integers (see
/// <see cref="PluginPlatformAttribute"/>) — don't renumber them.
/// </summary>
[Flags]
public enum PluginPlatform
{
    Windows = 1,
    MacOS = 2,
    Linux = 4,
    Any = Windows | MacOS | Linux,
}

/// <summary>
/// Declares which OS platforms a plugin assembly supports. The host reads this
/// metadata-only (via MetadataLoadContext) BEFORE attempting a real load, so a
/// platform-incompatible / RID-specific native plugin is skipped without ever
/// hitting the architecture-bound binder that would otherwise throw
/// FileLoadException. Absence of the attribute is treated as <see cref="PluginPlatform.Any"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PluginPlatformAttribute : Attribute
{
    public PluginPlatform Platforms { get; }

    public PluginPlatformAttribute(PluginPlatform platforms)
    {
        Platforms = platforms;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class DataSourceAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    /// <summary>Source needs administrative elevation to expose its full sensor set.</summary>
    public bool RequiresElevation { get; set; }

    /// <summary>Display grouping in source pickers (e.g. "Hardware").</summary>
    public string Category { get; set; } = "";

    public DataSourceAttribute(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class SensorAttribute : Attribute
{
    public string Path { get; }
    public SensorValueType ValueType { get; }
    public string Description { get; }
    public bool RequiresElevation { get; }

    public SensorAttribute(string path, SensorValueType valueType,
        string description = "", bool requiresElevation = false)
    {
        Path = path;
        ValueType = valueType;
        Description = description;
        RequiresElevation = requiresElevation;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class SourceActionAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string? ParamName { get; }
    public string? ParamDefault { get; }

    public bool IsDefault { get; set; }

    public SourceActionAttribute(string name, string description,
        string? paramName = null, string? paramDefault = null)
    {
        Name = name;
        Description = description;
        ParamName = paramName;
        ParamDefault = paramDefault;
    }
}
