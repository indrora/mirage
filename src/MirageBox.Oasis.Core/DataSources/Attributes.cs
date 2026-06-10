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
