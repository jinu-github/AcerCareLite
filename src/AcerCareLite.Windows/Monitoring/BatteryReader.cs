using System.Management;
using AcerCareLite.Core.Battery;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>Standard Microsoft battery classes in root\wmi. Readable without administrator rights.</summary>
public sealed class BatteryReader : IBatteryReader
{
    private readonly ManagementScope _scope = new(@"root\wmi");

    public BatteryInfo? Read()
    {
        var status = First("BatteryStatus");
        if (status == null) return null;
        var statics = First("BatteryStaticData");
        var full = First("BatteryFullChargedCapacity");
        var cycles = First("BatteryCycleCount");

        var design = U(statics, "DesignedCapacity");
        var fullCap = U(full, "FullChargedCapacity");
        var remaining = U(status, "RemainingCapacity");
        var charging = B(status, "Charging");
        var discharging = B(status, "Discharging");

        int? percent = remaining is { } r && fullCap is > 0
            ? (int)Math.Clamp(Math.Round(100.0 * r / fullCap.Value), 0, 100) : null;
        int? rate = charging == true ? I(status, "ChargeRate")
            : discharging == true ? -I(status, "DischargeRate") : 0;

        return new BatteryInfo(percent, B(status, "PowerOnline"), charging, discharging,
            design, fullCap, remaining, U(status, "Voltage"), rate, U(cycles, "CycleCount"),
            statics?.GetValueOrDefault("DeviceName")?.ToString());
    }

    private Dictionary<string, object?>? First(string className)
    {
        try
        {
            if (!_scope.IsConnected) _scope.Connect();
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery($"SELECT * FROM {className}"));
            foreach (ManagementBaseObject o in searcher.Get())
            {
                using (o)
                {
                    var d = new Dictionary<string, object?>();
                    foreach (PropertyData p in o.Properties) d[p.Name] = p.Value;
                    return d;
                }
            }
        }
        catch { /* class missing or access problem: treat as not available */ }
        return null;
    }

    private static uint? U(Dictionary<string, object?>? d, string key) =>
        d != null && d.TryGetValue(key, out var v) && v != null ? Convert.ToUInt32(v) : null;

    private static int I(Dictionary<string, object?>? d, string key) =>
        d != null && d.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;

    private static bool? B(Dictionary<string, object?>? d, string key) =>
        d != null && d.TryGetValue(key, out var v) && v != null ? Convert.ToBoolean(v) : null;
}
