using System.Windows.Threading;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.App;

public sealed class DispatcherPollingTimer : IPollingTimer
{
    private readonly DispatcherTimer _timer = new();

    public DispatcherPollingTimer() => _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);

    public event EventHandler? Tick;

    public void Start(TimeSpan interval)
    {
        _timer.Interval = interval;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
