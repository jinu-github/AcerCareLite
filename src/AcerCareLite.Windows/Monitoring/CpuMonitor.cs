using System.Diagnostics;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>Same counter Task Manager uses. The first call primes it and returns null. Safe to call from several threads.</summary>
public sealed class CpuMonitor : ICpuMonitor, IDisposable
{
    private readonly object _gate = new();
    private PerformanceCounter? _counter;
    private bool _failed;

    public double? GetUsagePercent()
    {
        lock (_gate)
        {
            if (_failed) return null;
            try
            {
                if (_counter == null)
                {
                    _counter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true);
                    _counter.NextValue();
                    return null;
                }
                return Math.Clamp(_counter.NextValue(), 0, 100);
            }
            catch { _failed = true; return null; }
        }
    }

    public void Dispose()
    {
        lock (_gate) _counter?.Dispose();
    }
}
