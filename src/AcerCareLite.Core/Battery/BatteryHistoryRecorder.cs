using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Core.Battery;

/// <summary>Records one sample per interval while the app is running. No background service.</summary>
public sealed class BatteryHistoryRecorder : IDisposable
{
    private readonly IBatteryReader _reader;
    private readonly IBatteryHistoryStore _store;
    private readonly IPollingTimer _timer;
    private bool _started;
    private int _count;

    public BatteryHistoryRecorder(IBatteryReader reader, IBatteryHistoryStore store, IPollingTimer timer)
    {
        _reader = reader; _store = store; _timer = timer;
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _timer.Tick += async (_, _) => await SampleAsync();
        _timer.Start(Interval);
        _ = SampleAsync();
    }

    public async Task SampleAsync()
    {
        try
        {
            var info = await Task.Run(() => _reader.Read());
            if (info?.Percent is not { } percent) return;
            var sample = new BatterySample(Clock(), percent, info.OnAc ?? false, info.Charging ?? false);
            await Task.Run(() =>
            {
                _store.Add(sample);
                if (_count++ % 60 == 0) _store.Prune(Clock() - Retention);
            });
        }
        catch { /* history is best-effort; the next tick tries again */ }
    }

    public void Dispose() => _timer.Stop();
}
