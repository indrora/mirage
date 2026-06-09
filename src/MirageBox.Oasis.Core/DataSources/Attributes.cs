namespace MirageBox.Oasis.Core.DataSources;

[AttributeUsage(AttributeTargets.Class)]
public class DataSourceAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

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
