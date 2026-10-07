using System.Globalization;

namespace AcerCareLite.Core.Monitoring;

public interface ICpuMonitor { double? GetUsagePercent(); }
public interface IGpuMonitor { double? GetUsagePercent(); }
public interface IMemoryMonitor { MemoryUsage? Read(); }
public interface IStorageMonitor { IReadOnlyList<DriveUsage> Read(); }
public interface IPowerMonitor { PowerStatus? Read(); }
public interface ISystemInfoProvider { SystemInfo? Read(); }

/// <summary>UI-agnostic timer so view models can be tested without a dispatcher.</summary>
public interface IPollingTimer
{
    event EventHandler? Tick;
    void Start(TimeSpan interval);
    void Stop();
}

public static class ByteFormat
{
    public static string Gb(ulong bytes) => (bytes / 1073741824.0).ToString("0.0", CultureInfo.CurrentCulture);
}

public sealed record MemoryUsage(ulong TotalBytes, ulong UsedBytes)
{
    public double Percent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
    public string Text => $"{ByteFormat.Gb(UsedBytes)} / {ByteFormat.Gb(TotalBytes)} GB";
}

public sealed record DriveUsage(string Name, ulong TotalBytes, ulong FreeBytes)
{
    public ulong UsedBytes => FreeBytes > TotalBytes ? 0 : TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
    public string Text => $"{ByteFormat.Gb(UsedBytes)} / {ByteFormat.Gb(TotalBytes)} GB used";
}

public sealed record PowerStatus(bool HasBattery, int? Percent, bool? OnAc, bool? Charging);

public sealed record SystemInfo(string Manufacturer, string Model, string BiosVersion, string OsDescription);
