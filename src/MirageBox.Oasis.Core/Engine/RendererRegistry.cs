using System.Reflection;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Core.Engine;

public record RendererEntry(string Name, GaugeValueType ValueType, Func<RenderFunc> Factory);

public class RendererRegistry
{
    private readonly Dictionary<string, RendererEntry> _renderers = new(StringComparer.OrdinalIgnoreCase);

    public RendererRegistry()
    {
        DiscoverFromAssembly(typeof(Styles).Assembly);
    }

    public void DiscoverFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<GaugeRendererAttribute>();
                if (attr == null) continue;
                if (method.ReturnType != typeof(RenderFunc)) continue;

                var paramCount = method.GetParameters().Length;
                if (paramCount == 0)
                {
                    var entry = new RendererEntry(attr.Name, attr.ValueType, () => (RenderFunc)method.Invoke(null, null)!);
                    _renderers[attr.Name] = entry;
                }
            }
        }
    }

    public void Register(string name, GaugeValueType valueType, Func<RenderFunc> factory)
    {
        _renderers[name] = new RendererEntry(name, valueType, factory);
    }

    public RendererEntry? Get(string name) =>
        _renderers.GetValueOrDefault(name);

    public RenderFunc? Resolve(string name, DataSources.SensorValueType sensorType)
    {
        var entry = Get(name);
        if (entry == null) return null;

        if (sensorType == DataSources.SensorValueType.Text && entry.ValueType == GaugeValueType.Numeric)
        {
            var textEntry = Get("Text");
            if (textEntry != null)
            {
                Console.Error.WriteLine(
                    $"Warning: Renderer '{name}' is numeric but sensor is text — falling back to Text renderer");
                return textEntry.Factory();
            }
        }

        return entry.Factory();
    }

    public IEnumerable<RendererEntry> GetAll() => _renderers.Values;
}
