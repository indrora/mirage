using System.Reflection;

namespace MirageBox.Oasis.Core.DataSources;

public static class SensorAttributeHelper
{
    public static IReadOnlyList<SensorInfo> GetSensorsFromAttributes(Type dataSourceType)
    {
        return dataSourceType
            .GetCustomAttributes<SensorAttribute>()
            .Select(a => new SensorInfo(a.Path, a.ValueType, a.Description, a.RequiresElevation))
            .ToList();
    }
}

public static class SourceActionHelper
{
    public static IReadOnlyList<SourceActionInfo> GetActionsFromAttributes(Type dataSourceType)
    {
        return dataSourceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => (method: m, attr: m.GetCustomAttribute<SourceActionAttribute>()))
            .Where(x => x.attr != null)
            .Select(x => new SourceActionInfo(x.attr!.Name, x.attr.Description, x.attr.ParamName, x.attr.ParamDefault, x.attr.IsDefault))
            .ToList();
    }

    public static string? GetDefaultAction(Type dataSourceType)
    {
        return dataSourceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<SourceActionAttribute>())
            .FirstOrDefault(a => a?.IsDefault == true)
            ?.Name;
    }

    public static void ExecuteAction(IDataSource source, string action, string? param)
    {
        var method = source.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.GetCustomAttribute<SourceActionAttribute>()?.Name == action);

        if (method == null) return;

        var parameters = method.GetParameters();
        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
            method.Invoke(source, [param]);
        else if (parameters.Length == 0)
            method.Invoke(source, []);
    }
}
