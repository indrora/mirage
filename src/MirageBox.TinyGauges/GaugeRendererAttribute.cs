namespace MirageBox.TinyGauges;

public enum GaugeValueType { Numeric, Text }

[AttributeUsage(AttributeTargets.Method)]
public class GaugeRendererAttribute : Attribute
{
    public string Name { get; }
    public GaugeValueType ValueType { get; }

    public GaugeRendererAttribute(string name, GaugeValueType valueType = GaugeValueType.Numeric)
    {
        Name = name;
        ValueType = valueType;
    }
}
