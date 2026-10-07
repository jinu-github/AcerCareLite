using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>
/// Busiest GPU by 3D engine utilization, from the "GPU Engine" counters (works for Intel and NVIDIA).
/// Needs two samples, so the first call returns null. Safe to call from several threads.
/// </summary>
public sealed class GpuMonitor : IGpuMonitor
{
    private static readonly Regex GpuKey = new(@"luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+_phys_\d+", RegexOptions.Compiled);
    private readonly object _gate = new();
    private Dictionary<string, CounterSample> _previous = new();
    private bool _failed;

    public double? GetUsagePercent()
    {
        lock (_gate)
        {
            if (_failed) return null;
            try
            {
                var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
                if (!data.Contains("Utilization Percentage")) { _failed = true; return null; }

                var next = new Dictionary<string, CounterSample>();
                var perGpu = new Dictionary<string, double>();
                foreach (DictionaryEntry entry in data["Utilization Percentage"])
                {
                    var name = (string)entry.Key;
                    var sample = ((InstanceData)entry.Value!).Sample;
                    next[name] = sample;
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!_previous.TryGetValue(name, out var before)) continue;

                    var match = GpuKey.Match(name);
                    if (!match.Success) continue;
                    var value = CounterSample.Calculate(before, sample);
                    perGpu[match.Value] = perGpu.GetValueOrDefault(match.Value) + value;
                }
                _previous = next;
                return perGpu.Count == 0 ? null : Math.Min(100, perGpu.Values.Max());
            }
            catch { _failed = true; return null; }
        }
    }
}
