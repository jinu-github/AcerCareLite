namespace AcerCareLite.Core.Hardware;

public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors, int MaxClockMhz);
public sealed record GpuInfo(string Name, string DriverVersion, ulong? VramBytes);
public sealed record MemoryModuleInfo(string Slot, ulong CapacityBytes, int? SpeedMhz, string Manufacturer);
public sealed record DiskInfo(string Model, ulong SizeBytes, string MediaType, string BusType);

public sealed record HardwareSnapshot(
    IReadOnlyList<CpuInfo> Cpus,
    IReadOnlyList<GpuInfo> Gpus,
    IReadOnlyList<MemoryModuleInfo> Memory,
    IReadOnlyList<DiskInfo> Disks);

public interface IHardwareInfoProvider { HardwareSnapshot Read(); }
