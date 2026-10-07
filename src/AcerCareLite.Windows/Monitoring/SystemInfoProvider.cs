using System.Management;
using System.Runtime.InteropServices;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Windows.Monitoring;

public sealed class SystemInfoProvider : ISystemInfoProvider
{
    public SystemInfo? Read()
    {
        string manufacturer = "", model = "", bios = "";
        using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            foreach (ManagementBaseObject o in s.Get())
                using (o) { manufacturer = o["Manufacturer"]?.ToString() ?? ""; model = o["Model"]?.ToString() ?? ""; break; }
        using (var s = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
            foreach (ManagementBaseObject o in s.Get())
                using (o) { bios = o["SMBIOSBIOSVersion"]?.ToString() ?? ""; break; }
        return new SystemInfo(manufacturer, model, bios, RuntimeInformation.OSDescription);
    }
}
