using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Settings;

namespace AcerCareLite.Core.Notifications;

/// <summary>One light check every 30 seconds (battery state and drive space), only while the app runs.</summary>
public sealed class NotificationService
{
    private readonly IBatteryReader _battery;
    private readonly IStorageMonitor _storage;
    private readonly SettingsService _settings;
    private readonly AlertEngine _engine;
    private readonly INotifier _notifier;
    private readonly IPollingTimer _timer;
    private bool _checking, _started;

    public NotificationService(IBatteryReader battery, IStorageMonitor storage, SettingsService settings,
        AlertEngine engine, INotifier notifier, IPollingTimer timer)
    {
        _battery = battery; _storage = storage; _settings = settings; _engine = engine; _notifier = notifier; _timer = timer;
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    public void Start()
    {
        if (_started) return;
        _started = true;
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start(Interval);
        _ = CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var snap = await Task.Run(() => (
                Battery: Safe(_battery.Read, null),
                Drives: Safe<IReadOnlyList<DriveUsage>>(_storage.Read, Array.Empty<DriveUsage>())));

            // Back on the caller's (UI) thread: balloons must be shown from there.
            foreach (var alert in _engine.Evaluate(_settings.Current, snap.Battery, snap.Drives))
                _notifier.Notify(alert.Title, alert.Message);
        }
        catch { /* alerts are best-effort; try again next tick */ }
        finally { _checking = false; }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
}
