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
