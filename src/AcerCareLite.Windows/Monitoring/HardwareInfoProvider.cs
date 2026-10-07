using System.Management;
using AcerCareLite.Core.Hardware;
using Microsoft.Win32;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>Static hardware facts from standard WMI classes. Each section fails independently.</summary>
public sealed class HardwareInfoProvider : IHardwareInfoProvider
{
    public HardwareSnapshot Read() => new(
        Section(ReadCpus), Section(ReadGpus), Section(ReadMemory), Section(ReadDisks));

    private static IReadOnlyList<T> Section<T>(Func<List<T>> read)
    {
        try { return read(); } catch { return Array.Empty<T>(); }
    }

    private static List<CpuInfo> ReadCpus() =>
        Query(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor")
            .Select(r => new CpuInfo(S(r, "Name"), (int)N(r, "NumberOfCores"), (int)N(r, "NumberOfLogicalProcessors"), (int)N(r, "MaxClockSpeed")))
            .ToList();

    private static List<GpuInfo> ReadGpus() =>
        Query(@"root\cimv2", "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController")
            .Select(r =>
            {
                var name = S(r, "Name");
                var wmiRam = N(r, "AdapterRAM");
                // AdapterRAM is a 32-bit value that saturates at 4 GB, so prefer the 64-bit value Windows stores per adapter.
                ulong? vram = RegistryVram(name) ?? (wmiRam is > 0 and < uint.MaxValue ? (ulong?)wmiRam : null);
                return new GpuInfo(name, S(r, "DriverVersion"), vram);
            }).ToList();

    private static List<MemoryModuleInfo> ReadMemory() =>
        Query(@"root\cimv2", "SELECT DeviceLocator, Capacity, ConfiguredClockSpeed, Speed, Manufacturer FROM Win32_PhysicalMemory")
            .Select(r =>
            {
                var speed = N(r, "ConfiguredClockSpeed") is > 0 and var cs ? cs : N(r, "Speed");
                return new MemoryModuleInfo(S(r, "DeviceLocator"), N(r, "Capacity"), speed > 0 ? (int)speed : null, S(r, "Manufacturer").Trim());
            }).ToList();

    private static List<DiskInfo> ReadDisks()
    {
        try
        {
            var rows = Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, Size, MediaType, BusType FROM MSFT_PhysicalDisk");
            if (rows.Count > 0)
                return rows.Select(r => new DiskInfo(S(r, "FriendlyName"), N(r, "Size"), MediaName(N(r, "MediaType")), BusName(N(r, "BusType")))).ToList();
        }
        catch { /* fall through to the older class */ }

        return Query(@"root\cimv2", "SELECT Model, Size, InterfaceType FROM Win32_DiskDrive")
            .Select(r => new DiskInfo(S(r, "Model"), N(r, "Size"), "Unknown", S(r, "InterfaceType"))).ToList();
    }

    private static string MediaName(ulong v) => v switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unknown type" };
    private static string BusName(ulong v) => v switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", _ => "Other bus" };

    private static ulong? RegistryVram(string adapterName)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (!sub.All(char.IsDigit)) continue;
                using var key = cls.OpenSubKey(sub);
                if (key == null || (key.GetValue("DriverDesc") as string) != adapterName) continue;
                var v = key.GetValue("HardwareInformation.qwMemorySize");
                if (v is long l && l > 0) return (ulong)l;
                if (v is byte[] b && b.Length >= 8) return BitConverter.ToUInt64(b, 0);
            }
        }
        catch { /* not available: caller falls back */ }
        return null;
    }

    private static List<Dictionary<string, object?>> Query(string ns, string wql)
    {
        var scope = new ManagementScope(ns);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementBaseObject o in searcher.Get())
        {
            using (o)
            {
                var d = new Dictionary<string, object?>();
                foreach (PropertyData p in o.Properties) d[p.Name] = p.Value;
                rows.Add(d);
            }
        }
        return rows;
    }

    private static string S(Dictionary<string, object?> r, string key) => r.GetValueOrDefault(key)?.ToString() ?? "";
    private static ulong N(Dictionary<string, object?> r, string key) =>
        r.GetValueOrDefault(key) is { } v ? Convert.ToUInt64(v) : 0;
}
