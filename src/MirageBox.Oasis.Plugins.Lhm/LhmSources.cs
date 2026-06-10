using LibreHardwareMonitor.Hardware;
using MirageBox.Oasis.Core.DataSources;

[assembly: DataSourcePlugin("lhm", "LibreHardwareMonitor",
    "Hardware sensors via LibreHardwareMonitorLib")]

namespace MirageBox.Oasis.Plugins.Lhm;

[DataSource("cpu", "CPU sensors (temperatures, clocks, load, power)",
    RequiresElevation = true, Category = "Hardware")]
public class LhmCpuSource : LhmDataSourceBase
{
    public LhmCpuSource() : base("lhm:cpu", HardwareType.Cpu) { }
}

[DataSource("gpu", "GPU sensors (temperatures, clocks, load, fans, VRAM)",
    Category = "Hardware")]
public class LhmGpuSource : LhmDataSourceBase
{
    public LhmGpuSource() : base("lhm:gpu",
        HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel) { }
}

[DataSource("memory", "System memory usage", Category = "Hardware")]
public class LhmMemorySource : LhmDataSourceBase
{
    public LhmMemorySource() : base("lhm:memory", HardwareType.Memory) { }
}

[DataSource("motherboard", "Motherboard and SuperIO sensors (fans, voltages, temperatures)",
    RequiresElevation = true, Category = "Hardware")]
public class LhmMotherboardSource : LhmDataSourceBase
{
    public LhmMotherboardSource() : base("lhm:motherboard",
        HardwareType.Motherboard, HardwareType.SuperIO) { }
}

[DataSource("storage", "Storage sensors (SMART temperatures, usage, throughput)",
    Category = "Hardware")]
public class LhmStorageSource : LhmDataSourceBase
{
    public LhmStorageSource() : base("lhm:storage", HardwareType.Storage) { }
}

[DataSource("network", "Network adapter throughput and utilization", Category = "Hardware")]
public class LhmNetworkSource : LhmDataSourceBase
{
    public LhmNetworkSource() : base("lhm:network", HardwareType.Network) { }
}

[DataSource("controller", "Embedded controller sensors",
    RequiresElevation = true, Category = "Hardware")]
public class LhmControllerSource : LhmDataSourceBase
{
    public LhmControllerSource() : base("lhm:controller", HardwareType.EmbeddedController) { }
}

[DataSource("battery", "Battery charge, voltage and wear", Category = "Hardware")]
public class LhmBatterySource : LhmDataSourceBase
{
    public LhmBatterySource() : base("lhm:battery", HardwareType.Battery) { }
}

[DataSource("psu", "Power supply sensors", Category = "Hardware")]
public class LhmPsuSource : LhmDataSourceBase
{
    public LhmPsuSource() : base("lhm:psu", HardwareType.Psu) { }
}
