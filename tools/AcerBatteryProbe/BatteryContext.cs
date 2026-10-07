using System.Management;

namespace AcerBatteryProbe;

/// <summary>Plain Windows battery numbers (standard Microsoft classes), captured alongside the probe for comparison.</summary>
internal static class BatteryContext
{
    private static readonly (string Class, string[] Props)[] Sources =
    {
        ("BatteryStaticData", new[] { "DesignedCapacity", "DeviceName" }),
        ("BatteryFullChargedCapacity", new[] { "FullChargedCapacity" }),
        ("BatteryStatus", new[] { "RemainingCapacity", "PowerOnline", "Charging", "Discharging", "ChargeRate", "DischargeRate", "Voltage" }),
        ("BatteryCycleCount", new[] { "CycleCount" }),
        ("BatteryTemperature", new[] { "Temperature" })
    };

    public static void Capture(ProbeReport report)
    {
        foreach (var (cls, props) in Sources)
        {
            try
            {
                var scope = new ManagementScope(@"root\wmi");
                scope.Connect();
                using var s = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {cls}"));
                foreach (ManagementBaseObject o in s.Get())
                {
                    using (o)
                    {
                        var row = new Dictionary<string, string?>();
                        foreach (var p in props) row[p] = o[p]?.ToString();
                        report.BatteryContext[cls] = row;
                    }
                    break;
                }
            }
            catch (Exception ex) { report.Errors.Add($"BatteryContext/{cls}: {ex.Message}"); }
        }
    }
}
