using System.Runtime.InteropServices;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>Standard Windows power status (no WMI, no administrator rights).</summary>
public sealed class PowerMonitor : IPowerMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public PowerStatus? Read()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        var hasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        int? percent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        bool? onAc = s.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        bool? charging = hasBattery ? (s.BatteryFlag & 8) != 0 : null;
        return new PowerStatus(hasBattery, percent, onAc, charging);
    }
}
