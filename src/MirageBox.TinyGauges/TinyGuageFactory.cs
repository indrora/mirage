namespace MirageBox.TinyGauges;

public static class TinyGaugeFactory
{
    public static ITinyGauge CreateRadial() => new RadialMeter();
    public static ITinyGauge CreateFillTank() => new TankFillMeter();
    public static ITinyGauge CreateNumeric() => new NumericMeter();
    public static ITinyGauge CreateBar() => new BarMeter();
}
