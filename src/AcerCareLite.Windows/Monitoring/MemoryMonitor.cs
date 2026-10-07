using System.Diagnostics;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Windows.Monitoring;

public sealed class MemoryMonitor : IMemoryMonitor, IDisposable
{
    private PerformanceCounter? _available;

    public MemoryUsage? Read()
    {
        var total = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        _available ??= new PerformanceCounter("Memory", "Available Bytes", readOnly: true);
        var available = (ulong)_available.NextValue();
        if (total == 0 || available > total) return null;
        return new MemoryUsage(total, total - available);
    }

    public void Dispose() => _available?.Dispose();
}
