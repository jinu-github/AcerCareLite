using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Settings;

namespace AcerCareLite.Core.Notifications;

public sealed record Alert(string Title, string Message);

public interface INotifier { void Notify(string title, string message); }

/// <summary>
/// Pure alert rules with memory. Each alert fires once per event and re-arms after recovery.
/// The first battery reading only seeds the state, so conditions that already exist at startup do not alert.
/// </summary>
public sealed class AlertEngine
{
    private bool _batterySeeded;
    private bool? _lastOnAc;
    private bool _lowArmed = true, _chargeArmed = true;
    private readonly HashSet<string> _lowStorage = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Alert> Evaluate(AppSettings s, BatteryInfo? battery, IReadOnlyList<DriveUsage> drives)
    {
        var alerts = new List<Alert>();

        if (battery?.Percent is { } p)
        {
            var onAc = battery.OnAc;
            var live = _batterySeeded;

            // Low battery: on battery power at or below the chosen level.
            var low = onAc == false && p <= s.LowBatteryPercent;
            if (low)
            {
                if (_lowArmed && live && s.NotifyLowBattery) alerts.Add(new Alert("Battery low", $"{p}% remaining. Plug in your charger."));
                _lowArmed = false;
            }
            else if (onAc == true || p >= s.LowBatteryPercent + 5) _lowArmed = true;

            // Battery threshold: plugged in and at or above the chosen level.
            var high = onAc == true && p >= s.ChargeLevelPercent;
            if (high)
            {
                if (_chargeArmed && live && s.NotifyChargeLevel) alerts.Add(new Alert($"Battery at {p}%", "The charge level you chose has been reached."));
                _chargeArmed = false;
            }
            else if (onAc == false || p < s.ChargeLevelPercent - 2) _chargeArmed = true;

            // Charger connected or disconnected.
            if (onAc is { } now)
            {
                if (_lastOnAc is { } before && before != now && s.NotifyAcChange)
                    alerts.Add(new Alert(now ? "Charger connected" : "Charger disconnected", $"Battery at {p}%."));
                _lastOnAc = now;
            }
            _batterySeeded = true;
        }

        // Low storage: once per drive until free space recovers by 2 points.
        foreach (var d in drives)
        {
            if (d.TotalBytes == 0) continue;
            var freePct = 100.0 * d.FreeBytes / d.TotalBytes;
            if (freePct < s.LowStoragePercentFree)
            {
                if (_lowStorage.Add(d.Name) && s.NotifyLowStorage)
                    alerts.Add(new Alert($"Low storage on {d.Name}", $"{ByteFormat.Gb(d.FreeBytes)} GB free ({freePct:0}%)."));
            }
            else if (freePct >= s.LowStoragePercentFree + 2) _lowStorage.Remove(d.Name);
        }

        return s.NotificationsEnabled ? alerts : Array.Empty<Alert>();
    }
}
